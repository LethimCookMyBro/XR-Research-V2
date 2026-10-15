#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchPdfZoomChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden).GetValue(owner);
        static T Public<T>(object owner, string name) => (T)owner.GetType().GetField(name).GetValue(owner);
        static void Set(object owner, string name, object value) => owner.GetType().GetProperty(name).GetSetMethod(true).Invoke(owner, new[] { value });
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor and no XR required.");
            if (!Path.IsPathRooted(evidenceDirectory)) throw new ArgumentException("Fresh absolute external evidence directory required.");
            var directory = Path.GetFullPath(evidenceDirectory);
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar);
            if (directory.Equals(project, StringComparison.OrdinalIgnoreCase) || directory.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Evidence must be external.");
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Evidence collision.");
            Directory.CreateDirectory(directory);
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Canonical fonts required.");
            var type = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var before = Scenes(); int previews = EditorSceneManager.previewSceneCount;
            var rows = new List<object>(); int failures = 0;
            void Check(bool ok, string name, object observed) { rows.Add(new { pass = ok, name, observed }); if (!ok) failures++; }
            foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
            foreach (var order in new[] { TaskOrder.AB, TaskOrder.BA })
            {
                object fixture = null; string prefix = language + "/" + order;
                try
                {
                    fixture = Activator.CreateInstance(type, new object[] { Path.Combine(directory, language + "_" + order), donor, language });
                    var controller = Public<ResearchSessionFlowController>(fixture, "Controller");
                    var view = Public<ResearchFlowView>(fixture, "View");
                    Set(controller, "Flow", new ResearchSessionFlow(order, ResearchFlowState.Task1Active, 5));
                    Field<ResearchTaskTiming>(controller, "taskTiming").Start(controller.Logger.Timestamp);
                    Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "user_manual");
                    type.GetMethod("Refresh").Invoke(fixture, null);
                    var viewer = view.ReferenceViewer; var scroll = Field<ScrollRect>(view, "scroll");
                    void Flush() { Canvas.ForceUpdateCanvases(); typeof(ResearchReferenceViewer).GetMethod("Update", Hidden).Invoke(viewer, null); typeof(ScrollRect).GetMethod("LateUpdate", Hidden).Invoke(scroll, null); Canvas.ForceUpdateCanvases(); }
                    viewer.SetVisible(true); Flush();
                    float Zoom() => (float?)JObject.FromObject(viewer.Metadata)["pdf_zoom"] ?? -1;
                    bool Command(string action) => view.LocalCommand("reference_media." + action, "None");
                    var pose = view.referencePanel.transform.position; var size = ((RectTransform)view.referencePanel.transform).sizeDelta;
                    Check(viewer.IsReady && Zoom() == 1, prefix + "/current S1 PDF begins at fit width", viewer.Metadata);
                    scroll.verticalNormalizedPosition = .55f; Flush(); int page = viewer.PageIndex;
                    float width = scroll.content.GetComponentInChildren<RawImage>().rectTransform.rect.width;
                    Check(Command("zoom_in"), prefix + "/zoom accepted through existing guarded command route", viewer.Metadata); Flush();
                    float zoomed = scroll.content.GetComponentInChildren<RawImage>().rectTransform.rect.width;
                    Check(Zoom() == 1.25f && Mathf.Abs(zoomed / width - 1.25f) < .001f && viewer.PageIndex == page,
                        prefix + "/zoom grows real pages and retains the top page", new { width, zoomed, page, current = viewer.PageIndex });
                    for (int i = 0; i < 20; i++) Command("zoom_in"); Flush();
                    Check(Zoom() == 3 && !Command("zoom_in"), prefix + "/upper zoom bound rejects further changes", viewer.Metadata);
                    for (int i = 0; i < 10; i++) Command("pan_left"); Flush();
                    Check(scroll.horizontalNormalizedPosition < .001f && !Command("pan_left"), prefix + "/left document edge reachable and bounded", scroll.horizontalNormalizedPosition);
                    for (int i = 0; i < 10; i++) Command("pan_right"); Flush();
                    Check(scroll.horizontalNormalizedPosition > .999f && !Command("pan_right"), prefix + "/right document edge reachable and bounded", scroll.horizontalNormalizedPosition);
                    Check(Command("zoom_reset"), prefix + "/fit-width reset accepted", viewer.Metadata); Flush();
                    Check(Zoom() == 1 && !Command("zoom_out") && !Command("pan_left") && !Command("pan_right") && !Command("zoom_reset"), prefix + "/fit-width controls reject no-op changes", viewer.Metadata);
                    Command("zoom_in"); Command("pan_right"); Flush(); float horizontal = scroll.horizontalNormalizedPosition;
                    viewer.SetVisible(false);
                    Check(!Command("zoom_in"), prefix + "/hidden viewer rejects zoom", viewer.Metadata);
                    viewer.SetVisible(true); Flush();
                    Check(Zoom() == 1.25f && Mathf.Abs(scroll.horizontalNormalizedPosition - horizontal) < .001f, prefix + "/hide-show retains zoom and pan", viewer.Metadata);
                    Set(controller, "ReferenceSection", "step_by_step"); view.RefreshReference(); viewer.SetVisible(true); Flush();
                    Check(!Command("zoom_in") && !view.GetComponentsInChildren<ResearchUIAction>(false).Any(a => a.command == "reference_media.zoom_in"), prefix + "/non-PDF retires zoom controls", viewer.Metadata);
                    Set(controller, "ReferenceSection", "user_manual"); view.RefreshReference(); viewer.SetVisible(true); Flush();
                    Check(Zoom() == 1.25f && Mathf.Abs(scroll.horizontalNormalizedPosition - horizontal) < .001f, prefix + "/format return restores PDF zoom and pan", viewer.Metadata);
                    controller.Flow.Pause();
                    Check(!Command("zoom_in") && !Command("pan_left"), prefix + "/pause rejects zoom and pan", viewer.Metadata);
                    Check(view.referencePanel.transform.position == pose && ((RectTransform)view.referencePanel.transform).sizeDelta == size && controller.Record == null,
                        prefix + "/fixed panel and no real session preserved", new { pose = new[] { pose.x, pose.y, pose.z }, actual = new[] { view.referencePanel.transform.position.x, view.referencePanel.transform.position.y, view.referencePanel.transform.position.z }, size = new[] { size.x, size.y }, actualSize = new[] { ((RectTransform)view.referencePanel.transform).sizeDelta.x, ((RectTransform)view.referencePanel.transform).sizeDelta.y } });
                }
                catch (Exception error) { Check(false, prefix + "/execution", error.ToString()); }
                finally { (fixture as IDisposable)?.Dispose(); foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value; }
            }
            Check(before.SequenceEqual(Scenes()) && previews == EditorSceneManager.previewSceneCount, "caller scenes and preview count preserved", new { before, after = Scenes() });
            string output = Path.Combine(directory, "pdf-zoom.json");
            File.WriteAllText(output, JsonConvert.SerializeObject(new { failures, rows, scope = "Owned actual Unity components and LocalCommand route. Explicit viewer visibility. No pointer ray, SDK, focus acquisition, Quest, lens or language acceptance." }, Formatting.Indented));
            return output;
        }
        public static string RunExtended(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor and no XR required.");
            if (!Path.IsPathRooted(evidenceDirectory)) throw new ArgumentException("Fresh absolute external evidence directory required.");
            string directory = Path.GetFullPath(evidenceDirectory), project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar);
            if (directory.Equals(project, StringComparison.OrdinalIgnoreCase) || directory.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Evidence must be external.");
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Evidence collision.");
            Directory.CreateDirectory(directory);
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Canonical fonts required.");
            var type = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var before = Scenes(); int previews = EditorSceneManager.previewSceneCount;
            var rows = new List<object>(); var captures = new List<object>(); int failures = 0;
            void Check(bool ok, string name, object observed) { rows.Add(new { pass = ok, name, observed }); if (!ok) failures++; }
            foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
            foreach (var order in new[] { TaskOrder.AB, TaskOrder.BA })
            {
                object fixture = null; string prefix = language + "/" + order;
                try
                {
                    fixture = Activator.CreateInstance(type, new object[] { Path.Combine(directory, language + "_" + order), donor, language });
                    var controller = Public<ResearchSessionFlowController>(fixture, "Controller");
                    var view = Public<ResearchFlowView>(fixture, "View");
                    Set(controller, "Flow", new ResearchSessionFlow(order, ResearchFlowState.Task1Active, 5));
                    Field<ResearchTaskTiming>(controller, "taskTiming").Start(controller.Logger.Timestamp);
                    Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "user_manual");
                    type.GetMethod("Refresh").Invoke(fixture, null);
                    var viewer = view.ReferenceViewer; var scroll = Field<ScrollRect>(view, "scroll");
                    void Flush() { Canvas.ForceUpdateCanvases(); typeof(ResearchReferenceViewer).GetMethod("Update", Hidden).Invoke(viewer, null); typeof(ScrollRect).GetMethod("LateUpdate", Hidden).Invoke(scroll, null); Canvas.ForceUpdateCanvases(); }
                    void Refresh() { view.RefreshReference(); viewer.SetVisible(true); Flush(); }
                    float Zoom() => (float?)JObject.FromObject(viewer.Metadata)["pdf_zoom"] ?? -1;
                    List<JObject> ViewEvents()
                    {
                        using (var file = new StreamReader(new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                            return file.ReadToEnd().Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).Select(JObject.Parse)
                                .Where(row => (string)row["event"] == "REFERENCE_PDF_VIEW_CHANGED").ToList();
                    }
                    int accepted = 0; var expectedViews = new List<JObject>();
                    bool Command(string action) { bool result = view.LocalCommand("reference_media." + action, "None"); if (result) { accepted++; expectedViews.Add(JObject.FromObject(viewer.Metadata)); } return result; }
                    viewer.SetVisible(true); Flush();
                    int initialEvents = ViewEvents().Count;
                    Check(viewer.IsReady && Zoom() == 1, prefix + "/fit-width current S1 PDF ready", viewer.Metadata);
                    captures.Add(type.GetMethod("Capture").Invoke(fixture, new object[] { "pdf_fit_width", view.referencePanel }));
                    Command("zoom_in"); Command("zoom_in"); Flush();
                    Check(Zoom() == 1.5f, prefix + "/150 percent reached", viewer.Metadata);
                    captures.Add(type.GetMethod("Capture").Invoke(fixture, new object[] { "pdf_150_percent", view.referencePanel }));
                    var expectedCommands = new[] { "zoom_out", "zoom_reset", "zoom_in", "pan_left", "pan_right" };
                    var expectedLabels = new[] { "-", "150%", "+", "<", ">" };
                    var panel = (RectTransform)view.referencePanel.transform;
                    for (int i = 0; i < expectedCommands.Length; i++)
                    {
                        var action = view.GetComponentsInChildren<ResearchUIAction>(false).Single(a => a.command == "reference_media." + expectedCommands[i]);
                        var rect = (RectTransform)action.transform;
                        var text = action.GetComponentInChildren<TMPro.TMP_Text>(); text.ForceMeshUpdate(true, true);
                        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                        var local = corners.Select(panel.InverseTransformPoint).ToArray();
                        bool inside = local.All(point => panel.rect.Contains(new Vector2(point.x, point.y)));
                        Check(Mathf.Abs(rect.rect.width - 88) < .01f && Mathf.Abs(rect.rect.height - 48) < .01f && inside
                            && text.GetParsedText() == expectedLabels[i] && !text.isTextOverflowing,
                            prefix + "/zoom control label, actual hit area and panel bounds/" + expectedCommands[i],
                            new { label = text.GetParsedText(), width = rect.rect.width, height = rect.rect.height, font = text.font.name, font_size = text.fontSize,
                                overflow = text.isTextOverflowing, label_rect = new[] { text.rectTransform.rect.width, text.rectTransform.rect.height },
                                label_margin = new[] { text.margin.x, text.margin.y, text.margin.z, text.margin.w }, preferred_height = text.GetPreferredValues(text.text, text.rectTransform.rect.width, 10000).y,
                                corners = local.Select(point => new[] { point.x, point.y, point.z }).ToArray() });
                    }
                    scroll.verticalNormalizedPosition = .65f;
                    float offset = (1 - scroll.verticalNormalizedPosition) * Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
                    var images = scroll.content.GetComponentsInChildren<RawImage>().Where(image => image.name.StartsWith("PDFPage_", StringComparison.Ordinal)).ToArray();
                    int actualPage = images.Count(image => -image.rectTransform.anchoredPosition.y <= offset + 16);
                    Check(Command("zoom_in"), prefix + "/same-frame scroll then zoom accepted", viewer.Metadata); Flush();
                    Check(viewer.PageIndex == actualPage, prefix + "/same-frame zoom retains actual viewport page, not stale callback index",
                        new { expectedPage = actualPage, actualPage = viewer.PageIndex, offset });
                    Command("pan_right"); Flush();
                    float retainedZoom = Zoom(), retainedPan = scroll.horizontalNormalizedPosition;
                    string originalResource = viewer.BoundRegistryResource, originalContext = (string)JObject.FromObject(viewer.Metadata)["context"];
                    Set(controller, "Flow", new ResearchSessionFlow(order, ResearchFlowState.Task2Active, 5)); Refresh();
                    string secondResource = viewer.BoundRegistryResource;
                    Check(secondResource != originalResource && Zoom() == 1 && Mathf.Abs(scroll.content.anchoredPosition.x) < .001f,
                        prefix + "/other task begins at its own fit width", new { originalResource, secondResource, metadata = viewer.Metadata });
                    Command("zoom_in"); Flush();
                    Set(controller, "Flow", new ResearchSessionFlow(order, ResearchFlowState.Task1Active, 5)); Refresh();
                    Check(viewer.BoundRegistryResource == originalResource && Zoom() == retainedZoom && Mathf.Abs(scroll.horizontalNormalizedPosition - retainedPan) < .001f,
                        prefix + "/returning task restores only its own zoom and pan", viewer.Metadata);
                    var otherLanguage = language == ParticipantLanguage.Thai ? ParticipantLanguage.Japanese : ParticipantLanguage.Thai;
                    Set(controller, "SetupLanguage", otherLanguage); Refresh();
                    Check(viewer.BoundRegistryResource != originalResource && Zoom() == 1 && Mathf.Abs(scroll.content.anchoredPosition.x) < .001f,
                        prefix + "/other language starts isolated", viewer.Metadata);
                    Command("zoom_in"); Flush();
                    Set(controller, "SetupLanguage", language); Refresh();
                    Check(viewer.BoundRegistryResource == originalResource && (string)JObject.FromObject(viewer.Metadata)["context"] == originalContext
                        && Zoom() == retainedZoom && Mathf.Abs(scroll.horizontalNormalizedPosition - retainedPan) < .001f,
                        prefix + "/language return restores original package context", viewer.Metadata);
                    var persisted = ViewEvents().Skip(initialEvents).ToList();
                    Check(persisted.Count == accepted && persisted.All(row => (string)row["source"] == "Tester" && (string)row["input_source"] == "None"
                        && (string)row["target"] == "reference.user_manual" && (float?)row["after"]?["pdf_zoom"] >= 1 && (float?)row["after"]?["pdf_zoom"] <= 3
                        && (float?)row["after"]?["pdf_horizontal"] >= -.001f && (float?)row["after"]?["pdf_horizontal"] <= 1.001f),
                        prefix + "/accepted controls persist exactly one correctly attributed bounded event",
                        new { accepted, persisted = persisted.Count, events_path = controller.Logger.EventsPath, rows = persisted });
                    Check(persisted.Count == expectedViews.Count && persisted.Select((row, i) => (string)row["after"]["context"] == (string)expectedViews[i]["context"]
                        && (string)row["after"]["content_id"] == (string)expectedViews[i]["content_id"]
                        && (string)row["after"]["registry_sha256"] == (string)expectedViews[i]["registry_sha256"]
                        && Mathf.Abs((float)row["after"]["pdf_zoom"] - (float)expectedViews[i]["pdf_zoom"]) < .001f
                        && Mathf.Abs((float)row["after"]["pdf_horizontal"] - (float)expectedViews[i]["pdf_horizontal"]) < .001f).All(equal => equal),
                        prefix + "/persisted values match every observed accepted context in order", new { expected = expectedViews, actual = persisted.Select(row => row["after"]).ToArray() });
                    int prior = ViewEvents().Count;
                    viewer.SetVisible(false); bool hidden = Command("zoom_in"); viewer.SetVisible(true); Flush();
                    controller.Flow.Pause(); bool pausedZoom = Command("zoom_in"), pausedPan = Command("pan_left");
                    Check(!hidden && !pausedZoom && !pausedPan && ViewEvents().Count == prior,
                        prefix + "/hidden and paused commands persist no accepted-change event", new { hidden, pausedZoom, pausedPan, prior, after = ViewEvents().Count });
                }
                catch (Exception error) { Check(false, prefix + "/extended execution", error.ToString()); }
                finally { (fixture as IDisposable)?.Dispose(); foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value; }
            }
            Check(before.SequenceEqual(Scenes()) && previews == EditorSceneManager.previewSceneCount, "extended caller scenes and preview count preserved", new { before, after = Scenes() });
            string output = Path.Combine(directory, "pdf-zoom-extended.json");
            File.WriteAllText(output, JsonConvert.SerializeObject(new { failures, rows, captures,
                scope = "Owned actual Unity components and persisted TEST logger, fitted synthetic mono pixels. Existing basic57 checks unchanged. No pointer ray/SDK/focus acquisition/Quest/lens/language acceptance." }, Formatting.Indented));
            return output;
        }

    }
}
#endif
