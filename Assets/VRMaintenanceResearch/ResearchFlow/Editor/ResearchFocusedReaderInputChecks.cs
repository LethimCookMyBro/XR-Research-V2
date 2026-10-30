#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchFocusedReaderInputChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Owner = -73401, Other = -73402;
        static T PublicField<T>(object fixture, string name) => (T)fixture.GetType().GetField(name).GetValue(fixture);
        static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, Private).GetValue(source);
        static void Set(ResearchSessionFlowController controller, string name, object value)
            => typeof(ResearchSessionFlowController).GetProperty(name).GetSetMethod(true).Invoke(controller, new[] { value });
        static void Refresh(object fixture) => fixture.GetType().GetMethod("Refresh").Invoke(fixture, null);
        static void ScrollLateUpdate(ScrollRect scroll) => typeof(ScrollRect).GetMethod("LateUpdate", Private).Invoke(scroll, null);
        static object Capture(object fixture, string stage) => fixture.GetType().GetMethod("Capture").Invoke(fixture,
            new object[] { stage, PublicField<ResearchFlowView>(fixture, "View").referencePanel });
        static int Page(ResearchFlowView view) => (int)typeof(ResearchFlowView).GetProperty("ReferencePageIndex", Private).GetValue(view);
        static float[] Vector(Vector3 value) => new[] { value.x, value.y, value.z };
        static object DisableState(ResearchDragHandle handle, ResearchSessionFlowController controller) => new
        {
            handle.enabled,
            handle.isActiveAndEnabled,
            active_in_hierarchy = handle.gameObject.activeInHierarchy,
            handle.IsDragging,
            controller_active_in_hierarchy = controller.gameObject.activeInHierarchy,
            editor_is_playing = EditorApplication.isPlaying,
            preview_scene = EditorSceneManager.IsPreviewScene(handle.gameObject.scene),
            scene_name = handle.gameObject.scene.name
        };
        static float[] Frame(ResearchFlowView view)
        {
            var panel = (RectTransform)view.referencePanel.transform; var position = panel.position; var rotation = panel.rotation;
            var title = Field<TMP_Text>(view, "referenceTitle").rectTransform;
            var previous = Field<Button>(view, "previousPage").GetComponent<RectTransform>();
            return new[] { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w,
                panel.rect.width, panel.rect.height, title.anchoredPosition.x, title.anchoredPosition.y,
                previous.anchoredPosition.x, previous.anchoredPosition.y };
        }
        static bool Same(float[] before, float[] after) => before.Length == after.Length && before.Zip(after, (a, b) => Mathf.Abs(a - b)).All(d => d < .001f);

        public static string RunProduction(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor without XR required.");
            string dir = Path.GetFullPath(evidenceDirectory), project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar);
            if (!Path.IsPathRooted(evidenceDirectory) || Directory.Exists(dir) || File.Exists(dir) || dir.Equals(project, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Fresh absolute external evidence directory required.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Authored TH/JA/EN font donor required.");
            var ownedType = typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned", BindingFlags.NonPublic);
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            T Get<T>(object o, string n) => (T)o.GetType().GetField(n, all).GetValue(o);
            object Call(object o, string n, params object[] args) => o.GetType().GetMethod(n, all).Invoke(o, args);
            string Hash(byte[] bytes) { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
            var caller = SceneManager.GetActiveScene(); bool dirty = caller.isDirty; int previews = EditorSceneManager.previewSceneCount;
            var priorEvents = EventSystem.current;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var rows = new List<object>(); var captures = new List<object>(); var failures = new List<string>(); var callbackErrors = new List<string>(); var contexts = new List<object>();
            void Check(bool pass, string name, object data) { rows.Add(new { name, pass, data }); if (!pass) failures.Add(name); }
            Application.LogCallback onLog = (message, stack, kind) => { if (kind == LogType.Error || kind == LogType.Exception) callbackErrors.Add(message + "\n" + stack); };
            // Reviewed input pins must be refreshed deliberately when package bytes change.
            var frozenHashes = new Dictionary<string, string> {
                { "ResearchReaderS1/A_th/package", "b1d034e6c6852a88e79f6f94838af4b4fe8651cb03d0d27b4f7a1bac915429f5" },
                { "ResearchReaderS1/A_ja/package", "edb116d463c1d5920c1230c31c6b2922fafd0e8b3dc74f64ccb58f5273425095" },
                { "ResearchReaderS1/B_th/package", "a822d4c6a98f2edc2cef7353e4ae17a60555f2cfa97af3c1754ed556e8cf3477" },
                { "ResearchReaderS1/B_ja/package", "a154999ee0db3b2e3cea5a6d6481f704fc8c95040b958ab550d45c5a3abd9e65" },
                { "ResearchReaderS1/A_en/package", "8f25dcdd9dd6c29cf4d4b34c61ad0e2f372eba26c9d7985ee91ddf1d93be9199" },
                { "ResearchReaderS1/B_en/package", "64abd818c09c78fe4d0a3503bd20836542e839c8c4f937ef56bbd03f07c65eeb" },
                { "ResearchReaderS1/T2_th/package", "9b2e379a6dfd725a3b2d7faa3da6f3ccfe3ae108df81846e31ae320e58728da2" },
                { "ResearchReaderS1/T2_ja/package", "ccf38506f48cc7016017d769b33ca467af28deaf0127d6275cb110584f82d4d3" },
                { "ResearchReaderS1/T2_en/package", "de163e3145e251c84a92521b5a42772bd1588c912560b351758d6c76b0d9007d" },
                { "ResearchReaderS1/T3_th/package", "5a48a492bb5a394a9798d27c16acab25a495e627abb3374c55e2c84578c7fe65" },
                { "ResearchReaderS1/T3_ja/package", "25c55ec6270d457b1e3ce66e5000466b1697462669b7d80e9a505f8648e23e8c" },
                { "ResearchReaderS1/T3_en/package", "b746e1cd603f2029bd062558a7c02a61163522f6c92b7b87099b8609968559fe" },
            };
            Directory.CreateDirectory(dir); Application.logMessageReceived += onLog;
            try
            {
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
                foreach (var task in new[] { "A", "B", "T2", "T3" })
                {
                    bool training = task == "T2" || task == "T3";
                    if (language == ParticipantLanguage.English && !training) continue; // No English experimental A/B group.
                    object owned = null; ResearchFlowView visibilityView = null; Action<bool, string> visibility = null;
                    string prefix = language + "/" + task;
                    string locale = language == ParticipantLanguage.Thai ? "th" : language == ParticipantLanguage.Japanese ? "ja" : "en";
                    string eventTask = training ? "training" : task;
                    contexts.Add(new { task, language = language.ToString(), training, synthetic = true, historical_version_fixture = task == "T2" });
                    try
                    {
                        owned = Activator.CreateInstance(ownedType, all, null, new object[] { Path.Combine(dir, language + "_" + task), donor, language }, null);
                        var c = Get<ResearchSessionFlowController>(owned, "Controller"); var view = Get<ResearchFlowView>(owned, "View");
                        visibilityView = view;
                        visibility = (Action<bool, string>)Delegate.CreateDelegate(typeof(Action<bool, string>), c, c.GetType().GetMethod("ReferenceVisibilityChanged", all));
                        view.ReferenceVisibilityChanged += visibility;
                        string expectedTraining = task == "T2" ? ResearchUIPolicy.TrainingTrainerV2 : ResearchUIPolicy.TrainingTrainerV3;
                        if (task == "T2")
                        {
                            // Historical version fixture only, in the brand-new external TEST registry created by Owned.
                            // Never rewrite a live or pre-existing registry; normal resume then proves frozen-version routing.
                            string contextRoot = Path.GetFullPath(Path.Combine(dir, language + "_" + task)) + Path.DirectorySeparatorChar;
                            string sidecarPath = Path.GetFullPath(c.Registry.ProtocolPath);
                            if (!sidecarPath.StartsWith(contextRoot, StringComparison.OrdinalIgnoreCase) || c.Record.group != ParticipantGroup.Test || !c.Record.test_data
                                || c.Record.training_version != ResearchUIPolicy.TrainingTrainerV3) throw new InvalidOperationException("Fresh owned synthetic historical registry required.");
                            var historical = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(sidecarPath));
                            var entry = historical["sessions"]?[c.Record.session_id] as Newtonsoft.Json.Linq.JObject;
                            if (entry == null || (string)entry["training_version"] != ResearchUIPolicy.TrainingTrainerV3) throw new InvalidOperationException("Owned version sidecar precondition failed.");
                            entry["training_version"] = ResearchUIPolicy.TrainingTrainerV2;
                            File.WriteAllText(sidecarPath, historical.ToString(Formatting.Indented), new System.Text.UTF8Encoding(false));
                            var resumed = c.Registry.StartOrResume(c.Record.participant_id, language, ExperimentInputMode.Controller);
                            Set(c, "Record", resumed);
                            Check(resumed.training_version == expectedTraining, prefix + "/synthetic historical version loaded through normal resume", new { synthetic = true, sidecarPath, resumed.training_version });
                        }
                        Check(c.ReaderAuthorized && c.Record.group == ParticipantGroup.Test && c.Record.test_data, prefix + "/normal owned TEST record authorizes media only", new { c.Record.group, c.Record.test_data, c.Record.session_state });

                        if (task == "B") Call(owned, "Seed", ResearchFlowState.Task2Ready, 5, (bool?)null);
                        void Click(string command) { Call(owned, "Click", (ResearchUIAction)Call(owned, "Action", command, null), 1); }
                        if (training) Call(owned, "Seed", ResearchFlowState.TutorialReferenceInspect, 3, (bool?)null);
                        else { Click("task_start"); Click("task_start_confirm"); }
                        var timing = Get<ResearchTaskTiming>(c, "taskTiming");
                        bool contextReady = training ? c.Flow.IsTutorial && c.Record.training_version == expectedTraining && !timing.Started
                            : c.Flow.CurrentTask == task && c.Flow.IsTaskActive && timing.Started && !timing.Ended;
                        Check(contextReady && c.Logger.State == HandsLabQuest3.Logging.HandsLabLogState.Active,
                            prefix + "/owned production Reader context and writer", new { training, c.Flow.CurrentTask, timing.Started, timing.Ended, c.Record.participant_id, c.Record.run_id, c.Record.training_version });
                        if (!contextReady) throw new InvalidOperationException("Production Reader context precondition failed.");
                        string identity = c.Record.participant_id + ":" + c.Record.session_id + ":" + c.Record.run_id;
                        Click("reference");
                        var viewer = view.ReferenceViewer; var scroll = Field<ScrollRect>(view, "scroll");
                        void Flush()
                        {
                            Call(owned, "Refresh"); Canvas.ForceUpdateCanvases();
                            // Owned inactive preview does not receive a normal later Editor placement frame.
                            bool settled = false;
                            foreach (var panel in new[] { view.mainPanel, view.referencePanel })
                                if (panel && panel.gameObject.activeInHierarchy && panel.RecenterPending)
                                { Call(panel, "TryApplyPendingRecenter", true, true, true, Time.frameCount + 1); settled |= !panel.RecenterPending; }
                            if (settled) { view.RefreshReference(); Canvas.ForceUpdateCanvases(); }
                            ScrollLateUpdate(scroll); Call(viewer, "Update"); Canvas.ForceUpdateCanvases();
                        }
                        Newtonsoft.Json.Linq.JObject Meta() => Newtonsoft.Json.Linq.JObject.FromObject(viewer.Metadata);
                        Newtonsoft.Json.Linq.JObject[] Events()
                        {
                            using (var reader = new StreamReader(new FileStream(c.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                                return reader.ReadToEnd().Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Select(Newtonsoft.Json.Linq.JObject.Parse).ToArray();
                        }
                        void CaptureStage(string stage) => Call(owned, "Capture", stage, view.referencePanel, captures, rows);
                        Flush();
                        var pose = view.referencePanel.transform.position; var rotation = view.referencePanel.transform.rotation; var frame = Frame(view);
                        string resource = "ResearchReaderS1/" + task + "_" + locale + "/package";
                        var asset = Resources.Load<TextAsset>(resource);
                        if (!asset || !frozenHashes.TryGetValue(resource, out var expectedHash) || Hash(asset.bytes) != expectedHash) throw new InvalidOperationException("Current production package differs from frozen fixture: " + resource);
                        var package = Newtonsoft.Json.Linq.JObject.Parse(asset.text);
                        string expectedId = "s1.reader." + task + "." + locale;
                        Check(c.ReaderResource == resource && viewer.BoundRegistryResource == resource && (string)package["content_id"] == expectedId && (string)package["approval"] == "S1_ENGINEERING_DRAFT_PENDING_HUMAN_REVIEW",
                            prefix + "/actual S1 package selected, no DEMO substitution", new { resource, expectedHash, expectedId, approval = (string)package["approval"] });
                        Check(new[] { "user_manual", "step_by_step", "illustrated", "video" }.All(c.ReferenceAvailable), prefix + "/all four current formats available", resource);
                        string InspectCard(string format)
                        {
                            var meta = Meta(); int page = viewer.PageIndex;
                            int captionPart = format == "illustrated" ? (int?)meta["caption_part"] ?? 1 : 1;
                            bool materialized = viewer.IsReady && (string)meta["status"] == "Ready" && (bool?)meta["visible"] == true
                                && (bool?)meta["in_view"] == true && (bool?)meta["active_reading"] == true && (string)meta["registry_sha256"] == expectedHash;
                            string expectedText = null; string actualText = null; bool contentCorrect; object textLayout = null;
                            if (format == "user_manual")
                            {
                                var image = scroll.content.GetComponentsInChildren<RawImage>(true).SingleOrDefault(item => item.name == "PDFPage_" + page);
                                contentCorrect = image && image.texture && image.texture.width > 0 && image.texture.height > 0;
                            }
                            else
                            {
                                var label = Field<TMP_Text>(viewer, "contentText"); label.ForceMeshUpdate(true, true); actualText = label.text;
                                if (format == "step_by_step") expectedText = (string)package["steps"][page - 1]["text_" + locale];
                                else
                                {
                                    string caption = (string)package["images"][page - 1]["caption_" + locale];
                                    var (heading, parts) = ResearchCaptionParts.Split(caption);
                                    expectedText = heading == null ? parts[captionPart - 1]
                                        : (heading.IndexOf('<') < 0 ? "<b>" + heading + "</b>" : heading) + "\n" + parts[captionPart - 1];
                                }
                                contentCorrect = !string.IsNullOrWhiteSpace(expectedText) && actualText == expectedText && label.textInfo.characterCount > 0 && !label.isTextOverflowing;
                                var textRect = label.rectTransform.rect;
                                textLayout = new { label.isTextOverflowing, label.isTextTruncated, label.fontSize, textRect.width, textRect.height,
                                    preferred_height = label.preferredHeight, label.textInfo.characterCount };
                                if (format == "illustrated")
                                {
                                    var image = scroll.content.GetComponentsInChildren<RawImage>(true).SingleOrDefault(item => item.name == "SourceImage_" + page);
                                    contentCorrect &= image && image.texture && image.texture.width > 0 && image.texture.height > 0;
                                }
                            }
                            Check(materialized && contentCorrect, prefix + "/" + format + "/card_" + page + "_" + captionPart + "/actual current production UI content materialized", new {
                                meta, contentCorrect, textLayout, expected_text_sha256 = expectedText == null ? null : Hash(System.Text.Encoding.UTF8.GetBytes(expectedText)),
                                actual_text_sha256 = actualText == null ? null : Hash(System.Text.Encoding.UTF8.GetBytes(actualText)) });
                            if (!materialized || !contentCorrect) CaptureStage(format + "_card_" + page + "_" + captionPart + "_failure");
                            if (format == "illustrated" && language == ParticipantLanguage.English && page == 3) CaptureStage("illustrated_caption_margin_regression");
                            return page + ":" + captionPart;
                        }
                        var pointer = new PointerEventData(Get<EventSystem>(owned, "eventSystem")) { pointerId = Owner, button = PointerEventData.InputButton.Left };
                        var other = new PointerEventData(Get<EventSystem>(owned, "eventSystem")) { pointerId = Other, button = PointerEventData.InputButton.Left };
                        float pdfOffset = 1, pdfHorizontal = .5f; int pdfPage = 1;
                        Check(training ? Events().Any(e => (string)e["event"] == "INFORMATION_OPEN" && (string)e["task_id"] == eventTask
                            && (string)e["after"]?["viewer"]?["content_id"] == expectedId)
                            : viewer.Status == "AwaitingSelection" && !viewer.IsReady && !Events().Any(e => (string)e["event"] == "INFORMATION_OPEN"),
                            prefix + (training ? "/initial training automatic content exposure" : "/task chooser is not content exposure"), Meta());
                        if (viewer.BoundSection == "user_manual") { Click("reference_tab.step_by_step"); Flush(); }
                        foreach (string format in new[] { "user_manual", "step_by_step", "illustrated", "video" })
                        {
                            int before = Events().Length;
                            Click("reference_tab." + format); Flush();
                            var meta = Meta(); var events = Events().Skip(before).ToArray();
                            Check(c.ReferenceOpen && c.ReferenceSection == format && viewer.BoundSection == format && viewer.BoundRegistryResource == resource && (string)meta["content_id"] == expectedId
                                && (string)meta["version"] == (string)package["version"] && (string)meta["registry_sha256"] == expectedHash,
                                prefix + "/" + format + "/real pointer selection binds exact package", meta);
                            var selected = events.SingleOrDefault(e => (string)e["event"] == "INFORMATION_FORMAT_SELECTED");
                            Check(selected != null && (string)selected["task_id"] == eventTask && (string)selected["after"]?["language"] == language.ToString()
                                && (bool?)selected["after"]?["deliberate"] == true && (bool?)selected["after"]?["incoming_binding_pending"] == true && selected["after"]?["viewer"] == null,
                                prefix + "/" + format + "/selection logged as intent, not false incoming exposure", selected);
                            Check(events.Any(e => (string)e["event"] == "REFERENCE_VIEWER_BOUND" && (string)e["after"]?["content_id"] == expectedId),
                                prefix + "/" + format + "/materialized binding logged", events.Where(e => (string)e["event"] == "REFERENCE_VIEWER_BOUND").ToArray());
                            if (format == "video")
                            {
                                string video = Path.Combine(Application.streamingAssetsPath, (string)package["video"]?["streaming_path"]);
                                Check(File.Exists(video) && Hash(File.ReadAllBytes(video)) == (string)package["video"]?["sha256"], prefix + "/video/current source bytes verified", new { video, sha256 = (string)package["video"]?["sha256"] });
                                Check(viewer.Status == "Loading" && !viewer.IsReady && viewer.DecodedFrameCount == 0 && !events.Any(e => (string)e["event"] == "INFORMATION_OPEN"),
                                    prefix + "/video/stopped Editor does not claim decoded reading", meta);
                                CaptureStage("video_loading_not_native_playback");
                                continue;
                            }
                            int expectedCount = ((Newtonsoft.Json.Linq.JArray)package[format == "user_manual" ? "pages" : format == "step_by_step" ? "steps" : "images"]).Count;
                            Check(viewer.IsReady && viewer.PageCount == expectedCount && events.Any(e => (string)e["event"] == "INFORMATION_OPEN" && (string)e["after"]?["viewer"]?["content_id"] == expectedId),
                                prefix + "/" + format + "/actual current content count and exposure", new { viewer.PageCount, expectedCount, viewer.IsReady, events });
                            CaptureStage(format + "_first");
                            var visitedCards = new HashSet<string> { InspectCard(format) };
                            if (format == "user_manual")
                            {
                                var focus = view.referencePanel.GetComponent<ResearchSlateFocus>();
                                scroll.verticalNormalizedPosition = 1; Flush(); float top = scroll.verticalNormalizedPosition;
                                focus.Scroll(-1, .6f); Flush(); Check(Mathf.Abs(scroll.verticalNormalizedPosition - top) < .001f, prefix + "/PDF/unfocused consumer does not scroll", scroll.verticalNormalizedPosition);
                                focus.OnPointerEnter(pointer); focus.OnPointerExit(other);
                                foreach (var invalid in new[] { (.1f,.6f), (float.NaN,.6f), (float.PositiveInfinity,.6f), (-1f,0f), (-1f,float.NaN) }) focus.Scroll(invalid.Item1, invalid.Item2);
                                Flush(); Check(focus.Focused && Mathf.Abs(scroll.verticalNormalizedPosition - top) < .001f, prefix + "/PDF/ownership deadzone invalid axis guards", scroll.verticalNormalizedPosition);
                                focus.Scroll(-1, .6f); Flush(); Check(scroll.verticalNormalizedPosition < top - .001f, prefix + "/PDF/focused actual consumer scrolls", scroll.verticalNormalizedPosition);
                                focus.OnPointerExit(pointer);
                                float boundaryDelta = (scroll.content.rect.height / viewer.PageCount + scroll.viewport.rect.height) / Mathf.Max(.001f, scroll.scrollSensitivity);
                                // Same-frame LateUpdate, as in the player loop; Flush's Refresh+canvas rebuild would otherwise reset the ScrollRect's previous position first.
                                pointer.scrollDelta = new Vector2(0, -boundaryDelta); ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.scrollHandler); ScrollLateUpdate(scroll); Flush();
                                Check(scroll.verticalNormalizedPosition < .999f && viewer.PageIndex > 1, prefix + "/PDF/actual ScrollRect callback changes page", Meta());
                                int transitions = 0;
                                while (viewer.CanPrevious && transitions++ < 128) { Click("reference_previous"); Flush(); }
                                Check(viewer.PageIndex == 1 && transitions < 128, prefix + "/PDF/actual previous controls return to first page", Meta());
                                visitedCards.Add(InspectCard(format));
                                while (viewer.CanNext && transitions++ < 128) { Click("reference_next"); Flush(); visitedCards.Add(InspectCard(format)); }
                                Check(transitions < 128 && visitedCards.SetEquals(Enumerable.Range(1, expectedCount).Select(page => page + ":1")),
                                    prefix + "/PDF/every authored page materialized through actual controls", visitedCards.ToArray());
                                int pageBeforeZoom = viewer.PageIndex; Click("reference_media.zoom_in"); Flush();
                                Check(Mathf.Abs((float)Meta()["pdf_zoom"] - 1.25f) < .001f && viewer.PageIndex == pageBeforeZoom, prefix + "/PDF/actual zoom pointer grows while retaining page", Meta());
                                Click("reference_media.pan_right"); Flush();
                                pdfOffset = scroll.verticalNormalizedPosition; pdfHorizontal = scroll.horizontalNormalizedPosition; pdfPage = viewer.PageIndex;
                                Check(Events().Any(e => (string)e["event"] == "REFERENCE_PDF_VIEW_CHANGED" && (string)e["task_id"] == eventTask), prefix + "/PDF/actual view change event", Events().Where(e => (string)e["event"] == "REFERENCE_PDF_VIEW_CHANGED").ToArray());
                                CaptureStage("PDF_scrolled_zoomed");
                            }
                            else
                            {
                                int steps = 0;
                                while (viewer.CanNext && steps++ < 128) { Click("reference_next"); Flush(); visitedCards.Add(InspectCard(format)); }
                                Check(!viewer.CanNext && viewer.PageIndex == expectedCount && steps < 128, prefix + "/" + format + "/all authored pages and caption parts reachable", Meta());
                                var expectedCards = format == "step_by_step" ? Enumerable.Range(1, expectedCount).Select(page => page + ":1")
                                    : Enumerable.Range(1, expectedCount).SelectMany(page => Enumerable.Range(1, ResearchCaptionParts.Count((string)package["images"][page - 1]["caption_" + locale])).Select(part => page + ":" + part));
                                Check(visitedCards.SetEquals(expectedCards), prefix + "/" + format + "/every authored card and caption part inspected", visitedCards.ToArray());
                                Check(!view.LocalCommand("reference_next", "None"), prefix + "/" + format + "/end boundary rejects no-op", Meta());
                                CaptureStage(format + "_last");
                            }
                            Check(Vector3.Distance(pose, view.referencePanel.transform.position) < .001f && Quaternion.Angle(rotation, view.referencePanel.transform.rotation) < .001f && Same(frame, Frame(view)),
                                prefix + "/" + format + "/fixed shell and pose retained", new { before = frame, after = Frame(view) });
                        }
                        Click("reference_tab.user_manual"); Flush();
                        Check(viewer.PageIndex == pdfPage && Mathf.Abs(scroll.verticalNormalizedPosition - pdfOffset) < .001f && Mathf.Abs(scroll.horizontalNormalizedPosition - pdfHorizontal) < .001f
                            && Mathf.Abs((float)Meta()["pdf_zoom"] - 1.25f) < .001f, prefix + "/PDF/format return retains page scroll zoom pan", Meta());
                        Click("reference_close"); Click("reference"); Flush();
                        Check(c.ReferenceOpen && viewer.BoundSection == "user_manual" && viewer.PageIndex == pdfPage && Mathf.Abs(scroll.verticalNormalizedPosition - pdfOffset) < .001f
                            && Mathf.Abs((float)Meta()["pdf_zoom"] - 1.25f) < .001f, prefix + "/PDF/close-reopen retains progress", Meta());
                        Check(Vector3.Distance(pose, view.referencePanel.transform.position) < .001f && Quaternion.Angle(rotation, view.referencePanel.transform.rotation) < .001f,
                            prefix + "/fixed panel close-reopen retains pose", view.referencePanel.Pose());
                        Check(c.Command("pause", "None") && c.Flow.Paused, prefix + "/actual pause accepted", c.Flow.Paused);
                        float paused = scroll.verticalNormalizedPosition; var slate = view.referencePanel.GetComponent<ResearchSlateFocus>(); slate.OnPointerEnter(pointer); slate.Scroll(-1, .6f);
                        Check(!view.LocalCommand("reference_media.zoom_in", "None") && !view.LocalCommand("reference_next", "None") && Mathf.Abs(scroll.verticalNormalizedPosition - paused) < .001f,
                            prefix + "/paused input guards preserve document", Meta());
                        Check(identity == c.Record.participant_id + ":" + c.Record.session_id + ":" + c.Record.run_id && c.Registry.List(ParticipantGroup.Test).Count == 1
                            && !timing.Ended && Events().Count(e => (string)e["event"] == "TASK_STARTED") == (training ? 0 : 1)
                            && !Events().Any(e => (string)e["event"] == "INSPECT_PASS" || (string)e["event"] == "TASK_COMPLETED"),
                            prefix + "/single owned task no scoring or fabricated completion", new { identity, eventsPath = c.Logger.EventsPath });
                        Check(c.Registry.List(ParticipantGroup.Test).Single().training_version == expectedTraining,
                            prefix + "/normal pause persistence preserves session training version", new { expectedTraining, c.Record.training_version });
                        failures.AddRange(Get<List<string>>(owned, "TextOverflows").Select(v => prefix + "/" + v));
                    }
                    catch (Exception e) { Check(false, prefix + "/execution", (e is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : e).ToString()); }
                    finally
                    {
                        if (visibilityView && visibility != null) visibilityView.ReferenceVisibilityChanged -= visibility;
                        (owned as IDisposable)?.Dispose();
                    }
                }
            }
            finally { Application.logMessageReceived -= onLog; foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value; }
            Check(callbackErrors.Count == 0, "no swallowed Unity callback exceptions", callbackErrors);
            Check(SceneManager.GetActiveScene() == caller && caller.isDirty == dirty && EditorSceneManager.previewSceneCount == previews && EventSystem.current == priorEvents, "caller scene dirty previews EventSystem preserved", new { dirty, previews });
            string report = Path.Combine(dir, "reader-production.json");
            File.WriteAllText(report, JsonConvert.SerializeObject(new { rows, captures, failures, pass = failures.Count == 0, frozenHashes, coverage = new { expected_contexts = 10, attempted_contexts = contexts.Count, formats_per_context = 4, contexts, english_experimental_tasks = false, video_evidence = "SOURCE_HASH_AND_STOPPED_LOADING_ONLY" },
                scope = "Current production-path engineering-draft A/B TH/JA and T2/T3 TH/JA/EN. Owned normal TEST creation/resume; T2 uses explicit synthetic historical version seed only in fresh external fixture. Every PDF page, Steps card and Illustrated caption part inspected through UI state/text/texture; existing first/last mono captures only. Programmatic PointerEventData and actual mono preview pixels, not SDK/native/wearer input. Video source SHA and stopped Loading only; actual decoded VideoPlayer frames/audio/play/pause/seek/end require separate guarded Play/Quest verification. No English experimental group, Study/content/language approval or task completion claim." }, Formatting.Indented));
            if (failures.Count != 0) throw new InvalidOperationException("READER_PRODUCTION_FAIL " + report);
            return "READER_PRODUCTION_PASS " + report;
        }

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Reader input fixture requires stopped Play Mode and no running XR subsystem.");
            string directory = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "reader-input.json");
            if (File.Exists(path)) throw new IOException("Refusing to overwrite prior reader input evidence: " + path);
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load the authored ResearchFlow scene with all three fonts.");
            var type = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            var caller = SceneManager.GetActiveScene(); bool dirty = caller.isDirty; int previews = EditorSceneManager.previewSceneCount;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var rows = new List<object>(); var captures = new List<object>(); var lifecycleDiagnostics = new List<object>(); int failures = 0;
            void Check(bool pass, string name, object observed)
            { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
            {
                object fixture = null;
                try
                {
                    fixture = Activator.CreateInstance(type, new object[] { Path.Combine(directory, language.ToString()), donor, language });
                    var controller = PublicField<ResearchSessionFlowController>(fixture, "Controller");
                    var view = PublicField<ResearchFlowView>(fixture, "View"); var camera = PublicField<Camera>(fixture, "Camera");
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                    Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "user_manual");
                    Refresh(fixture);
                    var scroll = Field<ScrollRect>(view, "scroll"); var body = Field<TMP_Text>(view, "referenceBody");
                    var focus = view.referencePanel.GetComponent<ResearchSlateFocus>();
                    view.referencePanel.GetComponent<Canvas>().worldCamera = camera;
                    Check(scroll && focus && focus.scroll == scroll && focus.view == view, language + "/actual reader uses existing ScrollRect and ResearchSlateFocus",
                        new { focus_owner = focus ? focus.GetType().FullName : null, controller_axis_owner = "ResearchSessionFlowController.ReferenceScrollAxis / Right tracked IController.Primary2DAxis", synthetic_consumer_input = true });
                    body.ForceMeshUpdate(true, true);
                    bool longDocument = body.textInfo.pageCount >= 2 && scroll.content.rect.height > scroll.viewport.rect.height + 1;
                    Check(longDocument, language + "/existing fictitious-demo user manual has real TMP multipage content",
                        new { pages = body.textInfo.pageCount, content_height = scroll.content.rect.height, viewport_height = scroll.viewport.rect.height,
                            demo_content_version = ResearchDemoManual.ContentVersion, study_approved_content = false });
                    var pointer = new PointerEventData(EventSystem.current) { pointerId = Owner, button = PointerEventData.InputButton.Left };
                    var other = new PointerEventData(EventSystem.current) { pointerId = Other, button = PointerEventData.InputButton.Left };
                    if (longDocument)
                    {
                        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; ScrollLateUpdate(scroll);
                        var frame = Frame(view); captures.Add(Capture(fixture, "manual_before_scroll"));
                        float top = scroll.verticalNormalizedPosition;
                        focus.Scroll(-1, .6f);
                        Check(Mathf.Abs(scroll.verticalNormalizedPosition - top) < .001f, language + "/unfocused thumbstick consumer does not scroll", scroll.verticalNormalizedPosition);
                        focus.OnPointerEnter(pointer); focus.OnPointerExit(other);
                        Check(focus.Focused, language + "/unregistered pointer exit cannot clear current focus owner", focus.Focused);
                        foreach (var input in new[] { (.1f, .6f), (float.NaN, .6f), (float.PositiveInfinity, .6f), (-1f, 0f), (-1f, float.NaN) }) focus.Scroll(input.Item1, input.Item2);
                        Check(Mathf.Abs(scroll.verticalNormalizedPosition - top) < .001f, language + "/deadzone and invalid timing/axis do not scroll", scroll.verticalNormalizedPosition);
                        focus.Scroll(-1, .6f); ScrollLateUpdate(scroll); body.ForceMeshUpdate(true, true);
                        Check(scroll.verticalNormalizedPosition < top - .001f && Page(view) > 1 && body.pageToDisplay == Page(view),
                            language + "/actual focused thumbstick consumer renders a distinct TMP page",
                            new { before = top, after = scroll.verticalNormalizedPosition, page = Page(view), rendered_page = body.pageToDisplay });
                        Check(Same(frame, Frame(view)), language + "/page change preserves world frame and header/pager aiming targets", new { before = frame, after = Frame(view) });
                        captures.Add(Capture(fixture, "manual_thumbstick_scroll"));
                        controller.Flow.Pause(); float paused = scroll.verticalNormalizedPosition; focus.Scroll(-1, .6f);
                        Check(Mathf.Abs(scroll.verticalNormalizedPosition - paused) < .001f, language + "/paused flow rejects focused scrolling", scroll.verticalNormalizedPosition);
                        controller.Flow.ResearcherResume();
                        focus.OnPointerExit(pointer); focus.Scroll(-1, .6f);
                        Check(!focus.Focused && Mathf.Abs(scroll.verticalNormalizedPosition - paused) < .001f, language + "/matching exit releases focus and scroll ownership", focus.Focused);
                        scroll.verticalNormalizedPosition = 1; ScrollLateUpdate(scroll);
                        pointer.scrollDelta = new Vector2(0, -240);
                        scroll.OnScroll(pointer); ScrollLateUpdate(scroll); body.ForceMeshUpdate(true, true);
                        Check(scroll.verticalNormalizedPosition < .99f && Page(view) > 1 && body.pageToDisplay == Page(view),
                            language + "/real ScrollRect.OnScroll callback reaches the page listener",
                            new { scroll_delta = new[] { pointer.scrollDelta.x, pointer.scrollDelta.y }, normalized = scroll.verticalNormalizedPosition, page = Page(view), rendered_page = body.pageToDisplay });
                        Check(Same(frame, Frame(view)), language + "/OnScroll keeps reader frame stable", new { before = frame, after = Frame(view) });
                        captures.Add(Capture(fixture, "manual_OnScroll"));
                        scroll.verticalNormalizedPosition = 1; ScrollLateUpdate(scroll);
                        pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = scroll.gameObject };
                        pointer.position = camera.WorldToScreenPoint(scroll.viewport.position);
                        scroll.OnInitializePotentialDrag(pointer); scroll.OnBeginDrag(pointer); pointer.position += Vector2.up * 120;
                        scroll.OnDrag(pointer); scroll.OnEndDrag(pointer); scroll.StopMovement(); ScrollLateUpdate(scroll);
                        Check(scroll.verticalNormalizedPosition < .99f, language + "/real content-surface drag scrolls without a narrow scrollbar", scroll.verticalNormalizedPosition);
                        Check(Same(frame, Frame(view)), language + "/content drag keeps the slate world frame stable", new { before = frame, after = Frame(view) });
                        captures.Add(Capture(fixture, "manual_content_drag"));
                        float saved = scroll.verticalNormalizedPosition;
                        foreach (string format in new[] { "step_by_step", "user_manual", "step_by_step", "user_manual" })
                        {
                            bool accepted = view.LocalCommand("reference_tab." + format, "None"); ScrollLateUpdate(scroll);
                            Check(accepted && controller.ReferenceSection == format && controller.ReferenceOpen && string.IsNullOrEmpty(controller.Error),
                                language + "/rapid real format command selects " + format, new { accepted, controller.ReferenceSection, controller.ReferenceOpen, controller.Error, frame = Frame(view) });
                        }
                        Check(Mathf.Abs(scroll.verticalNormalizedPosition - saved) < .001f, language + "/return to manual restores its actual scroll position",
                            new { before_switch = saved, after_return = scroll.verticalNormalizedPosition });
                        var beforeMinimize = Vector(view.referencePanel.transform.position);
                        bool minimized = view.LocalCommand("reference_minimize", "None");
                        Check(minimized && controller.ReferenceMinimized && controller.ReferenceOpen && Same(beforeMinimize, Vector(view.referencePanel.transform.position)),
                            language + "/real minimize keeps the reader world position", new { minimized, controller.ReferenceMinimized, position = Vector(view.referencePanel.transform.position) });
                        bool restored = view.LocalCommand("reference_restore", "None"); ScrollLateUpdate(scroll);
                        Check(restored && !controller.ReferenceMinimized && controller.ReferenceOpen && Same(beforeMinimize, Vector(view.referencePanel.transform.position))
                            && Mathf.Abs(scroll.verticalNormalizedPosition - saved) < .001f,
                            language + "/real restore preserves reader position and document progress", new { restored, controller.ReferenceMinimized, normalized = scroll.verticalNormalizedPosition, position = Vector(view.referencePanel.transform.position) });
                        captures.Add(Capture(fixture, "manual_restored"));
                    }

                    var handle = view.referencePanel.GetComponentsInChildren<ResearchDragHandle>(true).Single(h => h.panel == view.referencePanel);
                    pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = handle.gameObject };
                    pointer.position = camera.WorldToScreenPoint(handle.transform.position);
                    handle.OnBeginDrag(pointer); var beforeDrag = view.referencePanel.transform.position;
                    Check(handle.IsDragging, language + "/existing owned handle accepts a valid programmatic press camera", handle.IsDragging);
                    other.pointerPressRaycast = pointer.pointerPressRaycast; other.position = pointer.position + Vector2.right * 100;
                    handle.OnDrag(other); handle.OnEndDrag(other);
                    Check(handle.IsDragging && Vector3.Distance(beforeDrag, view.referencePanel.transform.position) < .001f,
                        language + "/wrong pointer cannot move or finish the acquired panel", new { handle.IsDragging, position = Vector(view.referencePanel.transform.position) });
                    pointer.position = camera.WorldToScreenPoint(handle.transform.position - camera.transform.right * .04f);
                    handle.OnDrag(pointer); handle.OnEndDrag(pointer);
                    Check(!handle.IsDragging && Vector3.Distance(beforeDrag, view.referencePanel.transform.position) > .001f,
                        language + "/matching pointer moves and releases the existing handle", new { handle.IsDragging, handle.MoveSamples, position = Vector(view.referencePanel.transform.position) });
                    captures.Add(Capture(fixture, "panel_owned_drag"));
                    pointer.position = camera.WorldToScreenPoint(handle.transform.position); handle.OnBeginDrag(pointer);
                    Check(handle.IsDragging, language + "/second drag owns a new acquisition", handle.IsDragging);
                    var beforeDisable = DisableState(handle, controller);
                    handle.enabled = false;
                    var afterPropertyDisable = DisableState(handle, controller);
                    bool explicitCallbackRequired = handle.IsDragging;
                    // This non-ExecuteAlways behaviour does not receive runtime lifecycle callbacks
                    // from an enabled-property change in the stopped Editor PreviewScene fixture.
                    // Exercise the existing cancellation callback without claiming runtime delivery.
                    if (explicitCallbackRequired)
                    {
                        var onDisable = typeof(ResearchDragHandle).GetMethod("OnDisable", Private);
                        if (onDisable == null) throw new MissingMethodException(typeof(ResearchDragHandle).FullName, "OnDisable");
                        onDisable.Invoke(handle, null);
                    }
                    var afterCallback = DisableState(handle, controller);
                    lifecycleDiagnostics.Add(new { language = language.ToString(), before_disable = beforeDisable,
                        after_enabled_property_disable = afterPropertyDisable, explicit_callback_required = explicitCallbackRequired,
                        callback = "existing ResearchDragHandle.OnDisable", after_callback = afterCallback,
                        scope = explicitCallbackRequired ? "Direct existing callback invocation in stopped Editor PreviewScene; automatic Unity runtime lifecycle delivery unverified."
                            : "Cancellation observed after enabled-property disable in stopped Editor PreviewScene; Unity runtime lifecycle delivery unverified." });
                    Check(!handle.IsDragging, language + "/existing disable callback cancels the acquired drag", new
                        { handle.IsDragging, explicit_callback_required = explicitCallbackRequired, release_cause = Field<string>(handle, "releaseCause") });
                    handle.enabled = true;
                    Check(controller.Flow.State == ResearchFlowState.Task1Active && controller.Record == null && string.IsNullOrEmpty(controller.Error),
                        language + "/fixture never evaluates or completes a study task", new { state = controller.Flow.State.ToString(), record_attached = controller.Record != null, controller.Error });
                }
                catch (Exception error) { Check(false, language + "/fixture completes", error.ToString()); }
                finally { if (fixture != null) ((IDisposable)fixture).Dispose(); foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value; }
            }
            Check(SceneManager.GetActiveScene() == caller && caller.isDirty == dirty && EditorSceneManager.previewSceneCount == previews,
                "caller scene and previews preserved", new { caller.path, dirty, current_dirty = caller.isDirty, current_previews = EditorSceneManager.previewSceneCount });
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = failures == 0 ? "PASS" : "FAIL", failures, rows, captures, lifecycle_diagnostics = lifecycleDiagnostics,
                scope = "Actual ResearchSlateFocus.Scroll, ScrollRect callbacks/LateUpdate, existing LocalCommand format/minimize/restore and ResearchDragHandle ownership callbacks on isolated TEST fixture and authored fonts. Existing ResearchDragHandle.OnDisable explicitly invoked if stopped EditMode does not deliver it; this verifies callback cancellation, not Unity runtime disable lifecycle. Existing fictitious-demo manual, not approved study content. No native SDK pointer acquisition, physical thumbstick polling, Quest, consent, task outcome or wearer acceptance.", hardware_verified = false }, Formatting.Indented));
            if (failures != 0) throw new InvalidOperationException("FOCUSED_READER_INPUT_FAIL " + failures + "; " + path);
            return "FOCUSED_READER_INPUT_PASS " + rows.Count + "; " + path;
        }
    public static string BeginTemporal(string evidenceDirectory)
    {
        string directory = Path.GetFullPath(evidenceDirectory);
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        if (Application.dataPath.Replace('\\', '/') != "D:/VR_TMU/XR-Research-V2/Assets" || EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
            throw new InvalidOperationException("Requires the bound project in stopped EditMode without XR.");
        Directory.CreateDirectory(directory);
        using (var marker = new FileStream(Path.Combine(directory, "started"), FileMode.CreateNew)) { }
        var caller = SceneManager.GetActiveScene(); bool dirty = caller.isDirty; int previews = EditorSceneManager.previewSceneCount;
        var currentEvents = EventSystem.current;
        Type LoadedType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
        var cursorType = LoadedType("Oculus.Interaction.RayInteractorCursorVisual");
        var hoverColor = cursorType.GetProperty("HoverColor");
        var colors = Resources.FindObjectsOfTypeAll(cursorType).Select(x => new { cursor = (Component)x, color = (Color)hoverColor.GetValue(x) }).ToArray();
        var serializer = LoadedType("Newtonsoft.Json.JsonConvert").GetMethod("SerializeObject", new[] { typeof(object) });
        var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().First(v => v.thaiFont && v.japaneseFont && v.englishFont);
        var fixtureType = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
        var capture = typeof(ResearchRecoveryTemporalChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        var moduleType = LoadedType("Oculus.Interaction.PointableCanvasModule");
        var processDrag = moduleType.GetMethod("ProcessDrag", hidden);
        var lateScroll = typeof(UnityEngine.UI.ScrollRect).GetMethod("LateUpdate", hidden);
        var rows = new List<object>(); var frames = new List<object>(); int failures = 0, updates = 0, languageIndex = 0, step = 0;
        var languages = new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese };
        object fixture = null; ResearchFlowView view = null; ResearchSessionFlowController controller = null;
        Camera camera = null; UnityEngine.UI.ScrollRect scroll = null; TMP_Text body = null; ResearchSlateFocus focus = null;
        Component module = null; PointerEventData pointer = null; EventSystem ownedEvents = null;
        ResearchUIAction pageAction = null; GameObject pressOwner = null; bool thumb = false, dragging = false;
        string stage = "initializing", error = null; int pressedPage = 0;
        float savedNormalized = 0; Vector3 headBase = default, panelBase = default; Quaternion headFacing = default, panelFacing = default;
        double start = EditorApplication.timeSinceStartup, lastTick = start, lastStep = start, lastCapture = start - 1, dragStart = 0;
        EditorApplication.CallbackFunction tick = null;
        void Check(bool pass, string name, object observed) { rows.Add(new { name, pass, observed, editor_time = EditorApplication.timeSinceStartup }); if (!pass) failures++; }
        int Page() => (int)typeof(ResearchFlowView).GetProperty("ReferencePageIndex", hidden).GetValue(view);
        void Set(string name, object value) => typeof(ResearchSessionFlowController).GetProperty(name).GetSetMethod(true).Invoke(controller, new[] { value });
        void Flush() { lateScroll.Invoke(scroll, null); Canvas.ForceUpdateCanvases(); body.ForceMeshUpdate(true, true); }
        void Click(string command)
        {
            var action = view.GetComponentsInChildren<ResearchUIAction>(true).First(x => x.command == command && x.gameObject.activeInHierarchy);
            if (typeof(ResearchUIAction).GetField("button", hidden).GetValue(action) == null) typeof(ResearchUIAction).GetMethod("OnEnable", hidden).Invoke(action, null);
            var data = new PointerEventData(ownedEvents) { pointerId = 953, button = PointerEventData.InputButton.Left };
            var owner = ExecuteEvents.ExecuteHierarchy(action.gameObject, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(owner, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(owner, data, ExecuteEvents.pointerClickHandler);
            Flush();
        }
        void Setup()
        {
            var language = languages[languageIndex];
            fixture = Activator.CreateInstance(fixtureType, new object[] { Path.Combine(directory, language.ToString()), donor, language });
            controller = (ResearchSessionFlowController)fixtureType.GetField("Controller").GetValue(fixture);
            view = (ResearchFlowView)fixtureType.GetField("View").GetValue(fixture);
            camera = (Camera)fixtureType.GetField("Camera").GetValue(fixture);
            Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
            Set("ReferenceOpen", true); Set("ReferenceMinimized", false); Set("ReferenceSection", "user_manual");
            fixtureType.GetMethod("Refresh").Invoke(fixture, null);
            scroll = (UnityEngine.UI.ScrollRect)typeof(ResearchFlowView).GetField("scroll", hidden).GetValue(view);
            body = (TMP_Text)typeof(ResearchFlowView).GetField("referenceBody", hidden).GetValue(view);
            focus = view.referencePanel.GetComponent<ResearchSlateFocus>();
            camera.transform.SetParent(null, true); camera.scene = view.gameObject.scene;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(view.gameObject.scene);
            camera.aspect = 1; camera.fieldOfView = 90;
            view.referencePanel.GetComponent<Canvas>().worldCamera = camera;
            var go = new GameObject("Owned_LongReader_Module") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false); SceneManager.MoveGameObjectToScene(go, view.gameObject.scene);
            go.transform.SetParent(view.transform, true);
            ownedEvents = go.AddComponent<EventSystem>(); module = go.AddComponent(moduleType);
            typeof(BaseInputModule).GetField("m_EventSystem", hidden).SetValue(module, ownedEvents);
            pointer = new PointerEventData(ownedEvents) { pointerId = 952, button = PointerEventData.InputButton.Left, useDragThreshold = false, eligibleForClick = true };
            headBase = camera.transform.position; headFacing = camera.transform.rotation;
            panelBase = view.referencePanel.transform.position; panelFacing = view.referencePanel.transform.rotation;
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Flush();
            Check(body.textInfo.pageCount >= 2 && scroll.content.rect.height > scroll.viewport.rect.height + 1, language + "/real long document", new { pages = body.textInfo.pageCount, content = scroll.content.rect.height, viewport = scroll.viewport.rect.height, demo_content_version = ResearchDemoManual.ContentVersion, study_approved_content = false });
            if (body.textInfo.pageCount < 2 || scroll.content.rect.height <= scroll.viewport.rect.height + 1) throw new InvalidOperationException("No actual long-document range.");
            stage = language + "/first_page"; step = 0; lastStep = EditorApplication.timeSinceStartup;
        }
        void DisposeFixture() { if (fixture != null) ((IDisposable)fixture).Dispose(); fixture = null; }
        void Finish()
        {
            EditorApplication.update -= tick;
            try { DisposeFixture(); } catch (Exception ex) { error = (error ?? "") + ex; failures++; }
            foreach (var color in colors) if (color.cursor) hoverColor.SetValue(color.cursor, color.color);
            Check(SceneManager.GetActiveScene() == caller && caller.isDirty == dirty && EditorSceneManager.previewSceneCount == previews && EventSystem.current == currentEvents && !EditorApplication.isPlaying,
                "canonical scene, dirty flag, preview count, EventSystem and stopped mode preserved", new { caller.path, dirty, current_dirty = caller.isDirty, previews, current_previews = EditorSceneManager.previewSceneCount, editor_is_playing = EditorApplication.isPlaying });
            string summary = "LONG_READER_EDITOR_TEMPORAL_" + (failures == 0 ? "PASS" : "FAIL");
            File.WriteAllText(Path.Combine(directory, "timeline.json"), (string)serializer.Invoke(null, new object[] { new { summary, failures, error, updates, rows, frames, hardware_verified = false,
                scope = "Continuous real EditorApplication.update and fixed-FOV Camera.Render in isolated preview scenes, stopped EditMode. TH/JP existing fictitious-demo manual and synthetic head/controller consumer/pointer inputs. ProcessDrag, ScrollRect.LateUpdate, ResearchUIAction acquisition/click and LocalCommand execute production code. Game clocks recorded but are not asserted to advance. No automatic Play lifecycle, native SDK ray acquisition, Quest/two-eye, approved content, study outcome or wearer claim." } }));
            Debug.Log(summary + "; " + Path.Combine(directory, "timeline.json"));
        }
        try { Setup(); }
        catch (Exception ex) { error = ex.ToString(); Check(false, "initialization", error); Finish(); throw; }
        tick = () =>
        {
            try
            {
                if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed || !donor) throw new InvalidOperationException("Bound stopped editor/fixture condition changed.");
                double now = EditorApplication.timeSinceStartup, delta = now - lastTick; lastTick = now; updates++;
                double elapsed = now - start; double interval = now - lastStep;
                camera.transform.SetPositionAndRotation(headBase + new Vector3(.025f * Mathf.Sin((float)elapsed * 3), .01f * Mathf.Sin((float)elapsed * 2), 0), headFacing * Quaternion.Euler(0, 2.5f * Mathf.Sin((float)elapsed * 2), 0));
                if (thumb) focus.Scroll(-1, (float)delta);
                if (dragging)
                {
                    var before = pointer.position; pointer.position = pointer.pressPosition + Vector2.up * (float)(now - dragStart) * 280;
                    pointer.delta = pointer.position - before; processDrag.Invoke(module, new object[] { pointer });
                }
                Flush();
                bool advanced = interval >= .55;
                if (advanced)
                {
                    lastStep = now; step++;
                    switch (step)
                    {
                        case 1:
                            stage = languages[languageIndex] + "/page_press_then_continuous_thumb_scroll";
                            pageAction = view.GetComponentsInChildren<ResearchUIAction>(true).First(x => x.command == "reference_next" && x.gameObject.activeInHierarchy);
                            if (typeof(ResearchUIAction).GetField("button", hidden).GetValue(pageAction) == null) typeof(ResearchUIAction).GetMethod("OnEnable", hidden).Invoke(pageAction, null);
                            pressedPage = Page(); pressOwner = ExecuteEvents.ExecuteHierarchy(pageAction.GetComponentInChildren<TMP_Text>().gameObject, pointer, ExecuteEvents.pointerDownHandler);
                            Check(pressOwner == pageAction.gameObject && pageAction.PointerHeld && pageAction.GetComponent<Button>().IsInteractable(), "page button acquisition is owned and enabled", new { page = pressedPage, acquired = pageAction.PointerHeld, owner = pressOwner ? pressOwner.name : null });
                            focus.OnPointerEnter(pointer); thumb = true; break;
                        case 2:
                            stage = languages[languageIndex] + "/stale_page_release"; thumb = false; int beforeRelease = Page();
                            Check(beforeRelease > pressedPage && beforeRelease < body.textInfo.pageCount && pageAction.PointerHeld && pageAction.GetComponent<Button>().IsInteractable(), "stale release is not masked by last-page disabling", new { pressedPage, beforeRelease, pages = body.textInfo.pageCount, acquired = pageAction.PointerHeld, interactable = pageAction.GetComponent<Button>().IsInteractable() });
                            ExecuteEvents.Execute(pressOwner, pointer, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(pressOwner, pointer, ExecuteEvents.pointerClickHandler); Flush();
                            string loggedEvents;
                            using (var reader = new StreamReader(new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))) loggedEvents = reader.ReadToEnd();
                            var rejection = loggedEvents.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).Select(Newtonsoft.Json.Linq.JObject.Parse).LastOrDefault(e => (string)e["event"] == "UI_CLICK_REJECTED" && (string)e["target"] == "reference_next");
                            Check(Page() == beforeRelease && !pageAction.PointerHeld && rejection != null && (string)rejection["result"] == "StaleAcquisition" && (string)rejection["reason"] == "context_changed", "stale page acquisition rejected with actual context-change event", new { pressedPage, beforeRelease, after = Page(), rejection });
                            Click("reference_next"); Check(Page() == beforeRelease + 1, "fresh next acquisition remains usable after rejection", new { beforeRelease, after = Page() });
                            Click("reference_previous"); Check(Page() == beforeRelease, "fresh previous acquisition returns to scrolled page", Page()); break;
                        case 3:
                            stage = languages[languageIndex] + "/owned_process_drag_with_head_motion";
                            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Flush();
                            pointer.position = camera.WorldToScreenPoint(scroll.viewport.position); pointer.pressPosition = pointer.position;
                            pointer.pointerPressRaycast = new RaycastResult { gameObject = body.gameObject, module = view.referencePanel.GetComponent<UnityEngine.UI.GraphicRaycaster>() };
                            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(body.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                            pointer.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(body.gameObject);
                            ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.initializePotentialDrag);
                            dragStart = now; dragging = true; break;
                        case 4:
                            stage = languages[languageIndex] + "/owned_drag_release"; dragging = false;
                            Check(pointer.dragging && pointer.pointerPress == pointer.pointerDrag && scroll.verticalNormalizedPosition < .99f && Page() == body.pageToDisplay,
                                "ProcessDrag moves real long content with stable input owner", new { pointer.dragging, press_owner = pointer.pointerPress ? pointer.pointerPress.name : null, drag_owner = pointer.pointerDrag ? pointer.pointerDrag.name : null, normalized = scroll.verticalNormalizedPosition, page = Page(), rendered_page = body.pageToDisplay });
                            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
                            pointer.dragging = false; pointer.pointerPress = null; pointer.pointerDrag = null; scroll.StopMovement(); focus.OnPointerExit(pointer); savedNormalized = scroll.verticalNormalizedPosition; break;
                        case 5: stage = languages[languageIndex] + "/user_format_steps"; Click("reference_tab.step_by_step"); Check(controller.ReferenceSection == "step_by_step", "explicit user format selection", controller.ReferenceSection); break;
                        case 6: stage = languages[languageIndex] + "/user_format_manual_restored"; Click("reference_tab.user_manual"); Check(Mathf.Abs(scroll.verticalNormalizedPosition - savedNormalized) < .001f, "manual position restored after explicit format return", new { savedNormalized, after = scroll.verticalNormalizedPosition }); break;
                        case 7: stage = languages[languageIndex] + "/user_minimize"; Click("reference_minimize"); Check(controller.ReferenceMinimized, "explicit minimize", controller.ReferenceMinimized); break;
                        case 8: stage = languages[languageIndex] + "/user_restore"; Click("reference_restore"); Check(!controller.ReferenceMinimized && Mathf.Abs(scroll.verticalNormalizedPosition - savedNormalized) < .001f, "restore retains manual progress", new { controller.ReferenceMinimized, normalized = scroll.verticalNormalizedPosition }); break;
                        case 9:
                            stage = languages[languageIndex] + "/user_last_page";
                            for (int n = 0; n < 32 && Page() < body.textInfo.pageCount; n++) Click("reference_next");
                            Check(Page() == body.textInfo.pageCount && body.pageToDisplay == Page(), "last real TMP page reached by explicit page control", new { page = Page(), rendered_page = body.pageToDisplay, pages = body.textInfo.pageCount }); break;
                        case 10:
                            Check(controller.Flow.State == ResearchFlowState.Task1Active && controller.Record == null && !controller.Flow.Paused && string.IsNullOrEmpty(controller.Error), "no real task/session evaluation", new { state = controller.Flow.State.ToString(), record = controller.Record, controller.Error });
                            DisposeFixture(); languageIndex++;
                            if (languageIndex == languages.Length) { Finish(); return; }
                            Setup(); break;
                    }
                }
                Check(Vector3.Distance(panelBase, view.referencePanel.transform.position) < .00001f && Quaternion.Angle(panelFacing, view.referencePanel.transform.rotation) < .001f,
                    "reference pose remains unchanged during head and reader input", view.referencePanel.Pose());
                if (advanced || now - lastCapture >= .10)
                {
                    lastCapture = now; string path = Path.Combine(directory, "frame-" + frames.Count.ToString("D4") + ".png");
                    capture.Invoke(null, new object[] { camera, path });
                    var surfaceFocus = pointer.pointerPress ? pointer.pointerPress.GetComponent<ResearchSlateFocus>() : null;
                    frames.Add(new { stage, path, editor_time = now, elapsed_editor_seconds = elapsed, frame = Time.frameCount, unscaled_time = Time.unscaledTime, language = languages[languageIndex].ToString(), page = Page(), rendered_page = body.pageToDisplay, pages = body.textInfo.pageCount, normalized = scroll.verticalNormalizedPosition, content_height = scroll.content.rect.height, viewport_height = scroll.viewport.rect.height, section = controller.ReferenceSection, controller.ReferenceMinimized, head_position = new[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z }, head_rotation = new[] { camera.transform.rotation.x, camera.transform.rotation.y, camera.transform.rotation.z, camera.transform.rotation.w }, fov = camera.fieldOfView, aspect = camera.aspect, reference_pose = view.referencePanel.Pose(), reference_readable = view.referencePanel.IsReadableFromHead, recovery = view.RecoveryCanvas.gameObject.activeSelf, held = (surfaceFocus && surfaceFocus.PointerHeld) || (pageAction && pageAction.PointerHeld), reader_surface_held = surfaceFocus && surfaceFocus.PointerHeld, slate_root_held = focus.PointerHeld, pointer.dragging, press_owner = pointer.pointerPress ? pointer.pointerPress.name : null, drag_owner = pointer.pointerDrag ? pointer.pointerDrag.name : null, page_action_held = pageAction && pageAction.PointerHeld });
                }
                if (now - start > 90) throw new TimeoutException("Focused editor capture exceeded bounded duration.");
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception ex) { error = ex.ToString(); Check(false, "execution", error); Finish(); }
        };
        EditorApplication.update += tick; EditorApplication.QueuePlayerLoopUpdate();
        return "LONG_READER_EDITOR_TEMPORAL_STARTED; " + directory;
    }
    }
}
#endif
