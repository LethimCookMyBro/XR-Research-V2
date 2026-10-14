#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // TEST-only stopped-Editor layout/callback captures; the native decoder gate runs separately.
    public static class ResearchReaderRefinementChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] Formats = { "user_manual", "step_by_step", "illustrated", "video" };
        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Hidden).GetValue(value);
        static T Public<T>(object value, string name) => (T)value.GetType().GetField(name).GetValue(value);
        static object PrivateCall(object value, string name, params object[] args) => value.GetType().GetMethod(name, Hidden).Invoke(value, args);
        static void Set(object value, string property, object next) => value.GetType().GetProperty(property).GetSetMethod(true).Invoke(value, new[] { next });
        static float[] RectValues(RectTransform rect) => new[] { rect.anchoredPosition.x, rect.anchoredPosition.y, rect.rect.width, rect.rect.height };
        static bool Same(float[] a, float[] b) => a.Length == b.Length && a.Zip(b, (x, y) => Mathf.Abs(x - y)).All(d => d < .01f);
        static bool Size(RectTransform rect, float width, float height) => Mathf.Abs(rect.rect.width - width) < .1f && Mathf.Abs(rect.rect.height - height) < .1f;
        static Rect BoundsIn(RectTransform rect, RectTransform owner)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Select(owner.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
        }
        static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1 && inner.xMax <= outer.xMax + 1 && inner.yMin >= outer.yMin - 1 && inner.yMax <= outer.yMax + 1;
        static bool EffectiveRaycast(Graphic graphic) => graphic && graphic.gameObject.activeInHierarchy && graphic.enabled && graphic.raycastTarget && graphic.GetComponentsInParent<CanvasGroup>().All(group => group.blocksRaycasts);
        static bool Retired(IEnumerable<Graphic> graphics) => graphics.All(graphic => !EffectiveRaycast(graphic) && (!graphic || !graphic.gameObject.activeInHierarchy));
        static List<RaycastResult> Hits(ResearchFlowView view, Camera camera, RectTransform target, Vector2 local, PointerEventData pointer)
        {
            Flush(view);
            pointer.Reset(); pointer.button = PointerEventData.InputButton.Left;
            pointer.position = camera.WorldToScreenPoint(target.TransformPoint(new Vector3(local.x, local.y, 0)));
            var hits = new List<RaycastResult>();
            view.referencePanel.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            return hits;
        }
        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        static string ResourcePath(string resource)
        {
            var asset = Resources.Load<TextAsset>(resource);
            if (!asset) throw new FileNotFoundException("Required original byte resource", resource);
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", AssetDatabase.GetAssetPath(asset)));
        }
        static string[] SceneStates() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
            var scene = SceneManager.GetSceneAt(i); return scene.handle + ":" + scene.path + ":" + scene.isDirty;
        }).ToArray();
        static List<JObject> Events(ResearchSessionFlowController controller)
        {
            using (var reader = new StreamReader(new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                return reader.ReadToEnd().Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).Select(JObject.Parse).ToList();
        }
        static void Flush(ResearchFlowView view)
        {
            Canvas.ForceUpdateCanvases();
            if (view.ReferenceViewer) PrivateCall(view.ReferenceViewer, "Update");
            typeof(ScrollRect).GetMethod("LateUpdate", Hidden).Invoke(Field<ScrollRect>(view, "scroll"), null);
            Canvas.ForceUpdateCanvases();
            foreach (var text in view.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
        }
        static void Click(ResearchFlowView view, string command, PointerEventData pointer)
        {
            var action = view.GetComponentsInChildren<ResearchUIAction>(true).Single(a => a.command == command && a.gameObject.activeInHierarchy && a.GetComponent<Button>().IsInteractable());
            if (Field<Button>(action, "button") == null) PrivateCall(action, "OnEnable");
            pointer.Reset(); pointer.button = PointerEventData.InputButton.Left; pointer.eligibleForClick = true;
            var owner = ExecuteEvents.ExecuteHierarchy(action.GetComponentInChildren<TMP_Text>(true).gameObject, pointer, ExecuteEvents.pointerDownHandler);
            if (!owner) throw new InvalidOperationException("No pointer-down owner: " + command);
            ExecuteEvents.Execute(owner, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(owner, pointer, ExecuteEvents.pointerClickHandler); Flush(view);
        }
        static float[] OuterFrame(ResearchFlowView view)
        {
            var expanded = view.referencePanel.transform.Find("Expanded");
            var values = new List<float>();
            foreach (var rect in new[] { view.referencePanel.GetComponent<RectTransform>(), (RectTransform)expanded,
                expanded.Find("Surface") as RectTransform, Field<ScrollRect>(view, "scroll").GetComponent<RectTransform>(), Field<TMP_Text>(view, "referenceTitle").rectTransform })
                values.AddRange(RectValues(rect));
            foreach (var action in view.referencePanel.GetComponentsInChildren<ResearchUIAction>(true)
                .Where(a => a.command.StartsWith("reference_tab.") || a.command == "reference_minimize" || a.command == "reference_close" || a.command == "pause").OrderBy(a => a.command))
                values.AddRange(RectValues(action.GetComponent<RectTransform>()));
            var grip = view.referencePanel.GetComponentsInChildren<ResearchDragHandle>(true).Single(handle => handle.panel == view.referencePanel);
            values.AddRange(RectValues(grip.GetComponent<RectTransform>()));
            return values.ToArray();
        }
        static void FixturePackage(ResearchFlowView view, ResearchReferenceViewer.Package package)
        {
            var reader = view.ReferenceViewer;
            PrivateCall(reader, "CancelVideo"); PrivateCall(reader, "ClearContent");
            reader.GetType().GetField("package", Hidden).SetValue(reader, package);
            reader.GetType().GetField("index", Hidden).SetValue(reader, 1);
            PrivateCall(reader, "Render"); view.RefreshPageControls(); Flush(view);
            Field<ScrollRect>(view, "scroll").verticalNormalizedPosition = 1; Flush(view);
        }
        static string TaskSnapshot(ResearchSessionFlowController controller) => JsonConvert.SerializeObject(new {
            controller.Flow.State, controller.Flow.CurrentTask, controller.Flow.InspectPassed, controller.Flow.TutorialCompleted,
            controller.Flow.Paused, controller.Record, controller.TutorialCompletedFactCount,
            credits = Field<HashSet<string>>(controller, "tutorialEvidence").OrderBy(x => x).ToArray(),
            ports = controller.taskPorts.Select(port => new { port.taskId, port.demoPassed }).ToArray()
        });

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor/no XR required; this is not native decoder evidence.");
            if (string.IsNullOrWhiteSpace(evidenceDirectory)) throw new ArgumentException("An explicit fresh TEST evidence directory is required.");
            string directory = Path.GetFullPath(evidenceDirectory), path = Path.Combine(directory, "reader-refinement.json");
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Evidence collision: " + directory);
            Directory.CreateDirectory(directory);
            var rows = new List<object>(); var captures = new List<JObject>(); int failures = 0;
            void Check(bool pass, string name, object observed = null) { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            var beforeScenes = SceneStates(); var priorEvents = EventSystem.current; int previews = EditorSceneManager.previewSceneCount;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(cursor => cursor, cursor => cursor.HoverColor);
            var donorArgs = new object[] { null };
            ResearchFlowView donor = null; GameObject ownedDonor = null;
            var fixtureType = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            var registry = Resources.Load<TextAsset>("ResearchReaderDemo/package");
            if (!registry) throw new FileNotFoundException("Existing TEST registry missing.");
            string originalRegistry = registry.text;
            var original = JsonUtility.FromJson<ResearchReferenceViewer.Package>(originalRegistry);
            var originalPaths = new[] { ResourcePath(original.source_pdf_resource), ResourcePath("ResearchReaderDemo/package") }
                .Concat(original.pages.Select(page => ResourcePath(page.resource))).Distinct().ToArray();
            var assetHashes = originalPaths.ToDictionary(asset => asset, Hash);
            string code = Path.Combine(Application.dataPath, "VRMaintenanceResearch", "ResearchFlow");
            var preservedSource = new[] { "Core/ResearchSessionFlow.cs", "Runtime/ResearchSessionFlowController.cs", "Runtime/ResearchTaskPort.cs", "Runtime/ResearchSessionExporter.cs" }
                .Select(relative => Path.Combine(code, relative)).Where(File.Exists).ToDictionary(file => Path.GetFullPath(file), Hash);
            try
            {
                donor = (ResearchFlowView)typeof(ResearchReaderWireframeChecks).GetMethod("FontDonor", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, donorArgs);
                ownedDonor = donorArgs[0] as GameObject;
                var displayTime = typeof(ResearchReferenceViewer).GetMethod("DisplayTime", BindingFlags.Static | BindingFlags.NonPublic);
                if (displayTime == null) throw new MissingMethodException(typeof(ResearchReferenceViewer).FullName, "DisplayTime");
                foreach (var sample in new[] {
                    new { seconds = -1d, expected = "--:--" }, new { seconds = double.NaN, expected = "--:--" },
                    new { seconds = double.PositiveInfinity, expected = "--:--" }, new { seconds = double.NegativeInfinity, expected = "--:--" },
                    new { seconds = 0d, expected = "0:00" }, new { seconds = 8d, expected = "0:08" }, new { seconds = 3608d, expected = "60:08" }
                })
                {
                    string actual = (string)displayTime.Invoke(null, new object[] { sample.seconds });
                    Check(actual == sample.expected, "actual DisplayTime boundary " + sample.seconds.ToString("R", CultureInfo.InvariantCulture),
                        new { input = sample.seconds.ToString("R", CultureInfo.InvariantCulture), sample.expected, actual, native_player_fabricated = false });
                }
                Check(Hash(ResourcePath(original.source_pdf_resource)) == original.source_sha256 && original.pages.All(page => Hash(ResourcePath(page.resource)) == page.sha256),
                    "original PDF/all mapped page bytes match stored provenance", new { original.source_pdf, original.source_sha256, pages = original.pages.Length, assetHashes });
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
                {
                    object fixture = null;
                    try
                    {
                        fixture = Activator.CreateInstance(fixtureType, new object[] { Path.Combine(directory, language.ToString()), donor, language });
                        var controller = Public<ResearchSessionFlowController>(fixture, "Controller");
                        var view = Public<ResearchFlowView>(fixture, "View"); var camera = Public<Camera>(fixture, "Camera");
                        Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                        Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "user_manual");
                        fixtureType.GetMethod("Refresh").Invoke(fixture, null); Flush(view);
                        view.referencePanel.GetComponent<Canvas>().worldCamera = camera;
                        var eventObject = new GameObject("ReaderRefinementTESTEvents") { hideFlags = HideFlags.HideAndDontSave };
                        eventObject.SetActive(false); SceneManager.MoveGameObjectToScene(eventObject, view.gameObject.scene); eventObject.transform.SetParent(view.transform, false);
                        var events = eventObject.AddComponent<EventSystem>();
                        var pointer = new PointerEventData(events) { pointerId = -73711, button = PointerEventData.InputButton.Left, useDragThreshold = false };
                        var reader = view.ReferenceViewer; var scroll = Field<ScrollRect>(view, "scroll");
                        var label = Field<TMP_Text>(view, "pageLabel"); var previous = Field<Button>(view, "previousPage"); var next = Field<Button>(view, "nextPage");
                        var frame = OuterFrame(view); var position = view.referencePanel.transform.position; var rotation = view.referencePanel.transform.rotation;
                        string task = TaskSnapshot(controller); int firstEvent = Events(controller).Count;
                        void C(bool pass, string name, object observed = null) => Check(pass, language + "/" + name, observed);
                        void Stable(string stage)
                        {
                            C(Same(frame, OuterFrame(view)) && Vector3.Distance(position, view.referencePanel.transform.position) < .001f
                                && Quaternion.Angle(rotation, view.referencePanel.transform.rotation) < .01f,
                                stage + ": fixed outer shell/header/sidebar/grip/world pose", new { before = frame, after = OuterFrame(view) });
                            C(Size(scroll.GetComponent<RectTransform>(), 840, 548), stage + ": expanded outer viewer", RectValues(scroll.GetComponent<RectTransform>()));
                            bool video = controller.ReferenceSection == "video";
                            C(Size(scroll.viewport, 810, video ? 548 : 500) && Vector2.Distance(scroll.viewport.anchoredPosition, new Vector2(-12, video ? 0 : 24)) < .1f,
                                stage + ": intentionally media-specific inner viewport", RectValues(scroll.viewport));
                            var footer = view.referencePanel.transform.Find("Expanded/ReaderFooter");
                            C(!footer || !footer.gameObject.activeInHierarchy, stage + ": redundant ReaderFooter inactive", footer ? (object)footer.gameObject.activeInHierarchy : "absent");
                        }
                        void Capture(string stage)
                        {
                            Flush(view);
                            var referenceCanvas = view.referencePanel.GetComponent<Canvas>();
                            var otherCanvases = view.GetComponentsInChildren<Canvas>(true)
                                .Where(canvas => canvas != referenceCanvas && !canvas.transform.IsChildOf(view.referencePanel.transform))
                                .ToDictionary(canvas => canvas, canvas => canvas.enabled);
                            JObject capture = null;
                            try
                            {
                                foreach (var canvas in otherCanvases.Keys) canvas.enabled = false;
                                capture = JObject.FromObject(fixtureType.GetMethod("Capture").Invoke(fixture, new object[] { stage, view.referencePanel }));
                            }
                            finally { foreach (var pair in otherCanvases) if (pair.Key) pair.Key.enabled = pair.Value; }
                            bool restored = otherCanvases.All(pair => pair.Key && pair.Key.enabled == pair.Value);
                            C(restored, stage + ": screenshot-only owned Canvas suppression restored", otherCanvases.Select(pair => new { canvas = pair.Key.name, was_enabled = pair.Value, now_enabled = pair.Key.enabled }).ToArray());
                            capture["screenshot_only_other_owned_canvases_suppressed"] = JArray.FromObject(otherCanvases.Keys.Select(canvas => canvas.name));
                            capture["other_owned_canvas_enabled_restored"] = restored;
                            capture["scope"] = (string)capture["scope"] + " Other owned canvases suppressed for screenshot only; behavior tests unchanged. No camera/panel move added.";
                            captures.Add(capture);
                        }
                        void OutsideVisualHit(string name, RectTransform carrier, RectTransform visible, Vector2 local)
                        {
                            var hits = Hits(view, camera, carrier, local, pointer);
                            var world = carrier.TransformPoint(new Vector3(local.x, local.y, 0));
                            bool outside = visible && !BoundsIn(visible, carrier).Contains(local);
                            C(carrier.rect.Contains(local) && outside && hits.Any(hit => hit.gameObject == carrier.gameObject),
                                name + ": actual GraphicRaycaster hits transparent carrier outside visible artwork",
                                new { local = new[] { local.x, local.y }, carrier = RectValues(carrier), visible = visible ? RectValues(visible) : null,
                                    screen = new[] { pointer.position.x, pointer.position.y }, camera_world = new[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z },
                                    camera_fov = camera.fieldOfView, point_depth = camera.WorldToScreenPoint(world).z,
                                    actual_hits = hits.Select(hit => new { target = hit.gameObject.name, module = hit.module.GetType().Name, hit.distance }).ToArray() });
                        }
                        void Pager(string stage, bool visible, string unit = null)
                        {
                            C(label.gameObject.activeInHierarchy == visible && previous.gameObject.activeInHierarchy == visible && next.gameObject.activeInHierarchy == visible,
                                stage + ": navigation only when useful", new { count = reader.PageCount, text = label.text, label_visible = label.gameObject.activeInHierarchy, previous_visible = previous.gameObject.activeInHierarchy, next_visible = next.gameObject.activeInHierarchy });
                            if (!visible) C(!EffectiveRaycast(previous.GetComponent<Graphic>()) && !EffectiveRaycast(next.GetComponent<Graphic>()), stage + ": hidden pager cannot intercept another format");
                            else
                            {
                                int[] numbers = Regex.Matches(label.GetParsedText(), @"\d+").Cast<Match>().Select(match => int.Parse(match.Value)).ToArray();
                                C(numbers.SequenceEqual(new[] { reader.PageIndex, reader.PageCount }) && (unit == null || label.GetParsedText().Contains(unit)),
                                    stage + ": localized item unit/actual index/count", label.GetParsedText());
                                C(Size(previous.GetComponent<RectTransform>(), 64, 40) && Size(next.GetComponent<RectTransform>(), 64, 40), stage + ": compact arrow hit areas");
                            }
                        }
                        string stepUnit = language == ParticipantLanguage.Thai ? "ขั้น" : language == ParticipantLanguage.Japanese ? "手順" : "Step";
                        string imageUnit = language == ParticipantLanguage.Thai ? "ภาพ" : language == ParticipantLanguage.Japanese ? "画像" : "Image";
                        C(reader && reader.IsReady && reader.PageCount == original.pages.Length && controller.Record == null && controller.IsTechnical,
                            "existing bytes render in TEST fixture without participant/consent", reader?.Metadata);
                        Stable("PDF"); Pager("PDF", true);
                        var pdfIndices = new List<int>();
                        foreach (var sample in new[] { new { name = "top", offset = 1f }, new { name = "mid", offset = .5f }, new { name = "end", offset = 0f } })
                        {
                            scroll.StopMovement(); scroll.verticalNormalizedPosition = sample.offset; Flush(view); pdfIndices.Add(reader.PageIndex);
                            C(reader.IsReady && scroll.content.rect.height > scroll.viewport.rect.height && reader.CachedPageCount <= 3, "PDF " + sample.name + ": continuous bounded-cache original pages", reader.Metadata);
                            Capture("PDF_" + sample.name);
                        }
                        C(pdfIndices[0] == 1 && pdfIndices[1] > 1 && pdfIndices[1] < original.pages.Length && pdfIndices[2] == original.pages.Length,
                            "PDF top/middle/end follows visible page boundary", pdfIndices);
                        scroll.verticalNormalizedPosition = .63f; Flush(view); float savedPdf = scroll.verticalNormalizedPosition;
                        Click(view, "reference_minimize", pointer); Capture("Minimized"); Click(view, "reference_restore", pointer);
                        C(Mathf.Abs(scroll.verticalNormalizedPosition - savedPdf) < .001f, "PDF minimize/restore retains document offset", new { savedPdf, restored = scroll.verticalNormalizedPosition });
                        Stable("Restored"); Capture("Restored");
                        var restoredBar = reader.GuardedScrollbar;
                        var barContainer = restoredBar.handleRect.parent as RectTransform;
                        float normalizedBeforeDrag = scroll.verticalNormalizedPosition, valueBeforeDrag = restoredBar.value;
                        float thumbBeforeDrag = BoundsIn(restoredBar.handleRect, barContainer).center.y;
                        var contentBeforeDrag = scroll.content.anchoredPosition;
                        var barHits = Hits(view, camera, restoredBar.handleRect, Vector2.zero, pointer);
                        var barHit = barHits.FirstOrDefault(hit => hit.gameObject == restoredBar.gameObject || hit.gameObject.transform.IsChildOf(restoredBar.transform));
                        GameObject pressOwner = null, dragOwner = null;
                        try
                        {
                            C(barHit.gameObject, "restored PDF scrollbar has an actual GraphicRaycaster hit", barHits.Select(hit => hit.gameObject.name).ToArray());
                            if (barHit.gameObject)
                            {
                                pointer.pressPosition = pointer.position; pointer.pointerPressRaycast = barHit; pointer.eligibleForClick = false;
                                pressOwner = ExecuteEvents.ExecuteHierarchy(barHit.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                                dragOwner = ExecuteEvents.GetEventHandler<IDragHandler>(barHit.gameObject);
                                pointer.pointerPress = pressOwner; pointer.pointerDrag = dragOwner;
                                ExecuteEvents.Execute(dragOwner, pointer, ExecuteEvents.initializePotentialDrag);
                                ExecuteEvents.Execute(dragOwner, pointer, ExecuteEvents.beginDragHandler);
                                pointer.dragging = true;
                                var target = barContainer.TransformPoint(new Vector3(barContainer.rect.center.x, Mathf.Lerp(barContainer.rect.yMin, barContainer.rect.yMax, .25f), 0));
                                Vector2 nextPosition = camera.WorldToScreenPoint(target); pointer.delta = nextPosition - pointer.position; pointer.position = nextPosition;
                                ExecuteEvents.Execute(dragOwner, pointer, ExecuteEvents.dragHandler); Flush(view);
                                C(reader.ScrollbarHeld && Mathf.Abs(restoredBar.value - valueBeforeDrag) > .01f
                                    && Mathf.Abs(BoundsIn(restoredBar.handleRect, barContainer).center.y - thumbBeforeDrag) > 1
                                    && Mathf.Abs(scroll.verticalNormalizedPosition - normalizedBeforeDrag) > .01f
                                    && Vector2.Distance(contentBeforeDrag, scroll.content.anchoredPosition) > 1
                                    && Vector3.Distance(position, view.referencePanel.transform.position) < .001f,
                                    "restored guarded scrollbar drag moves thumb AND ScrollRect/content, not just visual handle",
                                    new { reader.ScrollbarHeld, before_value = valueBeforeDrag, after_value = restoredBar.value,
                                        before_normalized = normalizedBeforeDrag, after_normalized = scroll.verticalNormalizedPosition,
                                        before_thumb_center = thumbBeforeDrag, after_thumb_center = BoundsIn(restoredBar.handleRect, barContainer).center.y,
                                        before_content = new[] { contentBeforeDrag.x, contentBeforeDrag.y },
                                        after_content = new[] { scroll.content.anchoredPosition.x, scroll.content.anchoredPosition.y } });
                            }
                        }
                        finally
                        {
                            if (pressOwner) ExecuteEvents.Execute(pressOwner, pointer, ExecuteEvents.pointerUpHandler);
                            if (dragOwner) ExecuteEvents.Execute(dragOwner, pointer, ExecuteEvents.endDragHandler);
                            pointer.dragging = false; pointer.pointerPress = null; pointer.pointerDrag = null; scroll.StopMovement(); Flush(view);
                        }
                        C(!reader.ScrollbarHeld, "restored guarded scrollbar releases its pointer owner", reader.Metadata);
                        Stable("PDF after restored scrollbar drag"); Capture("PDF_after_restored_scrollbar_drag");
                        foreach (string from in Formats)
                        {
                            if (controller.ReferenceSection != from) Click(view, "reference_tab." + from, pointer);
                            foreach (string to in Formats.Where(format => format != from))
                            {
                                var oldGraphics = scroll.content.GetComponentsInChildren<Graphic>(true).Where(graphic => graphic.gameObject != Field<TMP_Text>(view, "referenceBody").gameObject).ToArray();
                                Click(view, "reference_tab." + to, pointer);
                                C(controller.ReferenceSection == to && Retired(oldGraphics), from + " -> " + to + ": stale content/control visuals and raycasts retired",
                                    new { controller.ReferenceSection, stale_active = oldGraphics.Count(graphic => graphic && graphic.gameObject.activeInHierarchy), stale_raycasts = oldGraphics.Count(EffectiveRaycast) });
                                Stable(from + " -> " + to);
                                Pager(to, to != "video", to == "step_by_step" ? stepUnit : to == "illustrated" ? imageUnit : null);
                                if (to == "video") C(reader.Status == "Loading" && !reader.IsReady && !reader.NativeVideoPlayer && reader.DecodedFrameCount == 0,
                                    "stopped-Editor video remains honestly Loading, not claimed decoded", reader.Metadata);
                                Click(view, "reference_tab." + from, pointer);
                            }
                        }
                        foreach (string format in new[] { "step_by_step", "illustrated" })
                        {
                            Click(view, "reference_tab." + format, pointer); Stable(format); Capture(format == "step_by_step" ? "Steps" : "Images");
                            Pager(format, true, format == "step_by_step" ? stepUnit : imageUnit);
                            int index = reader.PageIndex; Click(view, "reference_next", pointer);
                            C(reader.PageIndex == index + 1, format + ": next changes only current media item", reader.Metadata);
                            Click(view, "reference_previous", pointer); C(reader.PageIndex == index, format + ": previous returns to original item", reader.Metadata);
                            var single = JsonUtility.FromJson<ResearchReferenceViewer.Package>(originalRegistry);
                            if (format == "step_by_step") single.steps = single.steps.Take(1).ToArray(); else single.images = single.images.Take(1).ToArray();
                            FixturePackage(view, single); Pager("single " + format, false);
                            C(scroll.content.rect.height <= scroll.viewport.rect.height + 1 && !scroll.verticalScrollbar.gameObject.activeInHierarchy,
                                "single short " + format + ": no fake scrollbar", new { content = scroll.content.rect.height, viewport = scroll.viewport.rect.height, bar_active = scroll.verticalScrollbar.gameObject.activeInHierarchy });
                            Capture(format == "step_by_step" ? "Single_Step" : "Single_Image");
                            FixturePackage(view, JsonUtility.FromJson<ResearchReferenceViewer.Package>(originalRegistry));
                        }
                        Click(view, "reference_tab.step_by_step", pointer);
                        var longStep = JsonUtility.FromJson<ResearchReferenceViewer.Package>(originalRegistry);
                        string th = "TEST: ข้อความทดสอบการเลื่อนเท่านั้น ไม่ใช่คำแนะนำ Task A/B";
                        string ja = "TEST: スクロール表示のテストです。研究課題の手順ではありません。";
                        string en = "TEST: Scroll fixture only. Not a Task A/B repair instruction.";
                        longStep.steps = new[] { new ResearchReferenceViewer.Step { text_th = string.Join("\n", Enumerable.Repeat(th, 45)), text_ja = string.Join("\n", Enumerable.Repeat(ja, 45)), text_en = string.Join("\n", Enumerable.Repeat(en, 45)) } };
                        FixturePackage(view, longStep); Pager("long single step", false); Stable("long step");
                        float expectedThumb = Mathf.Clamp01(scroll.viewport.rect.height / scroll.content.rect.height);
                        C(reader.IsReady && scroll.vertical && scroll.content.rect.height > scroll.viewport.rect.height + 10 && scroll.verticalScrollbar.gameObject.activeInHierarchy
                            && Mathf.Abs(scroll.verticalScrollbar.size - expectedThumb) < .03f && scroll.verticalScrollbar.size < .99f,
                            "long step has genuine range/proportional thumb", new { content = scroll.content.rect.height, viewport = scroll.viewport.rect.height, expectedThumb, actualThumb = scroll.verticalScrollbar.size, bar_active = scroll.verticalScrollbar.gameObject.activeInHierarchy });
                        Capture("Long_Step_top"); pointer.scrollDelta = new Vector2(0, -40); ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.scrollHandler); Flush(view);
                        C(scroll.verticalNormalizedPosition < .999f, "long step actual ScrollRect scroll callback moves content", scroll.verticalNormalizedPosition);
                        scroll.verticalNormalizedPosition = .5f; Flush(view); Capture("Long_Step_mid"); scroll.verticalNormalizedPosition = 0; Flush(view); Capture("Long_Step_end");
                        FixturePackage(view, JsonUtility.FromJson<ResearchReferenceViewer.Package>(originalRegistry));
                        Click(view, "reference_tab.illustrated", pointer);
                        var captionPackage = JsonUtility.FromJson<ResearchReferenceViewer.Package>(originalRegistry);
                        var caption = captionPackage.images[0]; captionPackage.images = new[] { caption };
                        caption.caption_th = string.Join("\n", Enumerable.Repeat(th, 8)); caption.caption_ja = string.Join("\n", Enumerable.Repeat(ja, 8)); caption.caption_en = string.Join("\n", Enumerable.Repeat(en, 8));
                        FixturePackage(view, captionPackage);
                        var captionText = Field<TMP_Text>(reader, "contentText"); var sourceImage = scroll.content.GetComponentInChildren<RawImage>(true);
                        C(reader.IsReady && captionText && !captionText.isTextOverflowing && Contains(scroll.content.rect, BoundsIn(captionText.rectTransform, scroll.content)),
                            "long TEST caption has no media error/text overflow", new { reader.Status, overflow = captionText ? captionText.isTextOverflowing : (bool?)null, text_height = captionText ? captionText.rectTransform.rect.height : 0, content_height = scroll.content.rect.height });
                        C(sourceImage && sourceImage.texture && Mathf.Abs(sourceImage.rectTransform.rect.width / sourceImage.rectTransform.rect.height - (float)sourceImage.texture.width / sourceImage.texture.height) < .01f,
                            "image remains uncropped/aspect-fit", sourceImage ? RectValues(sourceImage.rectTransform) : null);
                        Stable("long caption"); Pager("single image long caption", false); Capture("Long_Caption");
                        FixturePackage(view, JsonUtility.FromJson<ResearchReferenceViewer.Package>(originalRegistry));
                        Click(view, "reference_tab.video", pointer); Stable("Video loading"); Pager("Video loading", false);
                        var timeline = reader.Timeline; var carrier = timeline.handleRect; var visibleThumb = carrier.Find("VisibleThumb") as RectTransform;
                        C(Size(carrier, 48, 64) && carrier.GetComponent<Graphic>() && carrier.GetComponent<Graphic>().color.a < .01f && visibleThumb && Size(visibleThumb, 20, 20)
                            && visibleThumb.GetComponent<Graphic>() && !visibleThumb.GetComponent<Graphic>().raycastTarget,
                            "timeline large transparent carrier/independent small visible thumb", new { carrier = RectValues(carrier), visible = visibleThumb ? RectValues(visibleThumb) : null });
                        foreach (float value in new[] { 0f, .5f, 1f })
                        {
                            timeline.SetValueWithoutNotify(value); Flush(view);
                            C(visibleThumb && Contains(((RectTransform)timeline.transform).rect, BoundsIn(visibleThumb, (RectTransform)timeline.transform)), "timeline visible thumb inside band at " + value);
                        }
                        Capture("Video_loading");
                        OutsideVisualHit("timeline", carrier, visibleThumb, new Vector2(0, 24));
                        var grip = view.referencePanel.GetComponentsInChildren<ResearchDragHandle>(true).Single(handle => handle.panel == view.referencePanel).GetComponent<RectTransform>();
                        var gripLine = grip.Find("GripLine") as RectTransform; var gripCarrier = grip.GetComponent<Graphic>();
                        C(Size(grip, 320, 80) && gripCarrier && gripCarrier.color.a < .01f && gripCarrier.raycastTarget && gripLine && Size(gripLine, 96, 6),
                            "small visible grip/transparent reachable carrier", new { carrier = RectValues(grip), visible = gripLine ? RectValues(gripLine) : null });
                        OutsideVisualHit("grip", grip, gripLine, new Vector2(100, 0));
                        var icons = view.referencePanel.GetComponentsInChildren<ResearchUIAction>(true).Where(action => action.command.StartsWith("reference_tab.")).Select(action => action.transform.Find("FormatIcon") as RectTransform).ToArray();
                        C(icons.Length == 4 && icons.All(icon => icon && Size(icon, 48, 48) && Vector3.Distance(icon.localScale, Vector3.one) < .001f),
                            "four authored 48-unit sidebar icons, not .7 shrink", icons.Select(icon => icon ? new { values = RectValues(icon), scale = icon.localScale.x } : null).ToArray());
                        var title = Field<TMP_Text>(view, "referenceTitle");
                        C(!title.isTextOverflowing && !title.enableAutoSizing && title.GetParsedText().Contains("TEST"), "readable header keeps TEST attribution", new { text = title.GetParsedText(), title.isTextOverflowing, title.enableAutoSizing });
                        var emitted = Events(controller).Skip(firstEvent).ToArray();
                        C(TaskSnapshot(controller) == task && !emitted.Any(row => new[] { "TASK_STARTED", "TASK_COMPLETED", "INSPECT_RESULT", "REPAIR_COMPLETED", "TUTORIAL_SKILL_CREDIT" }.Contains((string)row["event"])),
                            "reader actions never score repair/Inspect/Task completion", new { before = task, after = TaskSnapshot(controller), events_path = controller.Logger.EventsPath, event_count = emitted.Length });
                        C(emitted.All(row => (bool?)row["test_data"] == true && (bool?)row["technical_test"] == true), "raw events explicitly TEST, not participant data", controller.Logger.EventsPath);
                        var expandedCaptures = captures.Where(capture => (string)capture["language"] == language.ToString() && (string)capture["stage"] != "Minimized").ToArray();
                        C(expandedCaptures.All(capture => Math.Abs((double)capture["camera_field_of_view"] - (double)expandedCaptures[0]["camera_field_of_view"]) < .01
                            && JToken.DeepEquals(capture["panel_world_position"], expandedCaptures[0]["panel_world_position"])),
                            "expanded pixel captures share camera/FOV/panel pose", expandedCaptures.Select(capture => new { stage = (string)capture["stage"], fov = (double)capture["camera_field_of_view"], position = capture["panel_world_position"] }).ToArray());
                    }
                    catch (Exception exception) { Check(false, language + "/fixture execution", (exception is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : exception).ToString()); }
                    finally { (fixture as IDisposable)?.Dispose(); }
                }
            }
            finally
            {
                try { typeof(ResearchReaderWireframeChecks).GetMethod("ReleaseFontDonor", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { ownedDonor }); }
                catch (Exception error) { Check(false, "font cleanup error", error.ToString()); }
                if (priorEvents) EventSystem.current = priorEvents;
                foreach (var pair in cursors) if (pair.Key) pair.Key.HoverColor = pair.Value;
            }
            Check(originalPaths.All(asset => Hash(asset) == assetHashes[asset]), "original PDF/package/page bytes untouched by fixture cases", assetHashes);
            Check(preservedSource.All(pair => Hash(pair.Key) == pair.Value), "task/session/scoring/exporter source untouched by check", preservedSource);
            Check(beforeScenes.SequenceEqual(SceneStates()) && previews == EditorSceneManager.previewSceneCount && EventSystem.current == priorEvents,
                "fixture cleanup preserves caller scenes/dirty flags/EventSystem/preview count", new { beforeScenes, afterScenes = SceneStates(), beforePreviewCount = previews, afterPreviewCount = EditorSceneManager.previewSceneCount });
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream)) writer.Write(JsonConvert.SerializeObject(new {
                schema = "reader-refinement-test-v1", status = failures == 0 ? "PASS" : "FAIL", failures,
                scope = "TEST stopped-Editor production UI and pointer/ScrollRect callbacks with matched-camera synthetic mono captures. No fabricated prepared player/duration/frame, participant, consent, approved media, Quest or wearer result.",
                author_note = "Single/long content deep-clones the in-memory registry. Other owned canvases are suppressed/restored only inside Capture, without changing behavioral tests or adding a camera/panel move. Native playback/seek/pause acknowledgement, XR comfort and Android profile checks run separately.",
                source_root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")), hardware_verified = false, native_decoder_verified = false, checks = rows, captures
            }, Formatting.Indented));
            return JsonConvert.SerializeObject(new { status = failures == 0 ? "PASS" : "FAIL", failures, checks = rows.Count, captures = captures.Count, path, sha256 = Hash(path), hardware_verified = false, native_decoder_verified = false });
        }
    }
}
#endif
