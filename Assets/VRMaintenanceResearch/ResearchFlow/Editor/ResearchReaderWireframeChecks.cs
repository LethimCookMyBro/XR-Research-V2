#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    [InitializeOnLoad]
    public static class ResearchReaderWireframeChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Owner = -73411, Other = -73412;
        static readonly string[] Formats = { "user_manual", "step_by_step", "illustrated", "video" };
        static EditorApplication.CallbackFunction videoTick;
        static Action videoAbort;
        static bool exitingVideoPlay, reloadingFonts;
        static readonly HashSet<TMP_FontAsset> ownedFonts = new HashSet<TMP_FontAsset>();
        static readonly Dictionary<GameObject, HashSet<TMP_FontAsset>> fontDonors = new Dictionary<GameObject, HashSet<TMP_FontAsset>>();
        const string VideoSession = "ResearchReaderWireframeChecks.IsolatedVideo";
        static ResearchReaderWireframeChecks()
        {
            EditorApplication.playModeStateChanged += VideoPlayStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.quitting += () => RestoreVideoSession("Editor quitting", false);
            if (!string.IsNullOrEmpty(SessionState.GetString(VideoSession, "")))
            {
                KeepVideoXRDisabled();
                EditorApplication.delayCall += () =>
                {
                    var state = VideoState();
                    if (state != null && (string)state["stage"] == "native_video" && EditorApplication.isPlaying && videoTick == null)
                    {
                        if (string.IsNullOrEmpty((string)state["error"])) state["error"] = "Native video interrupted by assembly reload"; SaveVideoState(state);
                        EditorApplication.ExitPlaymode();
                    }
                    else if (state != null && !EditorApplication.isPlayingOrWillChangePlaymode)
                        RestoreVideoSession((string)state["stage"] == "restoring_editor" ? (string)state["error"] : "Reload outside the bound Play transition", true);
                };
            }
        }
        static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
        static T Public<T>(object fixture, string name) => (T)fixture.GetType().GetField(name).GetValue(fixture);
        static void Set(ResearchSessionFlowController controller, string name, object value) => controller.GetType().GetProperty(name).GetSetMethod(true).Invoke(controller, new[] { value });
        static object Viewer(ResearchFlowView view) => typeof(ResearchFlowView).GetProperty("ReferenceViewer")?.GetValue(view, null);
        static object Read(object target, string name) => target?.GetType().GetProperty(name)?.GetValue(target, null);
        static int Number(object target, string name) => Read(target, name) == null ? 0 : Convert.ToInt32(Read(target, name));
        static bool Flag(object target, string name) => Read(target, name) is bool value && value;
        static object Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name).Invoke(target, arguments);
        static JObject Metadata(object reader) => JObject.FromObject(Read(reader, "Metadata"));
        static int Page(ResearchFlowView view) => (int)typeof(ResearchFlowView).GetProperty("ReferencePageIndex", Hidden).GetValue(view);
        static void Flush(ResearchFlowView view)
        {
            Canvas.ForceUpdateCanvases();
            var scroll = Field<ScrollRect>(view, "scroll");
            typeof(ScrollRect).GetMethod("LateUpdate", Hidden).Invoke(scroll, null);
            foreach (var text in view.GetComponentsInChildren<TMP_Text>(true))
            {
                text.ForceMeshUpdate(true, true);
                // shortcut: global TMP fallbacks are detected after rendering for this fixed corpus; isolate arbitrary text before widening it.
                for (int i = 0; i < text.textInfo.characterCount; i++)
                {
                    var font = text.textInfo.characterInfo[i].fontAsset;
                    if (font && !ownedFonts.Contains(font)) throw new InvalidOperationException("Fixture rendered with an unowned font: " + font.name);
                }
            }
        }
        static object Capture(object fixture, string stage) => fixture.GetType().GetMethod("Capture").Invoke(fixture, new object[] { stage, Public<ResearchFlowView>(fixture, "View").referencePanel });
        static List<JObject> Events(ResearchSessionFlowController controller)
        {
            using (var reader = new StreamReader(new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                return reader.ReadToEnd().Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).Select(JObject.Parse).ToList();
        }
        static int BoundPages(object reader) =>
            JsonUtility.FromJson<ResearchReferenceViewer.Package>(Resources.Load<TextAsset>(Read(reader, "BoundRegistryResource").ToString()).text).pages.Length;
        static ResearchUIAction Action(ResearchFlowView view, string command)
        {
            var action = view.GetComponentsInChildren<ResearchUIAction>(true).First(a => a.command == command && a.gameObject.activeInHierarchy && a.GetComponent<Button>().IsInteractable());
            if (Field<Button>(action, "button") == null) typeof(ResearchUIAction).GetMethod("OnEnable", Hidden).Invoke(action, null);
            return action;
        }
        static GameObject Press(ResearchUIAction action, PointerEventData pointer) => ExecuteEvents.ExecuteHierarchy(action.GetComponentInChildren<TMP_Text>(true).gameObject, pointer, ExecuteEvents.pointerDownHandler);
        static void Release(GameObject owner, PointerEventData pointer)
        {
            ExecuteEvents.Execute(owner, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(owner, pointer, ExecuteEvents.pointerClickHandler);
        }
        static void Click(ResearchFlowView view, string command, PointerEventData pointer)
        {
            var owner = Press(Action(view, command), pointer); Release(owner, pointer); Flush(view);
        }
        static float[] Frame(ResearchFlowView view)
        {
            var scroll = Field<ScrollRect>(view, "scroll");
            var result = new List<float>();
            foreach (var rt in new[] { view.referencePanel.GetComponent<RectTransform>(), view.referencePanel.transform.Find("Expanded") as RectTransform,
                Field<TMP_Text>(view, "referenceTitle").rectTransform, scroll.GetComponent<RectTransform>() })
            { result.Add(rt.anchoredPosition.x); result.Add(rt.anchoredPosition.y); result.Add(rt.rect.width); result.Add(rt.rect.height); }
            foreach (var action in view.referencePanel.GetComponentsInChildren<ResearchUIAction>(true).Where(a => a.command.StartsWith("reference_tab.") || a.command == "reference_minimize" || a.command == "reference_close" || a.command == "pause").OrderBy(a => a.command))
            { var rt = action.GetComponent<RectTransform>(); result.Add(rt.anchoredPosition.x); result.Add(rt.anchoredPosition.y); result.Add(rt.rect.width); result.Add(rt.rect.height); }
            var handle = view.referencePanel.GetComponentsInChildren<ResearchDragHandle>(true).Single(h => h.panel == view.referencePanel).GetComponent<RectTransform>();
            result.Add(handle.anchoredPosition.x); result.Add(handle.anchoredPosition.y); result.Add(handle.rect.width); result.Add(handle.rect.height);
            return result.ToArray();
        }
        static bool Same(float[] a, float[] b) => a.Length == b.Length && a.Zip(b, (x, y) => Mathf.Abs(x - y)).All(delta => delta < .001f);
        static Rect BoundsIn(RectTransform rect, RectTransform owner)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Select(owner.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
        }
        static object BoundsValue(Rect rect) => new { x_min = rect.xMin, y_min = rect.yMin, x_max = rect.xMax, y_max = rect.yMax };
        static bool Overlap(Rect a, Rect b) => a.xMin < b.xMax - .1f && a.xMax > b.xMin + .1f && a.yMin < b.yMax - .1f && a.yMax > b.yMin + .1f;
        static string Hash(string path)
        {
            if (!File.Exists(path)) return null;
            using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        static ResearchFlowView FontDonor(out GameObject owned)
        {
            const string fonts = "Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/";
            var existing = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont && !fontDonors.ContainsKey(v.gameObject));
            var thai = existing ? existing.thaiFont : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fonts + "ResearchFlow_Thai.asset");
            var japanese = existing ? existing.japaneseFont : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fonts + "ResearchFlow_Japanese.asset");
            var english = existing ? existing.englishFont : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fonts + "ResearchFlow_English.asset");
            if (!thai || !japanese || !english) throw new InvalidOperationException("Authored TH/JP/EN font assets are unavailable.");
            var copies = new Dictionary<TMP_FontAsset, TMP_FontAsset>();
            var created = new HashSet<TMP_FontAsset>();
            owned = new GameObject("ReaderAuthoredFontDonor") { hideFlags = HideFlags.HideAndDontSave };
            fontDonors.Add(owned, created);
            TMP_FontAsset Clone(TMP_FontAsset source)
            {
                if (!source) return null;
                if (copies.TryGetValue(source, out var prior)) return prior;
                var clone = UnityEngine.Object.Instantiate(source);
                // TMP OnDestroy owns these children, including partial construction and dynamic atlas expansion.
                clone.atlasTextures = null; clone.material = null;
                created.Add(clone); ownedFonts.Add(clone); copies.Add(source, clone);
                clone.hideFlags = HideFlags.HideAndDontSave;
                typeof(TMP_FontAsset).GetField("m_AtlasTexture", Hidden).SetValue(clone, null);
                clone.atlasTextures = new Texture2D[source.atlasTextures.Length];
                for (int i = 0; i < source.atlasTextures.Length; i++)
                    if (source.atlasTextures[i])
                    {
                        clone.atlasTextures[i] = UnityEngine.Object.Instantiate(source.atlasTextures[i]);
                        clone.atlasTextures[i].hideFlags = HideFlags.HideAndDontSave;
                    }
                if (source.material)
                {
                    clone.material = UnityEngine.Object.Instantiate(source.material);
                    clone.material.hideFlags = HideFlags.HideAndDontSave;
                    int atlas = Array.IndexOf(source.atlasTextures, source.material.mainTexture);
                    clone.material.mainTexture = clone.atlasTextures[atlas < 0 ? 0 : atlas];
                }
                clone.fallbackFontAssetTable = source.fallbackFontAssetTable?.Select(Clone).ToList();
                var weights = (TMP_FontWeightPair[])source.fontWeightTable.Clone();
                typeof(TMP_FontAsset).GetField("m_FontWeightTable", Hidden).SetValue(clone, weights);
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i].regularTypeface = Clone(weights[i].regularTypeface);
                    weights[i].italicTypeface = Clone(weights[i].italicTypeface);
                }
                if (ReferenceEquals(source.glyphTable, clone.glyphTable) || ReferenceEquals(source.characterTable, clone.characterTable) ||
                    source.characterTable.Zip(clone.characterTable, (original, copy) => ReferenceEquals(original, copy)).Any(shared => shared))
                    throw new InvalidOperationException("TMP clone shares mutable font tables.");
                clone.ReadFontAssetDefinition();
                return clone;
            }
            try
            {
                owned.SetActive(false);
                var view = owned.AddComponent<ResearchFlowView>();
                view.thaiFont = Clone(thai); view.japaneseFont = Clone(japanese); view.englishFont = Clone(english);
                return view;
            }
            catch (Exception error)
            {
                try { ReleaseFontDonor(owned); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        }
        static void ReleaseFontDonor(GameObject owned)
        {
            if (ReferenceEquals(owned, null) || !fontDonors.TryGetValue(owned, out var fonts)) return;
            var errors = new List<Exception>();
            try { if (owned) UnityEngine.Object.DestroyImmediate(owned); }
            catch (Exception error) { errors.Add(error); }
            foreach (var font in fonts.ToArray())
            {
                try { if (font) UnityEngine.Object.DestroyImmediate(font); ownedFonts.Remove(font); fonts.Remove(font); }
                catch (Exception error) { errors.Add(error); }
            }
            if (fonts.Count == 0) fontDonors.Remove(owned);
            if (errors.Count != 0) throw new AggregateException("Fixture font cleanup failed.", errors);
        }
        static void BeforeAssemblyReload()
        {
            KeepVideoXRDisabled();
            reloadingFonts = true;
            try
            {
                var state = VideoState();
                if (state != null && (string)state["stage"] == "native_video")
                {
                    state["error"] = ((string)state["error"] ?? "") + "Native video interrupted by assembly reload";
                    SaveVideoState(state);
                    videoAbort?.Invoke();
                }
            }
            finally
            {
                foreach (var donor in fontDonors.Keys.ToArray())
                    try { ReleaseFontDonor(donor); }
                    catch (Exception error) { Debug.LogException(error); }
            }
        }

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("Stopped Editor without a running XR subsystem is required.");
            using var legacyPolicy = ResearchUIPolicy.LegacyRegressionScope();
            string directory = Path.GetFullPath(evidenceDirectory), path = Path.Combine(directory, "reader-wireframe.json");
            if (File.Exists(path)) throw new IOException("Existing reader evidence: " + path);
            Directory.CreateDirectory(directory);
            ResearchFlowView donor = null; GameObject ownedDonor = null;
            var type = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            var caller = SceneManager.GetActiveScene(); bool dirty = caller.isDirty; int previews = EditorSceneManager.previewSceneCount;
            var priorEvents = EventSystem.current;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(cursor => cursor, cursor => cursor.HoverColor);
            var rows = new List<object>(); var captures = new List<object>(); int failures = 0;
            void Check(bool pass, string name, object observed) { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            donor = FontDonor(out ownedDonor);
            // S1 content exists for Thai and Japanese only (content contract); English has no Reader package.
            foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
            {
                object fixture = null;
                try
                {
                    fixture = Activator.CreateInstance(type, new object[] { Path.Combine(directory, language.ToString()), donor, language });
                    var controller = Public<ResearchSessionFlowController>(fixture, "Controller"); var view = Public<ResearchFlowView>(fixture, "View"); var camera = Public<Camera>(fixture, "Camera");
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                    var timing = Field<ResearchTaskTiming>(controller, "taskTiming");
                    timing.Start(controller.Logger.Timestamp);
                    Check(timing.CanManipulate && (bool)controller.GetType().GetProperty("LocalUICommandsAllowed", Hidden).GetValue(controller), language + "/owned legacy fixture has an active task clock", null);
                    Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "user_manual");
                    type.GetMethod("Refresh").Invoke(fixture, null); Flush(view);
                    view.referencePanel.GetComponent<Canvas>().worldCamera = camera;
                    var scroll = Field<ScrollRect>(view, "scroll"); var reader = Viewer(view);
                    Check(reader != null && Flag(reader, "IsReady") && Number(reader, "PageCount") == BoundPages(reader), language + "/all mapped pages of the bound S1 package are decoded", new { viewer_present = reader != null, ready = Flag(reader, "IsReady"), status = Read(reader, "Status"), pages = Number(reader, "PageCount"), metadata = Read(reader, "Metadata") });
                    if (reader == null) continue;
                    var eventObject = new GameObject("ReaderOwnedEvents") { hideFlags = HideFlags.HideAndDontSave }; eventObject.SetActive(false);
                    SceneManager.MoveGameObjectToScene(eventObject, view.gameObject.scene); eventObject.transform.SetParent(view.transform, false);
                    var events = eventObject.AddComponent<EventSystem>(); var pointer = new PointerEventData(events) { pointerId = Owner, button = PointerEventData.InputButton.Left, useDragThreshold = false, eligibleForClick = true };
                    var baselineFrame = Frame(view); var baselinePose = view.referencePanel.transform.position;
                    var availability = Formats.ToDictionary(format => format, controller.ReferenceAvailable);
                    Check(availability.All(pair => pair.Value), language + "/all four labelled engineering formats are available", availability);
                    var pause = view.referencePanel.transform.Find("Expanded/Pause");
                    view.RefreshRecoveryUtility(); Check(pause && pause.gameObject.activeInHierarchy && pause.GetComponent<Button>().IsInteractable(), language + "/header session Pause is accessible", new { present = pause != null, visible = pause && pause.gameObject.activeInHierarchy });
                    var tabs = view.referencePanel.GetComponentsInChildren<ResearchUIAction>(true).Where(a => a.command.StartsWith("reference_tab.")).Select(a => a.GetComponent<RectTransform>()).ToArray();
                    Check(tabs.Length == 4 && tabs.Max(tab => tab.anchoredPosition.x) - tabs.Min(tab => tab.anchoredPosition.x) < 1 && tabs.Select(tab => Mathf.RoundToInt(tab.anchoredPosition.y)).Distinct().Count() == 4,
                        language + "/four format targets form one left sidebar", tabs.Select(tab => new { tab.name, x = tab.anchoredPosition.x, y = tab.anchoredPosition.y, width = tab.rect.width, height = tab.rect.height }).ToArray());
                    captures.Add(Capture(fixture, "PDF_top"));
                    PDFCallbacks(view, controller, reader, pointer, (pass, name, observed) => Check(pass, language + "/" + name, observed));
                    scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Flush(view);
                    pointer.scrollDelta = new Vector2(0, -240); ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.scrollHandler); Flush(view);
                    Check(scroll.verticalNormalizedPosition < .999f && scroll.content.rect.height > scroll.viewport.rect.height, language + "/actual ScrollRect scroll callback moves the continuous original-page document", new { normalized = scroll.verticalNormalizedPosition, content_height = scroll.content.rect.height, viewport_height = scroll.viewport.rect.height, page = Page(view) });
                    Check(Same(baselineFrame, Frame(view)) && Vector3.Distance(baselinePose, view.referencePanel.transform.position) < .001f, language + "/continuous scroll leaves the shell and world pose unchanged", new { before = baselineFrame, after = Frame(view) });
                    captures.Add(Capture(fixture, "PDF_scrolled"));
                    float saved = scroll.verticalNormalizedPosition;
                    foreach (var format in Formats.Skip(1))
                    {
                        var oldMedia = JObject.Parse(Metadata(reader).ToString(Formatting.None)); string oldSection = controller.ReferenceSection; int beforeEvents = Events(controller).Count;
                        string exposure = Field<string>(controller, "exposureAction");
                        var exposureOpened = Events(controller).LastOrDefault(row => (string)row["event"] == "INFORMATION_OPEN" && (string)row["action_id"] == exposure);
                        Click(view, "reference_tab." + format, pointer);
                        var changes = Events(controller).Skip(beforeEvents).ToArray();
                        var selected = changes.SingleOrDefault(row => (string)row["event"] == "INFORMATION_FORMAT_SELECTED");
                        Check(selected != null && (string)selected["before"] == oldSection && (string)selected["target"] == "reference." + format
                            && (string)selected["source"] == "Tester" && (string)selected["input_source"] == "None" && (string)selected["task_id"] == "A"
                            && (string)selected["after"]?["reference_scheme"] == ResearchReferenceScheme.Name && (string)selected["after"]?["format"] == format
                            && (bool?)selected["after"]?["deliberate"] == true && (string)selected["after"]?["selection_kind"] == "deliberate"
                            && (string)selected["after"]?["language"] == language.ToString() && (bool?)selected["after"]?["incoming_binding_pending"] == true
                            && selected["after"]?["viewer"] == null && JToken.DeepEquals(oldMedia, selected["after"]?["viewer_before_action"]),
                            language + "/" + format + "/deliberate intent preserves the outgoing binding without claiming incoming content", new { oldSection, oldMedia, selected });
                        var exposureClosed = changes.Where(row => (string)row["event"] == "INFORMATION_CLOSE").ToArray();
                        var close = exposureClosed.SingleOrDefault();
                        Check(exposure == null ? exposureClosed.Length == 0 : close != null && (string)close["action_id"] == exposure
                            && (string)close["phase"] == (string)exposureOpened?["phase"] && (string)close["task_id"] == (string)exposureOpened?["task_id"]
                            && (string)close["target"] == "reference." + oldSection && (string)close["result"] == "tab_changed" && (string)close["reason"] == "tab_changed"
                            && (string)close["source"] == "Tester" && (string)close["input_source"] == "None"
                            && (double?)close["after"]?["exposure_seconds"] >= 0 && (string)close["after"]?["measurement"] == "panel_exposure_not_eye_gaze"
                            && (string)close["after"]?["opening_input_source"] == (string)exposureOpened?["input_source"]
                            && (string)close["after"]?["language"] == language.ToString() && (string)close["after"]?["reference_section"] == oldSection
                            && JToken.DeepEquals(oldMedia, close["after"]?["viewer_at_action"]),
                            language + "/" + format + "/exposure close retains duration identity and outgoing binding with an explicit technical reason", new { exposureOpened, oldMedia, exposureClosed });
                        var incoming = changes.LastOrDefault(row => (string)row["event"] == "INFORMATION_OPEN"); var materialized = Metadata(reader);
                        Check(format == "video" ? incoming == null && !Flag(reader, "IsReady") : incoming != null
                            && (string)incoming["source"] == "Tester" && (string)incoming["input_source"] == "None" && (string)incoming["task_id"] == "A"
                            && (string)incoming["after"]?["language"] == language.ToString() && (string)incoming["after"]?["reference_section"] == format
                            && (string)incoming["after"]?["selection_kind"] == "deliberate" && (bool?)incoming["after"]?["actual_content_visible"] == true
                            && (string)incoming["after"]?["viewer"]?["context"] == (string)materialized["context"]
                            && (string)incoming["after"]?["viewer"]?["media_type"] == (string)materialized["media_type"]
                            && (string)incoming["after"]?["viewer"]?["content_id"] == (string)materialized["content_id"]
                            && (string)incoming["after"]?["viewer"]?["version"] == (string)materialized["version"],
                            language + "/" + format + "/materialized exposure has the incoming content and Loading has none", new { incoming, materialized });
                        Check(controller.Flow.State == ResearchFlowState.Task1Active && controller.Flow.TutorialCompleted == 5
                            && !changes.Any(row => new[] { "TUTORIAL_SKILL_CREDIT", "TASK_COMPLETED", "INSPECT", "REPAIR" }.Contains((string)row["event"])),
                            language + "/" + format + "/deliberate media selection cannot credit repair or advance the Task", changes);
                        var closed = changes.LastOrDefault(row => (string)row["event"] == "REFERENCE_VIEWER_ACTIVE_END");
                        var opened = changes.LastOrDefault(row => (string)row["event"] == "REFERENCE_VIEWER_ACTIVE_BEGIN");
                        var closedMedia = closed?["after"]?["viewer"];
                        checkActiveBinding(closed != null && (string)closed["target"] == "reference." + oldSection && (string)closedMedia?["context"] == (string)oldMedia["context"] && (string)closedMedia?["media_type"] == (string)oldMedia["media_type"] && (double?)closed["after"]?["active_seconds"] >= 0,
                            "old successful-reading interval ends on its actual media binding", new { oldSection, oldMedia, closed });
                        checkActiveBinding(format == "video" ? opened == null : opened != null && (string)opened["target"] == "reference." + format && (string)opened["after"]?["context"] == (string)Metadata(reader)["context"] && (string)opened["after"]?["media_type"] == (string)Metadata(reader)["media_type"],
                            "new reading interval is correctly attributed, and Loading never starts one", new { format, opened, current = Metadata(reader) });
                        void checkActiveBinding(bool pass, string name, object observed) => Check(pass, language + "/" + format + "/" + name, observed);
                        Check(controller.ReferenceSection == format && Same(baselineFrame, Frame(view)), language + "/" + format + " uses the same fixed header/sidebar/viewport/grip", new { controller.ReferenceSection, before = baselineFrame, after = Frame(view), status = Read(reader, "Status"), ready = Flag(reader, "IsReady") });
                        if (format != "video") Check(Flag(reader, "IsReady"), language + "/" + format + " has real content", new { status = Read(reader, "Status"), metadata = Read(reader, "Metadata") });
                        captures.Add(Capture(fixture, format == "video" ? "Video_loading" : format == "illustrated" ? "Images" : "Steps"));
                    }
                    Click(view, "reference_tab.user_manual", pointer);
                    Check(Mathf.Abs(saved - scroll.verticalNormalizedPosition) < .001f, language + "/format return restores the PDF offset", new { saved, restored = scroll.verticalNormalizedPosition });
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task2Active, 5)); view.Refresh(); Flush(view);
                    Check(Mathf.Abs(scroll.verticalNormalizedPosition - 1) < .001f, language + "/a new task starts with its own document offset", scroll.verticalNormalizedPosition);
                    scroll.verticalNormalizedPosition = .7f; Flush(view);
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5)); view.Refresh(); Flush(view);
                    Check(Mathf.Abs(saved - scroll.verticalNormalizedPosition) < .001f, language + "/returning to Task A restores only Task A offset", new { saved, restored = scroll.verticalNormalizedPosition });
                    var alternate = language == ParticipantLanguage.Thai ? ParticipantLanguage.Japanese : ParticipantLanguage.Thai;
                    Set(controller, "SetupLanguage", alternate); view.Refresh(); Flush(view);
                    Check(Mathf.Abs(scroll.verticalNormalizedPosition - 1) < .001f, language + "/different language cannot inherit the previous language offset", new { alternate, normalized = scroll.verticalNormalizedPosition });
                    scroll.verticalNormalizedPosition = .6f; Flush(view);
                    Set(controller, "SetupLanguage", language); view.Refresh(); Flush(view);
                    Check(Mathf.Abs(saved - scroll.verticalNormalizedPosition) < .001f, language + "/returning to the original language restores its offset", new { saved, restored = scroll.verticalNormalizedPosition });
                    var beforeMinimizeViewer = JObject.Parse(Metadata(reader).ToString(Newtonsoft.Json.Formatting.None)); int beforeMinimizeEvents = Events(controller).Count;
                    Click(view, "reference_minimize", pointer); Check(controller.ReferenceMinimized, language + "/native button callbacks minimize", controller.ReferenceMinimized);
                    var minimizedEvents = Events(controller).Skip(beforeMinimizeEvents).Where(row => (string)row["event"] == "PANEL_MINIMIZE").ToArray();
                    var minimized = minimizedEvents.LastOrDefault(); var minimizedAfter = minimized?["after"] as JObject;
                    Check(minimizedEvents.Length == 1 && (bool?)minimized?["before"] == false && (bool?)minimizedAfter?["reference_minimized"] == true
                        && (string)minimized?["target"] == "reference" && (string)minimized?["result"] == "Minimized" && (string)minimized?["task_id"] == "A"
                        && (string)minimized?["source"] == "Tester" && (string)minimized?["input_source"] == "None"
                        && (string)minimizedAfter?["language"] == language.ToString() && (string)minimizedAfter?["reference_section"] == "user_manual"
                        && !string.IsNullOrEmpty((string)beforeMinimizeViewer["content_id"]) && !string.IsNullOrEmpty((string)beforeMinimizeViewer["version"])
                        && !string.IsNullOrEmpty((string)beforeMinimizeViewer["context"]) && !string.IsNullOrEmpty((string)beforeMinimizeViewer["source_sha256"])
                        && !string.IsNullOrEmpty((string)beforeMinimizeViewer["registry_sha256"]) && (string)beforeMinimizeViewer["media_type"] == "pdf"
                        && JToken.DeepEquals(beforeMinimizeViewer, minimizedAfter?["viewer_at_action"]),
                        language + "/minimize records one technical panel transition with its pre-refresh viewer binding",
                        new { expected = new { before = false, reference_minimized = true, source = "Tester", input_source = "None", language = language.ToString(), task_id = "A", reference_section = "user_manual", viewer_at_action = beforeMinimizeViewer }, events = minimizedEvents });
                    captures.Add(Capture(fixture, "Minimized"));
                    var beforeRestoreViewer = JObject.Parse(Metadata(reader).ToString(Newtonsoft.Json.Formatting.None)); int beforeRestoreEvents = Events(controller).Count;
                    Click(view, "reference_restore", pointer); Check(!controller.ReferenceMinimized && Mathf.Abs(saved - scroll.verticalNormalizedPosition) < .001f, language + "/restore retains offset without autoplay", new { controller.ReferenceMinimized, saved, restored = scroll.verticalNormalizedPosition });
                    var restoredEvents = Events(controller).Skip(beforeRestoreEvents).Where(row => (string)row["event"] == "PANEL_RESTORE").ToArray();
                    var restored = restoredEvents.LastOrDefault(); var restoredAfter = restored?["after"] as JObject;
                    Check(restoredEvents.Length == 1 && (bool?)restored?["before"] == true && (bool?)restoredAfter?["reference_minimized"] == false
                        && (string)restored?["target"] == "reference" && (string)restored?["result"] == "Restored" && (string)restored?["task_id"] == "A"
                        && (string)restored?["source"] == "Tester" && (string)restored?["input_source"] == "None"
                        && (string)restoredAfter?["language"] == language.ToString() && (string)restoredAfter?["reference_section"] == "user_manual"
                        && !string.IsNullOrEmpty((string)beforeRestoreViewer["content_id"]) && !string.IsNullOrEmpty((string)beforeRestoreViewer["version"])
                        && !string.IsNullOrEmpty((string)beforeRestoreViewer["context"]) && !string.IsNullOrEmpty((string)beforeRestoreViewer["source_sha256"])
                        && !string.IsNullOrEmpty((string)beforeRestoreViewer["registry_sha256"]) && (string)beforeRestoreViewer["media_type"] == "pdf"
                        && JToken.DeepEquals(beforeRestoreViewer, restoredAfter?["viewer_at_action"]),
                        language + "/restore records one technical panel transition with its pre-refresh viewer binding",
                        new { expected = new { before = true, reference_minimized = false, source = "Tester", input_source = "None", language = language.ToString(), task_id = "A", reference_section = "user_manual", viewer_at_action = beforeRestoreViewer }, events = restoredEvents });
                    captures.Add(Capture(fixture, "Restored"));
                    Click(view, "reference_tab.step_by_step", pointer);
                    int beforeHeadClick = Page(view); var next = Action(view, "reference_next"); var pressOwner = Press(next, pointer);
                    var head = camera.transform.position; var rotation = camera.transform.rotation;
                    camera.transform.SetPositionAndRotation(head + new Vector3(.02f, .01f, 0), rotation * Quaternion.Euler(0, 2, 0));
                    view.NotifyReferenceVisibility("synthetic_ordinary_head_motion");
                    Release(pressOwner, pointer); Flush(view); int afterHeadClick = Page(view);
                    ExecuteEvents.Execute(pressOwner, pointer, ExecuteEvents.pointerClickHandler); Flush(view);
                    Check(afterHeadClick == beforeHeadClick + 1 && Page(view) == afterHeadClick, language + "/down head-motion up accepts exactly once", new { before = beforeHeadClick, after = afterHeadClick, after_duplicate_click = Page(view), events = Events(controller).Where(row => (string)row["event"] == "UI_CLICK_REJECTED").ToArray() });
                    camera.transform.SetPositionAndRotation(head, rotation);
                    next = Action(view, "reference_next"); pressOwner = Press(next, pointer); Click(view, "reference_tab.illustrated", new PointerEventData(events) { pointerId = Other, button = PointerEventData.InputButton.Left });
                    int beforeStale = Page(view); Release(pressOwner, pointer); Flush(view);
                    var rejection = Events(controller).LastOrDefault(row => (string)row["event"] == "UI_CLICK_REJECTED" && (string)row["target"] == "reference_next");
                    Check(Page(view) == beforeStale && rejection != null && (string)rejection["reason"] == "context_changed", language + "/down actual tab-change up rejects stale acquisition", new { beforeStale, after = Page(view), rejection });
                    Click(view, "reference_tab.step_by_step", pointer); int beforeFresh = Page(view); Click(view, "reference_next", pointer);
                    Check(Page(view) == beforeFresh + 1, language + "/fresh acquisition works after actual stale rejection", new { beforeFresh, after = Page(view) });
                    var previous = Action(view, "reference_previous"); pressOwner = Press(previous, pointer);
                    Click(view, "reference_previous", new PointerEventData(events) { pointerId = Other, button = PointerEventData.InputButton.Left });
                    int boundaryIndex = Page(view); Release(pressOwner, pointer); Flush(view);
                    rejection = Events(controller).LastOrDefault(row => (string)row["event"] == "UI_CLICK_REJECTED" && (string)row["target"] == "reference_previous");
                    Check(Page(view) == boundaryIndex && rejection != null && (string)rejection["reason"] == "context_changed", language + "/down actual index-change up rejects stale acquisition", new { boundaryIndex, after = Page(view), rejection });
                    Click(view, "reference_next", pointer); Check(Page(view) == beforeFresh + 1, language + "/fresh index acquisition restores its requested page", new { expected = beforeFresh + 1, actual = Page(view) });
                    int heldPage = Page(view); Click(view, "reference_previous", pointer); Flush(view);
                    next = Action(view, "reference_next"); pressOwner = Press(next, pointer);
                    string originalContext = (string)Metadata(reader)["context"];
                    Invoke(reader, "Bind", "TEST/replaced-document/" + language, "steps", true, ""); Invoke(reader, "SetVisible", true); Flush(view);
                    int replacedIndex = Page(view); Release(pressOwner, pointer); Flush(view);
                    rejection = Events(controller).LastOrDefault(row => (string)row["event"] == "UI_CLICK_REJECTED" && (string)row["target"] == "reference_next");
                    Check(Page(view) == replacedIndex && rejection != null && (string)rejection["reason"] == "context_changed", language + "/down document replacement up rejects stale release", new { old_context = originalContext, new_context = Metadata(reader)["context"], replacedIndex, after = Page(view), rejection });
                    Invoke(reader, "Bind", originalContext, "steps", true, ""); Invoke(reader, "SetVisible", true); Flush(view);
                    Check(Page(view) == heldPage - 1, language + "/document return restores its own step index", new { expected = heldPage - 1, observed = Page(view) });
                    view.Refresh(); Flush(view); Click(view, "reference_previous", pointer);
                    int beforeDisabled = Page(view); next = Action(view, "reference_next"); pressOwner = Press(next, pointer);
                    next.GetComponent<Button>().interactable = false; Release(pressOwner, pointer); Flush(view);
                    next.GetComponent<Button>().interactable = true;
                    ExecuteEvents.Execute(pressOwner, pointer, ExecuteEvents.pointerClickHandler); Flush(view);
                    Check(Page(view) == beforeDisabled, language + "/release while disabled cannot revive as a stale click after reenabling", new { beforeDisabled, after = Page(view), next.PointerHeld });
                    Click(view, "reference_next", pointer);
                    Check(Page(view) == beforeDisabled + 1, language + "/fresh click after reenabling is accepted", new { beforeDisabled, after = Page(view) });
                    Click(view, "reference_tab.user_manual", pointer);
                    var focus = view.referencePanel.GetComponent<ResearchSlateFocus>();
                    float beforeFocus = scroll.verticalNormalizedPosition; focus.Scroll(-1, .5f); Check(Mathf.Abs(scroll.verticalNormalizedPosition - beforeFocus) < .001f, language + "/unfocused thumbstick cannot scroll", scroll.verticalNormalizedPosition);
                    focus.OnPointerEnter(pointer); focus.Scroll(-1, .5f); Flush(view); Check(focus.Focused && scroll.verticalNormalizedPosition < beforeFocus, language + "/existing focused thumbstick consumer scrolls", new { focus.Focused, beforeFocus, after = scroll.verticalNormalizedPosition });
                    focus.OnPointerExit(pointer); float afterFocus = scroll.verticalNormalizedPosition; focus.Scroll(-1, .5f); Flush(view); Check(!focus.Focused && Mathf.Abs(afterFocus - scroll.verticalNormalizedPosition) < .001f, language + "/focus exit releases scrolling ownership", new { focus.Focused, afterFocus, after = scroll.verticalNormalizedPosition });
                    Check(Number(reader, "CachedPageCount") > 0 && Number(reader, "CachedPageCount") <= 3, language + "/decoded PDF cache stays bounded to current plus neighbours", new { cached = Number(reader, "CachedPageCount"), pages = Number(reader, "PageCount") });
                    ContentAndGrip(view, camera, events, pointer, (pass, name, observed) => Check(pass, language + "/" + name, observed));
                    captures.Add(Capture(fixture, "Owned_grip_drag"));
                    ReaderFailures(view, reader, fixture, pointer, captures, (pass, name, observed) => Check(pass, language + "/" + name, observed));
                    controller.CloseReference("fixture_transition", "System"); Flush(view);
                    var outgoingTaskViewer = JObject.Parse(Metadata(reader).ToString(Formatting.None)); int beforeTutorialOpen = Events(controller).Count;
                    TutorialCueAndFeedback(fixture, type, view, controller, captures, (pass, name, observed) => Check(pass, language + "/" + name, observed));
                    var tutorialOpenEvents = Events(controller).Skip(beforeTutorialOpen).ToArray();
                    var referenceOpened = tutorialOpenEvents.SingleOrDefault(row => (string)row["event"] == "REFERENCE_OPEN");
                    var tutorialOpened = tutorialOpenEvents.LastOrDefault(row => (string)row["event"] == "INFORMATION_OPEN");
                    var tutorialBound = tutorialOpenEvents.LastOrDefault(row => (string)row["event"] == "REFERENCE_VIEWER_BOUND");
                    string tutorialContext = "fixture::" + language + ":Tutorial:user_manual";
                    Check(referenceOpened != null && (bool?)referenceOpened["before"] == false && (string)referenceOpened["task_id"] == "training"
                        && (string)referenceOpened["source"] == "Tester" && (string)referenceOpened["input_source"] == "None"
                        && (bool?)referenceOpened["after"]?["reference_open"] == true && (bool?)referenceOpened["after"]?["reference_minimized"] == false
                        && (string)referenceOpened["after"]?["language"] == language.ToString() && (string)referenceOpened["after"]?["reference_section"] == "user_manual"
                        && (bool?)referenceOpened["after"]?["incoming_binding_pending"] == true && referenceOpened["after"]?["viewer"] == null
                        && (string)outgoingTaskViewer["context"] != tutorialContext && tutorialBound != null && (string)tutorialBound["after"]?["context"] == tutorialContext
                        && (long?)referenceOpened["seq"] < (long?)tutorialBound["seq"],
                        language + "/Tutorial open intent cannot advertise the stale Task viewer as incoming materialized content", new { outgoingTaskViewer, referenceOpened, tutorialBound });
                    Check(tutorialOpened != null && (string)tutorialOpened["source"] == "System" && (string)tutorialOpened["input_source"] == "None"
                        && (string)tutorialOpened["task_id"] == "training" && (string)tutorialOpened["after"]?["selection_kind"] == "automatic_default"
                        && (string)tutorialOpened["after"]?["language"] == language.ToString() && (string)tutorialOpened["after"]?["reference_section"] == "user_manual"
                        && (string)tutorialOpened["after"]?["viewer"]?["context"] == tutorialContext
                        && (string)tutorialOpened["after"]?["viewer"]?["content_id"] == (string)Metadata(reader)["content_id"]
                        && (string)tutorialOpened["after"]?["viewer"]?["version"] == (string)Metadata(reader)["version"]
                        && !tutorialOpenEvents.Any(row => (string)row["event"] == "INFORMATION_FORMAT_SELECTED"),
                        language + "/automatic Tutorial exposure keeps System attribution and actual incoming content, not a deliberate choice", new { tutorialOpened, tutorialBound });
                    var beforeCloseViewer = JObject.Parse(Metadata(reader).ToString(Formatting.None)); int beforeClose = Events(controller).Count;
                    var creditsBeforeClose = Field<HashSet<string>>(controller, "tutorialEvidence").ToArray(); int completedBeforeClose = controller.Flow.TutorialCompleted;
                    Click(view, "reference_close", pointer);
                    var closeEvents = Events(controller).Skip(beforeClose).ToArray(); var referenceClosed = closeEvents.SingleOrDefault(row => (string)row["event"] == "REFERENCE_CLOSE");
                    var informationClosed = closeEvents.SingleOrDefault(row => (string)row["event"] == "INFORMATION_CLOSE");
                    Check(referenceClosed != null && (bool?)referenceClosed["before"]?["reference_open"] == true && (bool?)referenceClosed["before"]?["reference_minimized"] == false
                        && (bool?)referenceClosed["after"]?["reference_open"] == false && (bool?)referenceClosed["after"]?["reference_minimized"] == false
                        && (string)referenceClosed["source"] == "Tester" && (string)referenceClosed["input_source"] == "None" && (string)referenceClosed["reason"] == "closed"
                        && (string)referenceClosed["after"]?["language"] == language.ToString() && (string)referenceClosed["after"]?["reference_section"] == "user_manual"
                        && JToken.DeepEquals(beforeCloseViewer, referenceClosed["after"]?["viewer_at_action"])
                        && informationClosed != null && (string)informationClosed["source"] == "Tester" && (string)informationClosed["reason"] == "closed"
                        && (string)informationClosed["task_id"] == "training" && (string)informationClosed["action_id"] == (string)tutorialOpened?["action_id"]
                        && JToken.DeepEquals(beforeCloseViewer, informationClosed["after"]?["viewer_at_action"])
                        && (long?)referenceClosed["seq"] < (long?)informationClosed["seq"]
                        && controller.Flow.TutorialCompleted == completedBeforeClose && Field<HashSet<string>>(controller, "tutorialEvidence").SetEquals(creditsBeforeClose),
                        language + "/actual technical Close logs outgoing bindings before concealment without extra Tutorial credit", new { beforeCloseViewer, closeEvents, creditsBeforeClose });
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task2Active, 5)); view.Refresh(); Flush(view);
                    int beforeUnchosen = Events(controller).Count; controller.OpenReference("None"); Flush(view);
                    var unchosenViewer = JObject.Parse(Metadata(reader).ToString(Formatting.None)); Click(view, "reference_close", pointer);
                    var unchosenEvents = Events(controller).Skip(beforeUnchosen).ToArray(); var unchosenClosed = unchosenEvents.SingleOrDefault(row => (string)row["event"] == "REFERENCE_CLOSE");
                    Check(controller.ReferenceSection == "" && !controller.ReferenceOpen && !Flag(reader, "IsReady") && (string)unchosenViewer["content_id"] == null
                        && unchosenClosed != null && (string)unchosenClosed["source"] == "Tester" && (string)unchosenClosed["reason"] == "closed"
                        && (string)unchosenClosed["task_id"] == "B" && (string)unchosenClosed["after"]?["reference_section"] == ""
                        && JToken.DeepEquals(unchosenViewer, unchosenClosed["after"]?["viewer_at_action"])
                        && !unchosenEvents.Any(row => new[] { "INFORMATION_OPEN", "INFORMATION_CLOSE", "INFORMATION_FORMAT_SELECTED", "TUTORIAL_SKILL_CREDIT", "TASK_COMPLETED" }.Contains((string)row["event"]))
                        && controller.Flow.State == ResearchFlowState.Task2Active && controller.Flow.TutorialCompleted == 5,
                        language + "/close without visible exposure still logs REFERENCE_CLOSE and never invents Task B format choice or credit", new { unchosenViewer, unchosenEvents });
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                    Set(controller, "ReferenceSection", "user_manual"); controller.OpenReference("None"); Flush(view);
                    var beforePauseViewer = JObject.Parse(Metadata(reader).ToString(Formatting.None)); int beforePause = Events(controller).Count;
                    Click(view, "pause", pointer); Check(controller.Flow.Paused, language + "/header Pause pauses the session", controller.Flow.Paused);
                    var pauseEvents = Events(controller).Skip(beforePause).ToArray(); var pauseClosed = pauseEvents.SingleOrDefault(row => (string)row["event"] == "REFERENCE_CLOSE");
                    var pauseExposure = pauseEvents.SingleOrDefault(row => (string)row["event"] == "INFORMATION_CLOSE");
                    Check(pauseClosed != null && (string)pauseClosed["source"] == "System" && (string)pauseClosed["reason"] == "safety_pause"
                        && JToken.DeepEquals(beforePauseViewer, pauseClosed["after"]?["viewer_at_action"])
                        && pauseExposure != null && (string)pauseExposure["source"] == "System" && (string)pauseExposure["reason"] == "safety_pause"
                        && (string)pauseExposure["task_id"] == "A" && (string)pauseExposure["phase"] == "Experiment"
                        && (string)pauseExposure["after"]?["language"] == language.ToString() && (string)pauseExposure["after"]?["reference_section"] == "user_manual"
                        && JToken.DeepEquals(beforePauseViewer, pauseExposure["after"]?["viewer_at_action"]),
                        language + "/system pause preserves System attribution and the outgoing exposure context", new { beforePauseViewer, pauseEvents });
                    captures.Add(Capture(fixture, "Session_pause"));
                }
                catch (Exception error) { Check(false, language + "/arrangement or execution error", error.ToString()); }
                finally
                {
                    try { if (fixture != null) ((IDisposable)fixture).Dispose(); }
                    catch (Exception error) { Check(false, language + "/fixture cleanup error", error.ToString()); }
                    finally { if (priorEvents) EventSystem.current = priorEvents; }
                }
            }
            foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value;
            try { ReleaseFontDonor(ownedDonor); }
            catch (Exception error) { Check(false, "font cleanup error", error.ToString()); }
            Check(SceneManager.GetActiveScene() == caller && caller.isDirty == dirty && EditorSceneManager.previewSceneCount == previews && EventSystem.current == priorEvents && !EditorApplication.isPlaying,
                "canonical scene, dirty flag, preview count, EventSystem and stopped Editor preserved", new { caller.path, dirty, current_dirty = caller.isDirty, previews, current_previews = EditorSceneManager.previewSceneCount });
            string summary = failures == 0 ? "READER_WIREFRAME_EDITOR_PASS" : "READER_WIREFRAME_EDITOR_FAIL";
            string json = JsonConvert.SerializeObject(new { summary, failures, rows, captures, hardware_verified = false,
                pending = new[] { "Actual native VideoPlayer prepare/play/frame/seek/ended requires a separate Play-mode check; the stopped fixture captures actual Loading only." },
                scope = "Legacy-policy regression of real View and pointer/ScrollRect/PointableCanvasModule callbacks in isolated stopped-Editor previews. Owned TEST writer and synthetic mono camera/head/pointer inputs. Not current-policy workflow, native ray acquisition, Play lifecycle, Quest/stereo/wearer, participant data or approvals." }, Formatting.Indented);
            File.WriteAllText(path, json); return json;
        }

        static void PDFCallbacks(ResearchFlowView view, ResearchSessionFlowController controller, object reader, PointerEventData pointer, Action<bool, string, object> check)
        {
            var scroll = Field<ScrollRect>(view, "scroll"); var label = Field<TMP_Text>(view, "pageLabel");
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Flush(view);
            int before = Page(view), count = Events(controller).Count;
            Click(view, "reference_next", pointer);
            var events = Events(controller).Skip(count).Where(row => (string)row["event"] == "REFERENCE_VIEWER_NAVIGATE" || (string)row["event"] == "REFERENCE_VIEWER_PAGE_BOUNDARY").ToArray();
            check(Page(view) == before + 1 && events.Any(row => (string)row["event"] == "REFERENCE_VIEWER_NAVIGATE") && events.All(row => (string)row["source"] == "Tester" && (string)row["input_source"] == "None"),
                "actual PDF Next/page-boundary retains engineering Tester and literal None input", new { before, page = Page(view), events });
            count = Events(controller).Count; Click(view, "reference_previous", pointer);
            events = Events(controller).Skip(count).Where(row => (string)row["event"] == "REFERENCE_VIEWER_NAVIGATE" || (string)row["event"] == "REFERENCE_VIEWER_PAGE_BOUNDARY").ToArray();
            check(Page(view) == before && events.Any(row => (string)row["event"] == "REFERENCE_VIEWER_NAVIGATE") && events.All(row => (string)row["source"] == "Tester" && (string)row["input_source"] == "None"),
                "actual PDF Previous/page-boundary does not fabricate a native or User actor", new { before, page = Page(view), events });
            count = Events(controller).Count;
            float boundaryDelta = (scroll.content.rect.height / Number(reader, "PageCount") + scroll.viewport.rect.height) / Mathf.Max(.001f, scroll.scrollSensitivity);
            pointer.scrollDelta = new Vector2(0, -boundaryDelta); ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.scrollHandler); Flush(view);
            var boundary = Events(controller).Skip(count).LastOrDefault(row => (string)row["event"] == "REFERENCE_VIEWER_PAGE_BOUNDARY");
            string expected = (string)Read(reader, "NavigationLabel");
            check(Page(view) > before && boundary != null && label.GetParsedText() == expected && view.referencePanel.GetComponentsInChildren<ResearchUIAction>(true).First(a => a.command == "reference_previous").GetComponent<Button>().interactable,
                "compact PDF fraction and Previous update on the actual ScrollRect page boundary without a View Refresh", new { before, page = Page(view), expected, actual = label.GetParsedText(), boundary });
            check(boundary != null && (string)boundary["source"] == "Tester" && (string)boundary["input_source"] == "None", "synthetic ScrollRect boundary has honest Tester/None provenance", boundary);
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Flush(view);
        }

        static void TutorialCueAndFeedback(object fixture, Type type, ResearchFlowView view, ResearchSessionFlowController controller, List<object> captures, Action<bool, string, object> check)
        {
            Set(controller, "ReferenceOpen", false); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "user_manual");
            type.GetMethod("ChangeFlow").Invoke(fixture, new object[] { ResearchFlowState.TutorialReferenceInspect, 3 }); Flush(view);
            long armed = controller.TutorialInstructionEpoch; int start = Events(controller).Count;
            controller.OpenReference("None"); Flush(view);
            var records = Events(controller).Skip(start).ToArray();
            var credit = records.LastOrDefault(row => (string)row["event"] == "TUTORIAL_SKILL_CREDIT" && (string)row["target"] == "training.reference");
            var applied = records.LastOrDefault(row => (string)row["event"] == "TUTORIAL_FEEDBACK_APPLIED" && (string)row["target"] == "tutorial.reference");
            check(credit != null && applied != null && !string.IsNullOrEmpty((string)credit["action_id"]) && (string)applied["action_id"] == (string)credit["action_id"]
                && (long?)applied["after"]?["instruction_epoch"] == (long?)credit["after"]?["instruction_epoch"] && (long?)credit["after"]?["instruction_epoch"] == armed
                && applied["after"]?["human_seen"]?.Type == JTokenType.Null,
                "renderer-applied feedback links the actual accepted credit action_id and armed epoch, not a later prompt or human acknowledgement", new { armed, credit, applied });
            string action = (string)credit?["action_id"]; int markerCount = Events(controller).Count(row => (string)row["event"] == "TUTORIAL_FEEDBACK_APPLIED" && (string)row["action_id"] == action);
            view.Refresh(); Flush(view); view.Refresh(); Flush(view);
            check(action != null && markerCount == 1 && Events(controller).Count(row => (string)row["event"] == "TUTORIAL_FEEDBACK_APPLIED" && (string)row["action_id"] == action) == 1,
                "repeated renderer Refresh does not duplicate or fabricate feedback-applied markers", new { action, markerCount });
            var expanded = view.referencePanel.transform.Find("Expanded") as RectTransform;
            var cue = Field<TMP_Text>(view, "referenceTutorialCue"); var progress = Field<TMP_Text>(view, "referenceTutorialProgress"); var viewport = Field<ScrollRect>(view, "scroll").viewport;
            var cueBounds = BoundsIn(cue.rectTransform, expanded); var progressBounds = BoundsIn(progress.rectTransform, expanded); var viewportBounds = BoundsIn(viewport, expanded);
            check(cue.gameObject.activeInHierarchy && progress.gameObject.activeInHierarchy && !Overlap(cueBounds, viewportBounds) && !Overlap(progressBounds, viewportBounds),
                "actual Tutorial cue and progress reserved rects do not overlap the fixed viewer viewport", new { cue = BoundsValue(cueBounds), progress = BoundsValue(progressBounds), viewport = BoundsValue(viewportBounds), cue_text = cue.GetParsedText() });
            captures.Add(Capture(fixture, "Tutorial_actual_credit_cue"));
        }

        static void ReaderFailures(ResearchFlowView view, object reader, object fixture, PointerEventData pointer, List<object> captures, Action<bool, string, object> check)
        {
            var scroll = Field<ScrollRect>(view, "scroll"); var template = Field<TMP_Text>(view, "referenceBody");
            foreach (var resource in new[] { "ResearchReaderDemo/package_missing", "ResearchReaderDemo/package_corrupt", "ResearchReaderDemo/package_badpage" })
            {
                Invoke(reader, "Initialize", scroll, template, view, resource);
                Invoke(reader, "Bind", "TEST/error/" + resource, "pdf", true, ""); Invoke(reader, "SetVisible", true); Flush(view);
                var metadata = Metadata(reader); string error = (string)metadata["error"];
                bool expectedCause = resource.EndsWith("package_missing") ? error == "Missing content registry"
                    : resource.EndsWith("package_corrupt") ? error != null && (error.StartsWith("Registry error:") || error.StartsWith("Invalid registry"))
                    : error != null && error.StartsWith("PDF page missing or SHA256 mismatch");
                check(!Flag(reader, "IsReady") && (string)Read(reader, "Status") == "Error" && expectedCause && !view.IsReferenceActuallyVisible,
                    resource + " cannot count as successful reading", metadata);
                captures.Add(Capture(fixture, resource.Substring(resource.LastIndexOf('/') + 1)));
                bool retried = (bool)Invoke(reader, "Command", "reference_media.retry", "None"); Flush(view);
                check(!retried && !Flag(reader, "IsReady") && (string)Read(reader, "Status") == "Error", resource + " retry remains an honest error while its cause is unresolved", new { retried, metadata = Metadata(reader) });
                int errorsBeforeRetry = Events(view.controller).Count(row => (string)row["event"] == "REFERENCE_VIEWER_ERROR");
                Click(view, "reference_media.retry", pointer);
                check(Action(view, "reference_media.retry").gameObject.activeInHierarchy && !Flag(reader, "IsReady")
                    && (string)Metadata(reader)["error"] == error && Events(view.controller).Count(row => (string)row["event"] == "REFERENCE_VIEWER_ERROR") == errorsBeforeRetry + 1,
                    resource + " unresolved-cause actual Retry stays visible and logs its new error", Metadata(reader));
            }
            Invoke(reader, "Initialize", scroll, template, view, "ResearchReaderDemo/package");
            Invoke(reader, "Bind", "TEST/unsupported", "unsupported-format", true, ""); Invoke(reader, "SetVisible", true); Flush(view);
            check((string)Read(reader, "Status") == "Error" && !Flag(reader, "IsReady") && (string)Metadata(reader)["error"] == "Invalid reader context or format",
                "unsupported format has a real visible error", Metadata(reader));
            captures.Add(Capture(fixture, "Unsupported_error"));
            Invoke(reader, "Bind", "TEST/production-unapproved", "pdf", false, "No approved source"); Invoke(reader, "SetVisible", true); Flush(view);
            check((string)Read(reader, "Status") == "Blocked" && !Flag(reader, "IsReady") && !view.IsReferenceActuallyVisible,
                "DEMO package cannot be used as approved production content", Metadata(reader));
            view.Refresh(); Flush(view);
            check(Flag(reader, "IsReady") && Number(reader, "PageCount") == BoundPages(reader), "valid original registry restores actual PDF rendering after error trials", Metadata(reader));
            captures.Add(Capture(fixture, "Error_recovered_valid_binding"));
        }

        static void ContentAndGrip(ResearchFlowView view, Camera camera, EventSystem events, PointerEventData pointer, Action<bool, string, object> check)
        {
            var scroll = Field<ScrollRect>(view, "scroll"); scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Flush(view);
            var moduleType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("Oculus.Interaction.PointableCanvasModule")).First(type => type != null);
            var module = events.gameObject.AddComponent(moduleType);
            typeof(BaseInputModule).GetField("m_EventSystem", Hidden).SetValue(module, events);
            var processDrag = moduleType.GetMethod("ProcessDrag", Hidden);
            var before = view.referencePanel.transform.position; float top = scroll.verticalNormalizedPosition;
            pointer.position = camera.WorldToScreenPoint(scroll.viewport.position); pointer.pressPosition = pointer.position;
            pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = scroll.viewport.gameObject };
            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(scroll.content.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            pointer.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(scroll.content.gameObject);
            ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.initializePotentialDrag);
            pointer.delta = Vector2.up * 180; pointer.position += pointer.delta; processDrag.Invoke(module, new object[] { pointer });
            pointer.delta = Vector2.up * 180; pointer.position += pointer.delta; processDrag.Invoke(module, new object[] { pointer }); Flush(view);
            check(pointer.dragging && pointer.pointerPress == pointer.pointerDrag && scroll.verticalNormalizedPosition < top && Vector3.Distance(before, view.referencePanel.transform.position) < .001f,
                "PointableCanvasModule ProcessDrag scrolls content without moving the window", new { pointer.dragging, press_owner = pointer.pointerPress ? pointer.pointerPress.name : null, drag_owner = pointer.pointerDrag ? pointer.pointerDrag.name : null, normalized = scroll.verticalNormalizedPosition, position = new[] { view.referencePanel.transform.position.x, view.referencePanel.transform.position.y, view.referencePanel.transform.position.z } });
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
            pointer.dragging = false; pointer.pointerPress = null; pointer.pointerDrag = null; scroll.StopMovement(); Flush(view);
            var handle = view.referencePanel.GetComponentsInChildren<ResearchDragHandle>(true).Single(h => h.panel == view.referencePanel);
            float offset = scroll.verticalNormalizedPosition;
            pointer.position = camera.WorldToScreenPoint(handle.transform.position); pointer.pressPosition = pointer.position;
            pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = handle.gameObject };
            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(handle.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            pointer.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(handle.gameObject);
            ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.initializePotentialDrag);
            pointer.delta = Vector2.right * 10; pointer.position += pointer.delta; processDrag.Invoke(module, new object[] { pointer });
            var acquired = view.referencePanel.transform.position;
            var dragStart = pointer.position;
            foreach (var delta in new[] { Vector2.right * 10, Vector2.left * 10, Vector2.up * 10, Vector2.down * 10 })
            {
                var target = dragStart + delta;
                pointer.delta = target - pointer.position; pointer.position = target;
                processDrag.Invoke(module, new object[] { pointer });
                if (Vector3.Distance(acquired, view.referencePanel.transform.position) > .001f) break;
            }
            var heldPosition = view.referencePanel.transform.position;
            var other = new PointerEventData(events) { pointerId = Other, button = PointerEventData.InputButton.Left, position = pointer.position + Vector2.right * 100, pointerPressRaycast = pointer.pointerPressRaycast };
            handle.OnDrag(other); handle.OnEndDrag(other);
            bool otherRejected = handle.IsDragging && Vector3.Distance(heldPosition, view.referencePanel.transform.position) < .001f;
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
            pointer.dragging = false; pointer.pointerPress = null; pointer.pointerDrag = null; Flush(view);
            check(otherRejected && !handle.IsDragging && handle.MoveSamples > 0 && Vector3.Distance(acquired, heldPosition) > .001f && Mathf.Abs(offset - scroll.verticalNormalizedPosition) < .001f,
                "grip has independent pointer ownership and cannot scroll document", new { otherRejected, handle.IsDragging, handle.MoveSamples, handle.RejectedSamples, rejection = view.referencePanel.LastRejection, acquired = new[] { acquired.x, acquired.y, acquired.z }, heldPosition = new[] { heldPosition.x, heldPosition.y, heldPosition.z }, released = new[] { view.referencePanel.transform.position.x, view.referencePanel.transform.position.y, view.referencePanel.transform.position.z }, handle.ReleaseRecovered, before_offset = offset, after_offset = scroll.verticalNormalizedPosition });
        }

        static JArray SceneLedger() => JArray.FromObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        {
            var scene = SceneManager.GetSceneAt(i);
            return new { scene.path, scene.name, loaded = scene.isLoaded, dirty = scene.isDirty, active = scene == SceneManager.GetActiveScene() };
        }));
        static JArray SceneFileLedger() => JArray.FromObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).path)
            .Where(path => !string.IsNullOrEmpty(path)).Select(path => new { path, sha256 = Hash(Path.GetFullPath(path)) }));
        static JObject VideoState()
        {
            string json = SessionState.GetString(VideoSession, "");
            return string.IsNullOrEmpty(json) ? null : JObject.Parse(json);
        }
        static void SaveVideoState(JObject state) => SessionState.SetString(VideoSession, state.ToString(Formatting.None));
        static void KeepVideoXRDisabled()
        {
            if (VideoState() == null) return;
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!settings) throw new InvalidOperationException("Bound Standalone XR settings disappeared; cannot maintain no-XR test scope.");
            settings.InitManagerOnStart = false; XRGeneralSettings.Instance = settings;
        }
        public static string BeginVideo(string evidenceDirectory, string registryResource = "ResearchReaderDemo/package",
            TaskOrder taskOrder = TaskOrder.AB, ParticipantLanguage language = ParticipantLanguage.Thai, bool fixedLocale = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || VideoState() != null || videoTick != null)
                throw new InvalidOperationException("BeginVideo requires a stopped Editor and no existing owned video operation.");
            if (!ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("An XR subsystem is already running; isolated no-XR Play refused.");
            if (!Enum.IsDefined(typeof(TaskOrder), taskOrder) || !Enum.IsDefined(typeof(ParticipantLanguage), language))
                throw new ArgumentException("Unknown video fixture task order or language.");
            if (fixedLocale)
            {
                string expected = ResearchReferenceViewer.ResourceFor(taskOrder == TaskOrder.AB ? "A" : "B", language.ToString());
                if (registryResource != expected || !Resources.Load<TextAsset>(expected))
                    throw new ArgumentException("Fixed-locale fixture requires its actual task/language package: " + expected);
            }
            ResearchBuildIntegrityChecks.RequireLocalServicesDisabled();
            string servicesPath = Path.GetFullPath("ProjectSettings/UnityConnectSettings.asset");
            byte[] servicesBefore = File.ReadAllBytes(servicesPath);
            if (System.Text.Encoding.UTF8.GetString(servicesBefore).Contains("m_EnableCloudDiagnosticsReporting: 1"))
                throw new InvalidOperationException("Serialized Cloud Diagnostics must be disabled before the native fixture.");
            string directory = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(directory);
            if (Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Use a fresh empty evidence directory: " + directory);
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!settings || !typeof(XRGeneralSettings).GetProperty("InitManagerOnStart").CanWrite) throw new InvalidOperationException("Verified public Standalone InitManagerOnStart setter is required.");
            var instance = XRGeneralSettings.Instance;
            string settingsPath = AssetDatabase.GetAssetPath(settings), instancePath = instance ? AssetDatabase.GetAssetPath(instance) : "";
            if (string.IsNullOrEmpty(settingsPath) || instance && string.IsNullOrEmpty(instancePath)) throw new InvalidOperationException("Persistent XR settings identity is required for reload-safe restoration.");
            long instanceId = 0; if (instance) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(instance, out string unusedGuid, out instanceId);
            string scenePath = "Assets/VRMaintenanceResearch/ResearchFlow/Editor/ReaderVideoIsolated_" + Guid.NewGuid().ToString("N") + ".unity";
            var state = new JObject {
                ["directory"] = directory, ["scene"] = scenePath, ["caller_scenes"] = SceneLedger(), ["caller_scene_files"] = SceneFileLedger(),
                ["prior_start_scene"] = EditorSceneManager.playModeStartScene ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : "",
                ["xr_path"] = settingsPath, ["xr_hash"] = Hash(Path.GetFullPath(settingsPath)), ["xr_dirty"] = EditorUtility.IsDirty(settings),
                ["xr_init"] = settings.InitManagerOnStart, ["xr_instance_path"] = instancePath, ["xr_instance_id"] = instanceId,
                ["started_utc"] = DateTime.UtcNow.ToString("o"), ["stage"] = "preparing", ["hardware_verified"] = false, ["registry_resource"] = registryResource,
                ["task_order"] = taskOrder.ToString(), ["language"] = language.ToString(), ["fixed_locale"] = fixedLocale,
                ["services_path"] = servicesPath, ["services_before_base64"] = Convert.ToBase64String(servicesBefore),
                ["crash_reporting_before"] = UnityEditor.CrashReporting.CrashReportingSettings.enabled
            };
            SaveVideoState(state);
            Scene temporary = default;
            try
            {
                var caller = SceneManager.GetActiveScene();
                temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (!EditorSceneManager.SaveScene(temporary, scenePath, false)) throw new IOException("Unable to save owned empty Play fixture scene.");
                if (!EditorSceneManager.CloseScene(temporary, true)) throw new InvalidOperationException("Unable to close owned scene before Play.");
                temporary = default; SceneManager.SetActiveScene(caller);
                state["scene_hash"] = Hash(Path.GetFullPath(scenePath));
                if (!JToken.DeepEquals(state["caller_scenes"], SceneLedger())) throw new InvalidOperationException("Caller scene state changed while preparing the owned scene.");
                KeepVideoXRDisabled();
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                if (!EditorSceneManager.playModeStartScene) throw new InvalidOperationException("Owned saved blank scene did not import.");
                state["stage"] = "entering_play"; SaveVideoState(state);
                File.WriteAllText(Path.Combine(directory, "reader-video-lifecycle-start.json"), state.ToString(Formatting.Indented));
                EditorApplication.EnterPlaymode();
                return "READER_ISOLATED_NATIVE_VIDEO_PLAY_REQUESTED; " + directory;
            }
            catch (Exception error)
            {
                if (temporary.IsValid()) EditorSceneManager.CloseScene(temporary, true);
                RestoreVideoSession(error.ToString(), true); throw;
            }
        }
        public static string CancelVideo()
        {
            var state = VideoState(); if (state == null) return "No owned native video operation.";
            state["error"] = "Cancelled by operator"; SaveVideoState(state);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
            else RestoreVideoSession("Cancelled by operator", true);
            return "Owned video operation cancellation requested; no canonical scene saved.";
        }
        static void VideoPlayStateChanged(PlayModeStateChange change)
        {
            var state = VideoState(); if (state == null) return;
            try
            {
                if (change == PlayModeStateChange.ExitingEditMode) KeepVideoXRDisabled();
                else if (change == PlayModeStateChange.EnteredPlayMode)
                {
                    exitingVideoPlay = false;
                    if (SceneManager.GetActiveScene().path != (string)state["scene"] || SceneManager.sceneCount != 1 || !ResearchFloorPolicy.EditorFixtureAllowed)
                        throw new InvalidOperationException("Owned blank scene or no-XR guard did not survive Play entry.");
                    state["stage"] = "native_video"; state["play_scenes"] = SceneLedger(); SaveVideoState(state);
                    RunNativeVideo((string)state["directory"]);
                }
                else if (change == PlayModeStateChange.ExitingPlayMode)
                {
                    exitingVideoPlay = true;
                    if (videoAbort != null) videoAbort();
                    state = VideoState();
                    if (videoTick != null) { EditorApplication.update -= videoTick; videoTick = null; state["error"] = "Play exited before native video completion"; }
                    state["stage"] = "restoring_editor"; SaveVideoState(state);
                }
                else if (change == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += () =>
                    {
                        if ((string)VideoState()?["scene"] == (string)state["scene"])
                            RestoreVideoSession((string)state["error"], true);
                    };
            }
            catch (Exception error)
            {
                state["error"] = error.ToString(); SaveVideoState(state);
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
                else RestoreVideoSession(error.ToString(), true);
            }
        }
        static void RestoreVideoSession(string error, bool verifyScenes)
        {
            var state = VideoState(); if (state == null) return;
            try
            {
                var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
                if (!settings || AssetDatabase.GetAssetPath(settings) != (string)state["xr_path"])
                    throw new InvalidOperationException("Cannot restore the exact bound Standalone XR settings identity.");
                settings.InitManagerOnStart = (bool)state["xr_init"];
                string instancePath = (string)state["xr_instance_path"]; long instanceId = (long)state["xr_instance_id"];
                XRGeneralSettings previous = null;
                if (!string.IsNullOrEmpty(instancePath))
                    previous = AssetDatabase.LoadAllAssetsAtPath(instancePath).OfType<XRGeneralSettings>().FirstOrDefault(asset => {
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string unused, out long id); return id == instanceId;
                    });
                if (!string.IsNullOrEmpty(instancePath) && !previous) throw new InvalidOperationException("Cannot restore prior XRGeneralSettings.Instance identity.");
                XRGeneralSettings.Instance = previous;
                string startScene = (string)state["prior_start_scene"];
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(startScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
                string servicesPath = (string)state["services_path"];
                byte[] servicesBefore = Convert.FromBase64String((string)state["services_before_base64"]);
                byte[] servicesAfter = File.ReadAllBytes(servicesPath);
                byte[] normalizedServices = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(servicesAfter).Replace("m_EnableCloudDiagnosticsReporting: 1", "m_EnableCloudDiagnosticsReporting: 0"));
                if (!normalizedServices.SequenceEqual(servicesBefore))
                    throw new InvalidOperationException("Unexpected local services change during native fixture; preserve it instead of overwriting.");
                state["crash_reporting_observed_after_play"] = UnityEditor.CrashReporting.CrashReportingSettings.enabled;
                state["services_observed_after_sha256"] = Hash(servicesPath);
                UnityEditor.CrashReporting.CrashReportingSettings.enabled = (bool)state["crash_reporting_before"];
                if (!servicesAfter.SequenceEqual(servicesBefore)) File.WriteAllBytes(servicesPath, servicesBefore);
                bool servicesRestored = File.ReadAllBytes(servicesPath).SequenceEqual(servicesBefore) &&
                    UnityEditor.CrashReporting.CrashReportingSettings.enabled == (bool)state["crash_reporting_before"];
                ResearchBuildIntegrityChecks.RequireLocalServicesDisabled();
                state["services_restored"] = servicesRestored;
                bool xrUnchanged = Hash(Path.GetFullPath((string)state["xr_path"])) == (string)state["xr_hash"] && EditorUtility.IsDirty(settings) == (bool)state["xr_dirty"] && settings.InitManagerOnStart == (bool)state["xr_init"];
                bool scenesUnchanged = !verifyScenes || JToken.DeepEquals(state["caller_scenes"], SceneLedger()) && JToken.DeepEquals(state["caller_scene_files"], SceneFileLedger());
                state["stage"] = "restored"; state["finished_utc"] = DateTime.UtcNow.ToString("o"); state["error"] = error;
                state["xr_restored"] = xrUnchanged; state["caller_scenes_preserved"] = scenesUnchanged;
                state["observed_scenes"] = SceneLedger(); state["observed_scene_files"] = SceneFileLedger();
                state["summary"] = xrUnchanged && servicesRestored && scenesUnchanged && string.IsNullOrEmpty(error) ? "ISOLATED_PLAY_RESTORE_PASS" : "ISOLATED_PLAY_RESTORE_FAIL";
                File.WriteAllText(Path.Combine((string)state["directory"], "reader-video-lifecycle.json"), state.ToString(Formatting.Indented));
                SessionState.EraseString(VideoSession);
                Debug.Log((string)state["summary"] + "; " + (string)state["directory"]);
            }
            catch (Exception restoreError)
            {
                state["restore_error"] = restoreError.ToString(); SaveVideoState(state);
                File.WriteAllText(Path.Combine((string)state["directory"], "reader-video-restore-error.json"), state.ToString(Formatting.Indented));
                Debug.LogError("ISOLATED_PLAY_RESTORE_ERROR " + restoreError);
            }
        }

        static object CaptureNative(object fixture, string stage, string directory)
        {
            var view = Public<ResearchFlowView>(fixture, "View"); var camera = Public<Camera>(fixture, "Camera");
            var capturedReader = view.ReferenceViewer; capturedReader.GetType().GetMethod("Update", Hidden).Invoke(capturedReader, null);
            var capturedPlayer = Field<UnityEngine.Video.VideoPlayer>(capturedReader, "player");
            var renderedClock = Field<TMP_Text>(capturedReader, "timeLabel");
            if (capturedPlayer && capturedPlayer.isPrepared && Field<bool>(capturedReader, "visible") && !capturedPlayer.isPlaying)
            {
                double display = Flag(capturedReader, "TimelineHeld") ? ((Slider)Read(capturedReader, "Timeline")).value : capturedPlayer.time;
                var formatter = capturedReader.GetType().GetMethod("DisplayTime", BindingFlags.Static | BindingFlags.NonPublic);
                string expected = (string)formatter.Invoke(null, new object[] { display }) + " / " + (string)formatter.Invoke(null, new object[] { capturedPlayer.length });
                if (!renderedClock || renderedClock.text != expected) throw new InvalidOperationException("Native capture clock disagrees with actual decoded/held time: " + stage);
            }
            string path = Path.Combine(directory, stage + ".png"); if (File.Exists(path)) throw new IOException("Refusing to overwrite native pixels: " + path);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; var parent = camera.transform.parent;
            var position = camera.transform.position; var rotation = camera.transform.rotation; float fov = camera.fieldOfView, aspect = camera.aspect;
            ulong sceneMask = camera.overrideSceneCullingMask; var clear = camera.clearFlags; var background = camera.backgroundColor;
            var target = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var pixels = new Texture2D(1280, 800, TextureFormat.RGBA32, false);
            var otherCanvases = view.GetComponentsInChildren<Canvas>(true).Where(canvas => canvas != view.referencePanel.GetComponent<Canvas>()).ToDictionary(canvas => canvas, canvas => canvas.enabled);
            try
            {
                foreach (var pair in otherCanvases) pair.Key.enabled = false;
                camera.transform.SetParent(null, true); var corners = new Vector3[4]; view.referencePanel.GetComponent<RectTransform>().GetWorldCorners(corners);
                camera.transform.rotation = Quaternion.LookRotation((corners[0] + corners[2]) * .5f - position, Vector3.up); camera.aspect = 1.6f;
                float tangent = corners.Select(camera.transform.InverseTransformPoint).Max(point => Mathf.Max(Mathf.Abs(point.y), Mathf.Abs(point.x) / camera.aspect) / Mathf.Max(.01f, point.z));
                camera.fieldOfView = Mathf.Clamp(2 * Mathf.Atan(tangent * 1.18f) * Mathf.Rad2Deg, 15, 100);
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(view.gameObject.scene);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.11f, .14f, .18f, 1);
                camera.targetTexture = target; target.Create(); Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); pixels.Apply(false, false); File.WriteAllBytes(path, pixels.EncodeToPNG());
                int colors = pixels.GetPixels32().Select(pixel => (uint)pixel.r << 24 | (uint)pixel.g << 16 | (uint)pixel.b << 8 | pixel.a).Distinct().Take(33).Count();
                if (colors <= 16) throw new InvalidOperationException("Owned native scene render contains insufficient variation: " + path);
                return new { path, stage, width = 1280, height = 800, pixel_variation_lower_bound = colors, scene = view.gameObject.scene.path,
                    scene_culling_mask = camera.overrideSceneCullingMask.ToString(), hardware_verified = false,
                    camera_field_of_view = camera.fieldOfView, panel_world_position = new[] { view.referencePanel.transform.position.x, view.referencePanel.transform.position.y, view.referencePanel.transform.position.z },
                    other_owned_canvases_suppressed_for_capture_only = true,
                    scope = "Actual native VideoPlayer surface and production UI, framed synthetic mono camera in the owned blank runtime scene. Not Quest/stereo/wearer evidence." };
            }
            finally
            {
                foreach (var pair in otherCanvases) if (pair.Key) pair.Key.enabled = pair.Value;
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive; camera.overrideSceneCullingMask = sceneMask;
                camera.transform.SetParent(parent, true); camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; camera.aspect = aspect;
                camera.clearFlags = clear; camera.backgroundColor = background; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        static void TimelineStaleCallbacks(ResearchFlowView view, ResearchSessionFlowController controller, object reader, Camera camera, EventSystem eventOwner, PointerEventData pointer, Action<bool, string, object> check)
        {
            var timeline = Read(reader, "Timeline") as Slider;
            if (!timeline) throw new InvalidOperationException("Actual native video timeline is missing.");
            var rect = timeline.handleRect.parent as RectTransform; var player = Read(reader, "NativeVideoPlayer") as UnityEngine.Video.VideoPlayer;
            void Position(float fraction)
            {
                pointer.position = camera.WorldToScreenPoint(rect.TransformPoint(new Vector3(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, fraction), rect.rect.center.y, 0)));
                pointer.pressPosition = pointer.position;
                pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = timeline.gameObject };
            }
            int firstEvents = Events(controller).Count; double beforeTime = player.time;
            Position(.4f); ExecuteEvents.Execute(timeline.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            Position(.6f); ExecuteEvents.Execute(timeline.gameObject, pointer, ExecuteEvents.dragHandler);
            var other = new PointerEventData(eventOwner) { pointerId = Other, button = PointerEventData.InputButton.Left, position = pointer.position };
            ExecuteEvents.Execute(timeline.gameObject, other, ExecuteEvents.pointerUpHandler);
            check(Flag(reader, "TimelineHeld") && !Events(controller).Skip(firstEvents).Any(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_REQUESTED") && Math.Abs(player.time - beforeTime) < .08,
                "real timeline down/drag holds one owner and neither seeks before release nor releases for a different pointer", new { beforeTime, actual_time = player.time, held = Flag(reader, "TimelineHeld"), timeline_value = timeline.value });
            Click(view, "reference_minimize", other); ExecuteEvents.Execute(timeline.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            var rejection = Events(controller).Skip(firstEvents).LastOrDefault(row => (string)row["event"] == "REFERENCE_VIEWER_INPUT_REJECTED" && (string)row["after"]?["control"] == "timeline");
            check(controller.ReferenceMinimized && !Flag(reader, "TimelineHeld") && rejection != null && ((string)rejection["after"]?["reason"] == "disabled" || (string)rejection["after"]?["reason"] == "hidden")
                && !Events(controller).Skip(firstEvents).Any(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_REQUESTED"),
                "native minimize cancels stale timeline acquisition with its literal reason and cannot commit a late seek", new { held = Flag(reader, "TimelineHeld"), rejection });
            Click(view, "reference_restore", other);
        }
        static JObject TimelineFreshCallbacks(ResearchFlowView view, ResearchSessionFlowController controller, object reader, Camera camera, PointerEventData pointer, Action<bool, string, object> check, float down = .2f, float drag = .25f)
        {
            var timeline = Read(reader, "Timeline") as Slider; var player = Read(reader, "NativeVideoPlayer") as UnityEngine.Video.VideoPlayer;
            if (!timeline || !player || !player.isPrepared || !Flag(reader, "IsReady")) throw new InvalidOperationException("Reacquire the current prepared player/timeline only after native restoration is ready.");
            var rect = timeline.handleRect.parent as RectTransform;
            void Position(float fraction)
            {
                pointer.position = camera.WorldToScreenPoint(rect.TransformPoint(new Vector3(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, fraction), rect.rect.center.y, 0)));
                pointer.pressPosition = pointer.position;
                pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = timeline.gameObject };
            }
            int firstEvents = Events(controller).Count;
            Position(down); ExecuteEvents.Execute(timeline.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            Position(drag); ExecuteEvents.Execute(timeline.gameObject, pointer, ExecuteEvents.dragHandler);
            double target = timeline.value; ExecuteEvents.Execute(timeline.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            var requested = Events(controller).Skip(firstEvents).LastOrDefault(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_REQUESTED");
            check(!Flag(reader, "TimelineHeld") && (string)Read(reader, "Status") == "Seeking" && requested != null && (string)requested["source"] == "Tester" && (string)requested["input_source"] == "None",
                "fresh real timeline owner release starts native seek with honest Tester/None provenance", new { target, requested, metadata = Read(reader, "Metadata") });
            if (requested == null) throw new InvalidOperationException("Actual fresh timeline release produced no native seek request.");
            return requested;
        }

        static bool TargetFrameMatches(UnityEngine.Video.VideoPlayer player, double target) => player && player.isPrepared && player.frameRate > 0 && player.frame >= 0
            && Math.Abs(player.time - target) <= 2 / player.frameRate && Math.Abs((double)player.frame / player.frameRate - target) <= 2 / player.frameRate;

        static void CheckVideoRefinement(ResearchFlowView view, object reader, UnityEngine.Video.VideoPlayer player, Action<bool, string, object> check, object fixture, string directory, List<object> captures, PointerEventData pointer)
        {
            var actions = view.referencePanel.GetComponentsInChildren<ResearchUIAction>(true);
            var transport = actions.Where(a => a.gameObject.activeInHierarchy && (a.command == "reference_media.play" || a.command == "reference_media.pause")).ToArray();
            var skip = actions.Where(a => a.command == "reference_media.seek_back" || a.command == "reference_media.seek_forward").ToArray();
            var label = Field<TMP_Text>(view, "pageLabel");
            var footer = view.referencePanel.transform.Find("Expanded/ReaderFooter");
            check(transport.Length == 1 && !label.gameObject.activeInHierarchy && (!footer || !footer.gameObject.activeInHierarchy)
                && skip.All(a => a.gameObject.activeInHierarchy == (player.length > 20)), "native duration projects one transport, no video pager/footer and duration-aware skip targets", new { duration = player.length, transport = transport.Select(a => a.command).ToArray(), skip = skip.Select(a => new { a.command, active = a.gameObject.activeInHierarchy }).ToArray(), page_label_active = label.gameObject.activeInHierarchy });
            var scroll = Field<ScrollRect>(view, "scroll"); var image = Field<UnityEngine.UI.RawImage>(reader, "videoImage");
            check(image.rectTransform.rect.width >= (scroll.viewport.rect.width - 24) * .9f && Mathf.Abs(image.rectTransform.rect.width / image.rectTransform.rect.height - (float)player.width / player.height) < .001f,
                "native decoded video fits at least 90 percent of usable viewer width at original source aspect", new { image_width = image.rectTransform.rect.width, image_height = image.rectTransform.rect.height, viewer_width = scroll.viewport.rect.width, viewer_height = scroll.viewport.rect.height, source_width = player.width, source_height = player.height, panel_world_position = new[] { view.referencePanel.transform.position.x, view.referencePanel.transform.position.y, view.referencePanel.transform.position.z } });
            var timeline = (Slider)Read(reader, "Timeline"); var carrier = timeline.handleRect; var thumb = carrier.Find("VisibleThumb") as RectTransform;
            int eventsBefore = Events(view.controller).Count(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_REQUESTED");
            float saved = timeline.value;
            foreach (float normalized in new[] { 0f, .5f, 1f })
            {
                timeline.SetValueWithoutNotify(timeline.minValue + normalized * (timeline.maxValue - timeline.minValue)); Canvas.ForceUpdateCanvases();
                var bounds = thumb ? BoundsIn(thumb, (RectTransform)timeline.transform) : Rect.zero;
                check(thumb && Mathf.Abs(thumb.rect.width - 20) < .01f && Mathf.Abs(thumb.rect.height - 20) < .01f && !thumb.GetComponent<Graphic>().raycastTarget
                    && carrier.rect.width >= 48 && carrier.rect.height >= 64 && bounds.xMin >= -((RectTransform)timeline.transform).rect.width / 2 && bounds.xMax <= ((RectTransform)timeline.transform).rect.width / 2,
                    "native timeline " + normalized + " has fixed circular visual and independently large hit carrier", new { value = timeline.value, visible_width = thumb ? thumb.rect.width : 0, visible_height = thumb ? thumb.rect.height : 0, hit_width = carrier.rect.width, hit_height = carrier.rect.height, bounds = BoundsValue(bounds) });
                var camera = Public<Camera>(fixture, "Camera");
                pointer.position = camera.WorldToScreenPoint(carrier.TransformPoint(Vector3.zero));
                pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = timeline.gameObject };
                ExecuteEvents.Execute(timeline.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                captures.Add(CaptureNative(fixture, "Timeline_" + normalized + "_hover", directory));
                ExecuteEvents.Execute(timeline.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                check(Flag(reader, "TimelineHeld") && thumb && Mathf.Abs(thumb.rect.height - 20) < .01f, "native timeline held " + normalized + " retains circular visual and real owner", Read(reader, "Metadata"));
                captures.Add(CaptureNative(fixture, "Timeline_" + normalized + "_held", directory));
                ((ResearchReaderTimeline)timeline).Cancel("geometry_probe_finished");
                ExecuteEvents.Execute(timeline.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            }
            timeline.SetValueWithoutNotify(saved);
            check(eventsBefore == Events(view.controller).Count(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_REQUESTED"), "native playback UI value refresh does not generate a seek", eventsBefore);
            var time = Field<TMP_Text>(reader, "timeLabel").GetParsedText();
            check(System.Text.RegularExpressions.Regex.IsMatch(time, @"^\d+:\d{2} / \d+:\d{2}$"), "native clock is m:ss from actual decoder duration", new { clock_text = time, decoded_time = player.time, duration = player.length });
        }

        static string RunNativeVideo(string evidenceDirectory)
        {
            if (!EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("Video check requires root-owned Editor Play Mode without a running XR subsystem.");
            if (videoTick != null) throw new InvalidOperationException("A native video check is already running.");
            var videoState = VideoState();
            var taskOrder = (TaskOrder)Enum.Parse(typeof(TaskOrder), (string)videoState?["task_order"] ?? "AB");
            var language = (ParticipantLanguage)Enum.Parse(typeof(ParticipantLanguage), (string)videoState?["language"] ?? "Thai");
            bool fixedLocale = (bool?)videoState?["fixed_locale"] ?? false;
            string expectedResource = ResearchReferenceViewer.ResourceFor(taskOrder == TaskOrder.AB ? "A" : "B", language.ToString());
            var expectedPackage = fixedLocale ? JsonUtility.FromJson<ResearchReferenceViewer.Package>(Resources.Load<TextAsset>(expectedResource).text) : null;
            string directory = Path.GetFullPath(evidenceDirectory), path = Path.Combine(directory, "reader-video.json");
            Directory.CreateDirectory(directory);
            using (var marker = new FileStream(Path.Combine(directory, "started"), FileMode.CreateNew)) { }
            ResearchFlowView donor = null; GameObject ownedDonor = null;
            var type = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            var caller = SceneManager.GetActiveScene(); bool dirty = caller.isDirty; int previews = EditorSceneManager.previewSceneCount;
            var priorEvents = EventSystem.current;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(cursor => cursor, cursor => cursor.HoverColor);
            object fixture = null, reader = null; ResearchFlowView view = null; ResearchSessionFlowController controller = null;
            PointerEventData pointer = null; EventSystem ownedEvents = null;
            var rows = new List<object>(); var captures = new List<object>(); int failures = 0, updates = 0, stage = 0;
            double started = 0, phaseStarted = 0, pausedTime = 0, firstTime = 0, timelineTarget = 0, timelineRestoreTime = 0, pendingTarget = 0;
            long firstFrame = -1, timelineFrames = 0, timelineRequestSeq = 0, pendingRestoreSeq = 0; int replacedPlayerId = 0;
            string timelineContext = null; bool quickPauseIssued = false; double quickPauseTime = 0, quickPauseAt = 0, longSkipTarget = 0; int longSkipStage = 0, endpointIndex = 0; bool endpointPending = false; double endpointTarget = 0; long endpointRequestSeq = 0;
            bool tailSeekPending = false; long tailSeekRequestSeq = 0;
            string error = null;
            int reopenCycle = 0, closedEvents = 0, openedEvents = 0, errorLocaleCycle = 0; long playingSeekSeq = 0; double playingSeekTarget = 0;
            var retiredPlayers = new List<UnityEngine.Video.VideoPlayer>(); var retiredTextures = new List<RenderTexture>();
            var priorTextures = new HashSet<int>(Resources.FindObjectsOfTypeAll<RenderTexture>().Select(item => item.GetInstanceID()));
            void Check(bool pass, string name, object observed) { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            void CheckBinding(string phase)
            {
                if (!fixedLocale) return;
                var metadata = Metadata(reader);
                Check(((ResearchReferenceViewer)reader).BoundRegistryResource == expectedResource
                    && (string)metadata["content_id"] == expectedPackage.content_id
                    && (string)metadata["version"] == expectedPackage.version
                    && controller.Language == language.ToString(),
                    phase + " retains the requested task/language package", new { expectedResource, actual = Read(reader, "BoundRegistryResource"), metadata });
            }
            void TrackMedia()
            {
                CheckBinding("media observation");
                var currentPlayer = Field<UnityEngine.Video.VideoPlayer>(reader, "player"); var texture = Field<RenderTexture>(reader, "videoTexture");
                if (currentPlayer && !retiredPlayers.Contains(currentPlayer)) retiredPlayers.Add(currentPlayer);
                if (texture && !retiredTextures.Contains(texture)) retiredTextures.Add(texture);
                var newTextures = Resources.FindObjectsOfTypeAll<RenderTexture>().Where(item => item.name == "ResearchReaderVideoFrame" && !priorTextures.Contains(item.GetInstanceID())).ToArray();
                Check(newTextures.Length == (texture ? 1 : 0), "native fixture has no untracked replacement RenderTexture", newTextures.Select(item => new { id = item.GetInstanceID(), item.width, item.height, created = item.IsCreated() }).ToArray());
            }
            void CheckReleased(string reason)
            {
                var livePlayers = view.GetComponentsInChildren<UnityEngine.Video.VideoPlayer>(true);
                Check(livePlayers.Length == 0 && retiredPlayers.All(item => !item) && retiredTextures.All(item => !item)
                    && Resources.FindObjectsOfTypeAll<RenderTexture>().Where(item => item.name == "ResearchReaderVideoFrame").All(item => priorTextures.Contains(item.GetInstanceID()))
                    && !Field<UnityEngine.Video.VideoPlayer>(reader, "player") && !Field<AudioSource>(reader, "videoAudio") && !Field<RenderTexture>(reader, "videoTexture"),
                    reason + " releases every tracked decoder, AudioSource and RenderTexture after deferred destruction", new { live_players = livePlayers.Length,
                        tracked_players = retiredPlayers.Count, remaining_players = retiredPlayers.Count(item => item), tracked_textures = retiredTextures.Count,
                        remaining_textures = retiredTextures.Count(item => item), metadata = Read(reader, "Metadata") });
            }
            void Reopen()
            {
                openedEvents = Events(controller).Count;
                Check(controller.Command("reference", "None") && controller.ReferenceOpen, "existing reference command reopens the isolated TEST reader", controller.ReferenceSection);
                Flush(view); if (controller.ReferenceSection != "video") Click(view, "reference_tab.video", pointer);
                CheckBinding("reopen");
            }
            object Observe(UnityEngine.Video.VideoPlayer player) => new { status = Read(reader, "Status"), is_prepared = player && player.isPrepared,
                is_playing = player && player.isPlaying, frame = player ? player.frame : -1, time_seconds = player && player.isPrepared ? player.time : (double?)null,
                frame_rate = player && player.isPrepared ? player.frameRate : (double?)null,
                frame_timeSeconds = player && player.isPrepared && player.frameRate > 0 && player.frame >= 0 ? (double)player.frame / player.frameRate : (double?)null,
                target_tolerance_seconds = player && player.isPrepared && player.frameRate > 0 ? 2 / player.frameRate : (double?)null,
                length_seconds = player && player.isPrepared ? player.length : (double?)null, session_paused = controller.Flow.Paused, metadata = Read(reader, "Metadata") };
            void Finish()
            {
                EditorApplication.update -= videoTick; videoTick = null; videoAbort = null;
                try { if (fixture != null) ((IDisposable)fixture).Dispose(); }
                catch (Exception exception) { error = (error ?? "") + exception; failures++; }
                if (priorEvents) EventSystem.current = priorEvents;
                foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value;
                try { ReleaseFontDonor(ownedDonor); }
                catch (Exception exception) { error = (error ?? "") + exception; Check(false, "font cleanup error", exception.ToString()); }
                Check(SceneManager.GetActiveScene() == caller && EditorSceneManager.previewSceneCount == previews && EventSystem.current == priorEvents,
                    "native video fixture releases its preview and EventSystem ownership in the owned blank scene", new { caller.path, prior_owned_scene_dirty = dirty, current_owned_scene_dirty = caller.isDirty, previews, current_previews = EditorSceneManager.previewSceneCount });
                string summary = failures == 0 ? "READER_NATIVE_VIDEO_EDITOR_PASS" : "READER_NATIVE_VIDEO_EDITOR_FAIL";
                File.WriteAllText(path, JsonConvert.SerializeObject(new { summary, failures, error, updates, rows, captures, hardware_verified = false,
                    scope = "Native Unity VideoPlayer URL/hash load, decode clock/frame and production callbacks in root-owned Editor Play Mode and isolated TEST preview. UI button events use synthetic pointers. No manually invoked prepare/seek/end callback, simulated playback clock, Quest/stereo/wearer, consent or study approval." }, Formatting.Indented));
                Debug.Log(summary + "; " + path);
                var session = VideoState();
                if (session != null)
                {
                    session["native_result"] = summary; session["native_failures"] = failures;
                    if (!string.IsNullOrEmpty(error)) session["error"] = error;
                    SaveVideoState(session); if (!exitingVideoPlay && !reloadingFonts) EditorApplication.ExitPlaymode();
                }
            }
            try
            {
                donor = FontDonor(out ownedDonor);
                fixture = Activator.CreateInstance(type, new object[] { directory, donor, language });
                controller = Public<ResearchSessionFlowController>(fixture, "Controller"); view = Public<ResearchFlowView>(fixture, "View");
                // Only the owned detached View joins the owned runtime scene so genuine media Update/coroutines tick.
                // Controller stays inactive in the preview scene; View remains disabled; no task/session startup executes.
                SceneManager.MoveGameObjectToScene(view.gameObject, caller);
                view.referencePanel.GetComponent<Canvas>().worldCamera = Public<Camera>(fixture, "Camera");
                Set(controller, "Flow", new ResearchSessionFlow(taskOrder, ResearchFlowState.Task1Active, 5));
                var timing = Field<ResearchTaskTiming>(controller, "taskTiming");
                timing.Start(controller.Logger.Timestamp);
                Check(timing.CanManipulate && (bool)controller.GetType().GetProperty("LocalUICommandsAllowed", Hidden).GetValue(controller), "owned media fixture enables its task clock without task startup", new { timing.CanManipulate, localUICommandsAllowed = (bool)controller.GetType().GetProperty("LocalUICommandsAllowed", Hidden).GetValue(controller) });
                Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "video");
                type.GetMethod("Refresh").Invoke(fixture, null); Flush(view); reader = Viewer(view);
                if (reader == null) throw new InvalidOperationException("No production media viewer.");
                string registryResource = (string)VideoState()?["registry_resource"];
                if (!string.IsNullOrEmpty(registryResource) && registryResource != "ResearchReaderDemo/package")
                {
                    ((ResearchReferenceViewer)reader).Initialize(Field<ScrollRect>(view, "scroll"), Field<TMP_Text>(view, "referenceBody"), view, registryResource);
                    view.RefreshReference(); Flush(view);
                }
                CheckBinding("initial binding");
                Check(!controller.gameObject.activeInHierarchy && !view.enabled && view.gameObject.activeInHierarchy && ((Behaviour)reader).isActiveAndEnabled && controller.Record == null && ResearchFloorPolicy.EditorFixtureAllowed,
                    "only the owned active media viewer runs; canonical controllers, task startup, participant record and XR remain absent", new { controller_active = controller.gameObject.activeInHierarchy, view.enabled, viewer_active = ((Behaviour)reader).isActiveAndEnabled, participant_record_present = controller.Record != null, no_xr = ResearchFloorPolicy.EditorFixtureAllowed });
                var eventObject = new GameObject("NativeVideoOwnedEvents") { hideFlags = HideFlags.HideAndDontSave }; eventObject.SetActive(false);
                SceneManager.MoveGameObjectToScene(eventObject, view.gameObject.scene); eventObject.transform.SetParent(view.transform, false);
                ownedEvents = eventObject.AddComponent<EventSystem>(); pointer = new PointerEventData(ownedEvents) { pointerId = Owner, button = PointerEventData.InputButton.Left };
                captures.Add(CaptureNative(fixture, "Video_actual_loading", directory));
                Check(!Flag(reader, "IsReady") && !view.IsReferenceActuallyVisible, "loading video is not successful reading", Read(reader, "Metadata"));
            }
            catch (Exception exception) { error = exception.ToString(); Check(false, "native video arrangement", error); Finish(); return "READER_NATIVE_VIDEO_EDITOR_FAILED_TO_START; " + path; }
            videoAbort = () => { error = error ?? (string)VideoState()?["error"] ?? "Play exited before native video completion"; Check(false, "native video interrupted", error); Finish(); };
            videoTick = () =>
            {
                try
                {
                    updates++;
                    if (!EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed || !view) throw new InvalidOperationException("Bound Editor Play/no-XR fixture condition changed.");
                    double now = EditorApplication.timeSinceStartup;
                    if (updates == 1)
                    {
                        rows.Add(new { name = "first_video_tick_observation", diagnostic_only = true,
                            initial_delay_seconds = now - started, frame_count = Time.frameCount,
                            editor_playing = EditorApplication.isPlaying, editor_paused = EditorApplication.isPaused,
                            application_focused = Application.isFocused, run_in_background = Application.runInBackground });
                        started = phaseStarted = now;
                    }
                    if (now - started > 180 || now - phaseStarted > 25) throw new TimeoutException("Native video did not reach stage " + stage + "; " + JsonConvert.SerializeObject(Read(reader, "Metadata")));
                    var player = Field<UnityEngine.Video.VideoPlayer>(reader, "player");
                    if ((string)Read(reader, "Status") == "Error" && stage != 21) throw new InvalidOperationException(JsonConvert.SerializeObject(Read(reader, "Metadata")));
                    Flush(view);
                    switch (stage)
                    {
                        case 0:
                            if (!player || !player.isPrepared || !Flag(reader, "IsReady")) return;
                            var audio = Field<AudioSource>(reader, "videoAudio");
                            if (endpointIndex == 0 && !endpointPending)
                            {
                                int beforeNoop = Events(controller).Count; long beforeFrame = player.frame;
                                var idleTimeline = (Slider)Read(reader, "Timeline"); var idleArea = (RectTransform)idleTimeline.handleRect.parent;
                                var zeroPoint = idleArea.TransformPoint(new Vector3(idleArea.rect.xMin - 1f, idleArea.rect.center.y, 0));
                                if (!((RectTransform)idleTimeline.transform).rect.Contains(((RectTransform)idleTimeline.transform).InverseTransformPoint(zeroPoint)))
                                    throw new InvalidOperationException("Zero timeline probe must remain inside the actual Slider hit rect.");
                                pointer.position = Public<Camera>(fixture, "Camera").WorldToScreenPoint(zeroPoint);
                                pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = idleTimeline.gameObject };
                                bool zeroCanSeek = Flag(reader, "CanSeekTimeline"), zeroInteractable = idleTimeline.IsInteractable();
                                ExecuteEvents.Execute(idleTimeline.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                                bool zeroHeld = Flag(reader, "TimelineHeld"); float zeroTarget = idleTimeline.value; ExecuteEvents.Execute(idleTimeline.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                                var noop = Events(controller).Skip(beforeNoop).ToArray();
                                Check(zeroHeld && zeroTarget == idleTimeline.minValue && Flag(reader, "IsReady") && player.frame == beforeFrame && !Flag(reader, "TimelineHeld") && !noop.Any(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_REQUESTED")
                                    && noop.Any(row => (string)row["event"] == "REFERENCE_VIEWER_INPUT_REJECTED" && (string)row["after"]?["reason"] == "already_at_decoded_start"),
                                    "actual initial zero timeline release is a decoded-start no-op, not a timeout or fabricated seek ACK", new { beforeFrame, actual_frame = player.frame, actual_time = player.time, target = zeroTarget, held_after_down = zeroHeld, can_seek = zeroCanSeek, interactable = zeroInteractable, events = noop });
                            }
                            if (endpointIndex < 4)
                            {
                                float fraction = new[] { .5f, 0f, 1f, 0f }[endpointIndex];
                                if (!endpointPending)
                                {
                                    var endpointRequest = TimelineFreshCallbacks(view, controller, reader, Public<Camera>(fixture, "Camera"), pointer, Check, fraction, fraction);
                                    endpointTarget = (double)endpointRequest["after"]["seek_target_seconds"]; endpointRequestSeq = (long)endpointRequest["seq"]; endpointPending = true; return;
                                }
                                var completion = Events(controller).LastOrDefault(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_COMPLETED" && (long)row["seq"] > endpointRequestSeq);
                                Check(completion != null && TargetFrameMatches(player, endpointTarget) && !player.isPlaying && !controller.Flow.Paused,
                                    "actual native timeline endpoint " + fraction + " acknowledges requested target frame without task change", new { target = endpointTarget, request_seq = endpointRequestSeq, actual_time = player.time, actual_frame = player.frame, completion });
                                captures.Add(CaptureNative(fixture, "Timeline_endpoint_" + endpointIndex + "_ack", directory));
                                if (completion != null && TargetFrameMatches(player, endpointTarget) && !player.isPlaying && !controller.Flow.Paused)
                                    phaseStarted = EditorApplication.timeSinceStartup;
                                endpointIndex++; endpointPending = false; return;
                            }
                            if (!quickPauseIssued)
                            {
                                Check(!player.isPlaying && !controller.Flow.Paused && audio && audio.mute && Number(reader, "DecodedFrameCount") > 0 && Convert.ToInt64(Read(reader, "FirstDecodedFrame")) >= 0,
                                    "native prepare yields a genuine first decoded frame, paused and muted without session Pause", Observe(player));
                                // Reader v2 S1 videos are narrated; a silent S1 movie is a content regression (DEMO fixtures stay exempt).
                                if (Read(reader, "BoundRegistryResource").ToString().StartsWith("ResearchReaderS1/", StringComparison.Ordinal))
                                    Check(player.audioTrackCount >= 1 && player.GetAudioChannelCount(0) >= 1 && player.GetAudioSampleRate(0) > 0,
                                        "S1 video decoder exposes a narration audio track routed to the media AudioSource",
                                        new { tracks = player.audioTrackCount, channels = player.audioTrackCount > 0 ? player.GetAudioChannelCount(0) : 0, rate = player.audioTrackCount > 0 ? player.GetAudioSampleRate(0) : 0, output = player.audioOutputMode.ToString() });
                                Canvas.ForceUpdateCanvases();
                                var slider = (Slider)Read(reader, "Timeline"); var handleRect = slider.handleRect; var hostRect = (RectTransform)slider.transform;
                                var handleCorners = new Vector3[4]; handleRect.GetWorldCorners(handleCorners);
                                Check(handleRect.rect.height > 0 && handleCorners.All(corner => { var point = hostRect.InverseTransformPoint(corner); return point.y >= hostRect.rect.yMin - .1f && point.y <= hostRect.rect.yMax + .1f; }),
                                    "native timeline handle remains inside its reserved band without covering time or buttons", new { handle_height = handleRect.rect.height, timeline_height = hostRect.rect.height });
                                CheckVideoRefinement(view, reader, player, Check, fixture, directory, captures, pointer);
                                captures.Add(CaptureNative(fixture, "Video_actual_prepared", directory));
                                quickPauseTime = player.time; Click(view, "reference_media.play", pointer); Click(view, "reference_media.pause", pointer);
                                Check(!Flag(reader, "IsReady") && !player.isPlaying && !controller.Flow.Paused, "immediate Play/Pause waits decoded pause acknowledgement without session Pause", Read(reader, "Metadata"));
                                quickPauseIssued = true; quickPauseAt = now; return;
                            }
                            if (now - quickPauseAt < .4) return;
                            var pauseAck = Events(controller).LastOrDefault(row => (string)row["event"] == "REFERENCE_VIDEO_PAUSED");
                            Check(pauseAck != null && !player.isPlaying && !controller.Flow.Paused && TargetFrameMatches(player, quickPauseTime), "rapid Play/Pause acknowledges its original decoded target before readiness", new { expected_time = quickPauseTime, observed = Observe(player), pause_ack = pauseAck });
                            captures.Add(CaptureNative(fixture, "Video_actual_rapid_pause_ack", directory));
                            firstFrame = player.frame; firstTime = player.time; Click(view, "reference_media.play", pointer); break;
                        case 1:
                            if (!player || !player.isPlaying || player.frame <= firstFrame + 2 || player.time <= firstTime + .1) return;
                            Check(!controller.Flow.Paused, "actual play advances native decoded frames and time without pausing the session", Observe(player));
                            captures.Add(CaptureNative(fixture, "Video_actual_playing", directory));
                            pausedTime = player.time; Click(view, "reference_minimize", pointer); break;
                        case 2:
                            if (now - phaseStarted < .4) return;
                            Check(controller.ReferenceMinimized && (!player || !player.isPlaying) && Math.Abs(Convert.ToDouble(JToken.FromObject(Read(reader, "Metadata"))["time_seconds"]) - pausedTime) < .08 && !view.IsReferenceActuallyVisible,
                                "minimize stops real playback and exposure", Observe(player));
                            captures.Add(CaptureNative(fixture, "Video_actual_minimized", directory));
                            Click(view, "reference_restore", pointer); break;
                        case 3:
                            if (now - phaseStarted < .4 || !player || !player.isPrepared || !Flag(reader, "IsReady")) return;
                            Check(!controller.ReferenceMinimized && (!player || !player.isPlaying) && Math.Abs(Convert.ToDouble(JToken.FromObject(Read(reader, "Metadata"))["time_seconds"]) - pausedTime) < .08, "restore retains video time without autoplay", Observe(player));
                            captures.Add(CaptureNative(fixture, "Video_actual_restored", directory));
                            firstFrame = player.frame; firstTime = player.time; Click(view, "reference_media.play", pointer); break;
                        case 4:
                            if (!player.isPlaying || player.frame <= firstFrame + 2 || player.time <= firstTime + .1) return;
                            Click(view, "reference_media.pause", pointer); pausedTime = player.time; break;
                        case 5:
                            if (now - phaseStarted < .4 || !Flag(reader, "IsReady")) return;
                            if (longSkipStage == 0) Check(!player.isPlaying && !controller.Flow.Paused && Math.Abs(player.time - pausedTime) < .08, "player Pause freezes the native clock but not the session", new { expected_paused_time = pausedTime, observed = Observe(player) });
                            if (longSkipStage == 0) captures.Add(CaptureNative(fixture, "Video_actual_player_pause", directory));
                            if (player.length > 20)
                            {
                                if (longSkipStage == 0)
                                { longSkipTarget = Math.Min(player.length - .001, player.time + 10); Click(view, "reference_media.seek_forward", pointer); longSkipStage = 1; return; }
                                if (longSkipStage == 1)
                                {
                                    Check(TargetFrameMatches(player, longSkipTarget) && !player.isPlaying, "long TEST native +10 control acknowledges actual decoded target", new { target = longSkipTarget, observed = Observe(player) });
                                    captures.Add(CaptureNative(fixture, "Video_long_skip_forward", directory));
                                    longSkipTarget = Math.Max(0, player.time - 10); Click(view, "reference_media.seek_back", pointer); longSkipStage = 2; return;
                                }
                                if (longSkipStage == 2)
                                {
                                    Check(TargetFrameMatches(player, longSkipTarget) && !player.isPlaying, "long TEST native -10 control acknowledges actual decoded target", new { target = longSkipTarget, observed = Observe(player) });
                                    captures.Add(CaptureNative(fixture, "Video_long_skip_back", directory)); longSkipStage = 3;
                                }
                            }
                            TimelineFreshCallbacks(view, controller, reader, Public<Camera>(fixture, "Camera"), pointer, Check, .9f, .95f); break;
                        case 6:
                            if ((string)Read(reader, "Status") == "Seeking" || !player || !player.isPrepared || !Flag(reader, "IsReady")) return;
                            var seek = Events(controller).LastOrDefault(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_COMPLETED");
                            Check(seek != null && player.time > pausedTime + .1 && !player.isPlaying && !controller.Flow.Paused,
                                "native seek completion changes media time without evaluator progress", new { observed = Observe(player), seek_event = seek });
                            captures.Add(CaptureNative(fixture, "Video_actual_seek_completed", directory));
                            timelineRestoreTime = player.time; replacedPlayerId = player.GetInstanceID();
                            TimelineStaleCallbacks(view, controller, reader, Public<Camera>(fixture, "Camera"), ownedEvents, pointer, Check);
                            Check(!Flag(reader, "IsReady") && !view.IsReferenceActuallyVisible, "stale timeline restore remains Loading until the replacement decoder and saved-time target frame are ready", Read(reader, "Metadata")); break;
                        case 7:
                            if (!player || !player.isPrepared || !Flag(reader, "IsReady")) return;
                            Check(player.GetInstanceID() != replacedPlayerId && !player.isPlaying && TargetFrameMatches(player, timelineRestoreTime) && Convert.ToInt64(Read(reader, "DecodedFrameCount")) > 0,
                                "timeline stale-context restoration reacquires a genuinely prepared replacement player at its saved time without autoplay", new { replaced_player_id = replacedPlayerId, current_player_id = player.GetInstanceID(), saved_time = timelineRestoreTime, observed = Observe(player) });
                            timelineFrames = Convert.ToInt64(Read(reader, "DecodedFrameCount"));
                            var request = TimelineFreshCallbacks(view, controller, reader, Public<Camera>(fixture, "Camera"), pointer, Check);
                            timelineRequestSeq = (long)request["seq"]; timelineTarget = (double)request["after"]["seek_target_seconds"]; timelineContext = (string)request["after"]["context"]; break;
                        case 8:
                            if (!player || !player.isPrepared || !Flag(reader, "IsReady")) return;
                            var timelineSeek = Events(controller).LastOrDefault(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_COMPLETED" && (long)row["seq"] > timelineRequestSeq && (string)row["after"]?["context"] == timelineContext);
                            Check(Flag(reader, "IsReady") && !player.isPlaying && TargetFrameMatches(player, timelineTarget)
                                && Convert.ToInt64(Read(reader, "DecodedFrameCount")) > timelineFrames && timelineSeek != null,
                                "actual fresh timeline native seek has its own later matching completion and genuine target frame", new { target = timelineTarget, request_seq = timelineRequestSeq, before_decoded_frames = timelineFrames, observed = Observe(player), timelineSeek });
                            captures.Add(CaptureNative(fixture, "Video_actual_timeline_seek", directory));
                            var pendingRequest = TimelineFreshCallbacks(view, controller, reader, Public<Camera>(fixture, "Camera"), pointer, Check, .55f, .6f);
                            pendingTarget = (double)pendingRequest["after"]["seek_target_seconds"]; replacedPlayerId = player.GetInstanceID();
                            Check(!Flag(reader, "IsReady") && !view.IsReferenceActuallyVisible, "pending native timeline seek is not successful reading", Read(reader, "Metadata"));
                            Click(view, "reference_minimize", pointer);
                            Check(!Flag(reader, "IsReady") && !(Read(reader, "NativeVideoPlayer") as UnityEngine.Video.VideoPlayer) && Math.Abs((double)Metadata(reader)["time_seconds"] - pendingTarget) < .001,
                                "minimize during pending native seek releases the old decoder and preserves the requested target, not a prior frame", new { requested_target = pendingTarget, metadata = Read(reader, "Metadata") });
                            captures.Add(CaptureNative(fixture, "Video_actual_pending_seek_minimized", directory));
                            Click(view, "reference_restore", pointer); pendingRestoreSeq = (long)Events(controller).Last()["seq"];
                            Check(!Flag(reader, "IsReady") && !view.IsReferenceActuallyVisible, "pending seek restore starts honest Loading rather than treating a saved timestamp as a decoded frame", Read(reader, "Metadata"));
                            captures.Add(CaptureNative(fixture, "Video_actual_pending_seek_restored_loading", directory)); break;
                        case 9:
                            if (!player || !player.isPrepared || !Flag(reader, "IsReady")) return;
                            if (!tailSeekPending)
                            {
                                var restoredEvents = Events(controller).Where(row => (long)row["seq"] > pendingRestoreSeq).ToArray();
                                var restoredRequest = restoredEvents.LastOrDefault(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_REQUESTED" && (string)row["after"]?["context"] == timelineContext && Math.Abs(((double?)row["after"]?["seek_target_seconds"] ?? -1000) - pendingTarget) < .001);
                                var restoredSeek = restoredEvents.LastOrDefault(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_COMPLETED" && (string)row["after"]?["context"] == timelineContext);
                                Check(player.GetInstanceID() != replacedPlayerId && !player.isPlaying && TargetFrameMatches(player, pendingTarget)
                                    && restoredRequest != null && restoredSeek != null && (long)restoredSeek["seq"] > (long)restoredRequest["seq"] && Convert.ToInt64(Read(reader, "DecodedFrameCount")) > 1
                                    && !restoredEvents.Any(row => (string)row["event"] == "REFERENCE_VIEWER_ACTIVE_BEGIN" && (long)row["seq"] < (long)(restoredSeek?["seq"] ?? long.MaxValue)),
                                    "pending seek minimize/restore becomes readable only after new native prepare and matching requested-target frame completion, without autoplay", new { pendingTarget, restoredRequest, restoredSeek, observed = Observe(player) });
                                captures.Add(CaptureNative(fixture, "Video_actual_pending_seek_restored_target", directory));
                                float tailFraction = (float)(Math.Max(0, player.length - 2) / player.length);
                                var tailRequest = TimelineFreshCallbacks(view, controller, reader, Public<Camera>(fixture, "Camera"), pointer, Check, tailFraction, tailFraction);
                                tailSeekRequestSeq = (long)tailRequest["seq"]; tailSeekPending = true;
                                phaseStarted = EditorApplication.timeSinceStartup; return;
                            }
                            var tailRequested = Events(controller).Single(row => (long)row["seq"] == tailSeekRequestSeq);
                            double tailTarget = (double)tailRequested["after"]["seek_target_seconds"];
                            var tailCompleted = Events(controller).LastOrDefault(row => (long)row["seq"] > tailSeekRequestSeq
                                && (string)row["event"] == "REFERENCE_VIDEO_SEEK_COMPLETED"
                                && (string)row["after"]?["context"] == timelineContext);
                            if (tailCompleted == null || !TargetFrameMatches(player, tailTarget)) return;
                            Check(tailCompleted != null && TargetFrameMatches(player, tailTarget) && !player.isPlaying,
                                "real near-end timeline seek decodes its requested frame before bounded Ended/Replay", new { tailTarget, tailSeekRequestSeq, tailCompleted, observed = Observe(player) });
                            Click(view, "reference_media.play", pointer); break;
                        case 10:
                            if ((string)Read(reader, "Status") != "Ended") return;
                            var ended = Events(controller).LastOrDefault(row => (string)row["event"] == "REFERENCE_VIDEO_ENDED");
                            Check(ended != null && !player.isPlaying && !controller.Flow.Paused && controller.Flow.State == ResearchFlowState.Task1Active && !controller.Flow.InspectPassed.HasValue,
                                "real decoder reaches Ended without completing or evaluating the task", new { observed = Observe(player), ended_event = ended, task_state = controller.Flow.State.ToString(), controller.Flow.InspectPassed });
                            captures.Add(CaptureNative(fixture, "Video_actual_ended", directory));
                            Click(view, "reference_media.play", pointer); break;
                        case 11:
                            if (!player.isPlaying || player.frame < 1 || player.time < .1) return;
                            TrackMedia();
                            pausedTime = player.time; Click(view, "reference_close", pointer); break;
                        case 12:
                            if (now - phaseStarted < .4) return;
                            Check(!controller.ReferenceOpen && (!player || !player.isPlaying) && Math.Abs(Convert.ToDouble(JToken.FromObject(Read(reader, "Metadata"))["time_seconds"]) - pausedTime) < .08 && !view.IsReferenceActuallyVisible,
                                "Close document stops native video without ending the session", Observe(player));
                            CheckReleased("initial Close"); Reopen(); break;
                        case 13:
                            if (!player || !player.isPrepared || !Flag(reader, "IsReady")) return;
                            TrackMedia();
                            Check(view.GetComponentsInChildren<UnityEngine.Video.VideoPlayer>(true).Length == 1 && retiredPlayers.Count(item => item) == 1
                                && retiredTextures.Count(item => item) == 1 && Field<AudioSource>(reader, "videoAudio").mute && !player.isPlaying,
                                "reopen cycle " + reopenCycle + " owns exactly one prepared decoder/texture, muted without autoplay", Observe(player));
                            var generationEvents = Events(controller).Skip(openedEvents).ToArray();
                            Check(generationEvents.Count(row => (string)row["event"] == "REFERENCE_VIDEO_PREPARED") == 1
                                && generationEvents.Count(row => (string)row["event"] == "REFERENCE_VIDEO_FIRST_FRAME_DECODED") == 1,
                                "reopen cycle " + reopenCycle + " receives prepare and first-frame callbacks exactly once", generationEvents);
                            captures.Add(CaptureNative(fixture, "Video_actual_reopen_" + controller.Language + "_cycle_" + reopenCycle, directory));
                            firstTime = player.time; Click(view, "reference_media.play", pointer); break;
                        case 14:
                            if (!player.isPlaying || player.time < firstTime + .2) return;
                            Check(!Field<AudioSource>(reader, "videoAudio").mute && !controller.Flow.Paused,
                                controller.Language + " actual native Play unmutes only this media, not session Pause", Observe(player));
                            captures.Add(CaptureNative(fixture, "Video_actual_playing_" + controller.Language + "_cycle_" + reopenCycle, directory));
                            Click(view, "reference_close", pointer); closedEvents = Events(controller).Count; break;
                        case 15:
                            if (now - phaseStarted < .4) return;
                            CheckReleased("reopen cycle " + reopenCycle + " Close");
                            var lateClosed = Events(controller).Skip(closedEvents).Where(row => new[] { "REFERENCE_VIDEO_PREPARED", "REFERENCE_VIDEO_FIRST_FRAME_DECODED", "REFERENCE_VIDEO_SEEK_COMPLETED", "REFERENCE_VIDEO_PLAYING", "REFERENCE_VIEWER_ACTIVE_BEGIN" }.Contains((string)row["event"])).ToArray();
                            Check(lateClosed.Length == 0 && !view.IsReferenceActuallyVisible && !Flag(reader, "ControlHeld"),
                                "closed reader accepts no late decoder/readiness callback or held input", lateClosed);
                            if (++reopenCycle < 3)
                            {
                                Set(controller, "SetupLanguage", fixedLocale ? language : reopenCycle == 1 ? ParticipantLanguage.Japanese : ParticipantLanguage.English);
                                Reopen(); stage = 13; phaseStarted = EditorApplication.timeSinceStartup; return;
                            }
                            Reopen(); break;
                        case 16:
                            if (!player || !Flag(reader, "IsReady")) return;
                            TrackMedia(); firstTime = player.time; Click(view, "reference_media.play", pointer); break;
                        case 17:
                            if (!player.isPlaying || player.time < firstTime + .2) return;
                            var playingRequest = TimelineFreshCallbacks(view, controller, reader, Public<Camera>(fixture, "Camera"), pointer, Check, .45f, .5f);
                            playingSeekTarget = (double)playingRequest["after"]["seek_target_seconds"]; playingSeekSeq = (long)playingRequest["seq"];
                            captures.Add(CaptureNative(fixture, "Video_actual_playing_seek_loading", directory)); break;
                        case 18:
                            var playingComplete = Events(controller).LastOrDefault(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_COMPLETED" && (long)row["seq"] > playingSeekSeq);
                            if (playingComplete == null || !player.isPlaying || !Flag(reader, "IsReady")) return;
                            Check(Math.Abs((double)playingComplete["after"]["time_seconds"] - playingSeekTarget) <= 2 / player.frameRate
                                && player.time >= playingSeekTarget && !Field<AudioSource>(reader, "videoAudio").mute && !controller.Flow.Paused,
                                "seek while actually playing receives native target ACK before resuming unmuted playback", new { playingSeekTarget, playingComplete, observed = Observe(player) });
                            captures.Add(CaptureNative(fixture, "Video_actual_playing_seek_resumed", directory));
                            TimelineFreshCallbacks(view, controller, reader, Public<Camera>(fixture, "Camera"), pointer, Check, .65f, .7f);
                            Click(view, "reference_tab.user_manual", pointer); closedEvents = Events(controller).Count; break;
                        case 19:
                            if (now - phaseStarted < .4) return;
                            CheckReleased("pending seek to PDF switch");
                            Check(controller.ReferenceSection == "user_manual" && Flag(reader, "IsReady") && !Flag(reader, "ControlHeld")
                                && !Events(controller).Skip(closedEvents).Any(row => (string)row["event"] == "REFERENCE_VIDEO_SEEK_COMPLETED"),
                                "switching to real PDF cancels pending video seek, old callbacks and held input", Read(reader, "Metadata"));
                            captures.Add(CaptureNative(fixture, "PDF_actual_after_pending_video_seek", directory));
                            Click(view, "reference_tab.video", pointer); break;
                        case 20:
                            if (!player || !Flag(reader, "IsReady")) return;
                            TrackMedia();
                            var invalidMovie = JsonUtility.FromJson<ResearchReferenceViewer.Package>(JsonUtility.ToJson(Field<ResearchReferenceViewer.Package>(reader, "package")));
                            invalidMovie.video.streaming_path = "ResearchReaderDemo/source.pdf"; invalidMovie.video.sha256 = "4d0e92c9af5cb28e096951e713c88b4bdb857ba876a21f32d7023996d34d2815";
                            typeof(ResearchReferenceViewer).GetMethod("CancelVideo", Hidden).Invoke(reader, null);
                            typeof(ResearchReferenceViewer).GetMethod("ClearContent", Hidden).Invoke(reader, null);
                            typeof(ResearchReferenceViewer).GetField("package", Hidden).SetValue(reader, invalidMovie);
                            typeof(ResearchReferenceViewer).GetMethod("Render", Hidden).Invoke(reader, null); Flush(view); break;
                        case 21:
                            if ((string)Read(reader, "Status") != "Error") return;
                            TrackMedia();
                            var errorEvent = Events(controller).LastOrDefault(row => (string)row["event"] == "REFERENCE_VIEWER_ERROR");
                            var retryAction = Action(view, "reference_media.retry");
                            Check(errorEvent != null && ((string)errorEvent["after"]?["error"] ?? "").StartsWith("Video error: ", StringComparison.Ordinal)
                                && retryAction.gameObject.activeInHierarchy && !Flag(reader, "IsReady") && !view.IsReferenceActuallyVisible
                                && Field<AudioSource>(reader, "videoAudio").mute,
                                "hash-valid non-video PDF reaches actual VideoPlayer errorReceived and visible Retry without successful reading", new { errorEvent, retry_label = retryAction.GetComponentInChildren<TMP_Text>().GetParsedText(), observed = Observe(player) });
                            Check(!Field<TMP_Text>(reader, "timeLabel").gameObject.activeInHierarchy && !((Slider)Read(reader, "Timeline")).gameObject.activeInHierarchy
                                && view.referencePanel.GetComponentsInChildren<ResearchUIAction>(true).Where(item => item.command.StartsWith("reference_media.") && item.command != "reference_media.retry").All(item => !item.gameObject.activeInHierarchy),
                                "native Error reserves the band for Retry without clock, timeline or transport overlap", Read(reader, "Metadata"));
                            captures.Add(CaptureNative(fixture, "Video_actual_native_error_retry_" + controller.Language + "_cycle_" + errorLocaleCycle, directory));
                            Click(view, "reference_media.retry", pointer); break;
                        case 22:
                            if (!player || !player.isPrepared || !Flag(reader, "IsReady")) return;
                            TrackMedia();
                            Check(Events(controller).Any(row => (string)row["event"] == "REFERENCE_VIEWER_RETRY") && !player.isPlaying
                                && Field<AudioSource>(reader, "videoAudio").mute && Convert.ToInt64(Read(reader, "DecodedFrameCount")) > 0,
                                "actual Retry UI reloads the original valid registry and native decoder without autoplay", Observe(player));
                            captures.Add(CaptureNative(fixture, "Video_actual_retry_recovered_" + controller.Language + "_cycle_" + errorLocaleCycle, directory));
                            if (++errorLocaleCycle < 3)
                            {
                                Set(controller, "SetupLanguage", fixedLocale ? language : errorLocaleCycle == 1 ? ParticipantLanguage.Thai : ParticipantLanguage.Japanese);
                                view.RefreshReference(); stage = 20; phaseStarted = EditorApplication.timeSinceStartup; return;
                            }
                            firstTime = player.time; Click(view, "reference_media.play", pointer); break;
                        case 23:
                            if (!player.isPlaying || player.time < firstTime + .2) return;
                            var focusTimeline = (Slider)Read(reader, "Timeline");
                            pointer.position = Public<Camera>(fixture, "Camera").WorldToScreenPoint(focusTimeline.handleRect.position);
                            pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = focusTimeline.gameObject };
                            ExecuteEvents.Execute(focusTimeline.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                            Check(Flag(reader, "TimelineHeld"), "focus-loss TEST begins with a real held timeline owner", Read(reader, "Metadata"));
                            typeof(ResearchReferenceViewer).GetMethod("OnApplicationFocus", Hidden).Invoke(reader, new object[] { false }); break;
                        case 24:
                            if (now - phaseStarted < .4) return;
                            CheckReleased("injected Unity focus-loss lifecycle");
                            Check(!Flag(reader, "ControlHeld") && !Flag(reader, "IsReady") && !controller.Flow.Paused,
                                "injected focus loss cancels held timeline and native reading without changing task pause", Read(reader, "Metadata"));
                            typeof(ResearchReferenceViewer).GetMethod("OnApplicationFocus", Hidden).Invoke(reader, new object[] { true }); view.RefreshReference(); break;
                        case 25:
                            if (!player || !Flag(reader, "IsReady")) return;
                            TrackMedia(); Check(!player.isPlaying && Field<AudioSource>(reader, "videoAudio").mute, "focus restore prepares muted without autoplay", Observe(player));
                            firstTime = player.time; Click(view, "reference_media.play", pointer); break;
                        case 26:
                            if (!player.isPlaying || player.time < firstTime + .2) return;
                            typeof(ResearchReferenceViewer).GetMethod("OnApplicationPause", Hidden).Invoke(reader, new object[] { true }); break;
                        case 27:
                            if (now - phaseStarted < .4) return;
                            CheckReleased("injected Unity application-pause lifecycle");
                            Check(!Flag(reader, "ControlHeld") && !Flag(reader, "IsReady"), "injected application pause stops native reading and controls", Read(reader, "Metadata"));
                            typeof(ResearchReferenceViewer).GetMethod("OnApplicationPause", Hidden).Invoke(reader, new object[] { false }); view.RefreshReference(); break;
                        case 28:
                            if (!player || !Flag(reader, "IsReady")) return;
                            TrackMedia(); Check(!player.isPlaying && Field<AudioSource>(reader, "videoAudio").mute, "application restore prepares muted without autoplay", Observe(player));
                            ((Behaviour)reader).enabled = false; break;
                        case 29:
                            if (now - phaseStarted < .4) return;
                            CheckReleased("actual component disable");
                            Check(!Flag(reader, "ControlHeld") && !Flag(reader, "IsReady"), "actual disable ends native reading and controls", Read(reader, "Metadata"));
                            ((Behaviour)reader).enabled = true; view.RefreshReference(); break;
                        case 30:
                            if (!player || !Flag(reader, "IsReady")) return;
                            TrackMedia(); Check(!player.isPlaying && Field<AudioSource>(reader, "videoAudio").mute, "component restore prepares muted without autoplay", Observe(player));
                            firstTime = player.time; Click(view, "reference_media.play", pointer); break;
                        case 31:
                            if (!player.isPlaying || player.time < firstTime + .2) return;
                            Click(view, "pause", pointer); break;
                        case 32:
                            if (now - phaseStarted < .4) return;
                            CheckReleased("actual session Pause UI");
                            Check(controller.Flow.Paused && !controller.ReferenceOpen && !Flag(reader, "ControlHeld") && !Flag(reader, "IsReady")
                                && !view.IsReferenceActuallyVisible && controller.Flow.State == ResearchFlowState.Task1Active && !controller.Flow.InspectPassed.HasValue
                                && controller.Record == null && !Events(controller).Any(row => new[] { "TASK_COMPLETED", "INSPECT", "REPAIR", "ACTION" }.Contains((string)row["event"])),
                                "actual header Pause closes media/reading without task result, consent or research record", new { observed = Observe(player), events = Events(controller).Where(row => (string)row["event"] == "SESSION_PAUSED").ToArray() });
                            Finish(); return;
                    }
                    stage++; phaseStarted = EditorApplication.timeSinceStartup;
                }
                catch (Exception exception) { error = exception.ToString(); Check(false, "native video execution", error); Finish(); }
            };
            started = phaseStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update += videoTick;
            return "READER_NATIVE_VIDEO_EDITOR_STARTED; " + path;
        }
    }
}
#endif
