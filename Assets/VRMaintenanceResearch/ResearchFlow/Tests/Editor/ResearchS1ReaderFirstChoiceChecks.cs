#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1ReaderFirstChoiceChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Hidden).GetValue(value);
        static T Public<T>(object value, string name) => (T)value.GetType().GetField(name).GetValue(value);
        static void Set(object value, string name, object next) => value.GetType().GetProperty(name).GetSetMethod(true).Invoke(value, new[] { next });
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
            var scene = SceneManager.GetSceneAt(i); return scene.handle + ":" + scene.path + ":" + scene.isDirty;
        }).ToArray();
        static List<JObject> Events(ResearchSessionFlowController controller)
        {
            using (var reader = new StreamReader(new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                return reader.ReadToEnd().Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).Select(JObject.Parse).ToList();
        }

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor and no XR required. This test is an isolated component fixture.");
            if (string.IsNullOrWhiteSpace(evidenceDirectory)) throw new ArgumentException("Fresh external evidence directory required.");
            var directory = Path.GetFullPath(evidenceDirectory);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Evidence collision: " + directory);
            Directory.CreateDirectory(directory);
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load a ResearchFlow scene with its three authored fonts.");
            var fixtureType = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            var before = Scenes(); int previews = EditorSceneManager.previewSceneCount;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var rows = new List<object>(); var captures = new List<object>(); int failures = 0;
            Action<bool, string, object> check = (pass, name, observed) => { rows.Add(new { pass, name, observed }); if (!pass) failures++; };
            var titles = new[] { "เลือกรูปแบบสื่อ", "資料の形式を選択", "Choose a format" };
            var prompts = new[] { "เลือกรูปแบบสื่อจากเมนูด้านซ้าย", "左側のメニューから資料の形式を選んでください", "Choose a format from the menu on the left." };
            var languages = new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English };
            try
            {
                for (int i = 0; i < languages.Length; i++)
                {
                    object fixture = null;
                    try
                    {
                        fixture = Activator.CreateInstance(fixtureType, new object[] { Path.Combine(directory, languages[i].ToString()), donor, languages[i] });
                        var controller = Public<ResearchSessionFlowController>(fixture, "Controller");
                        var view = Public<ResearchFlowView>(fixture, "View");
                        Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                        Field<ResearchTaskTiming>(controller, "taskTiming").Start(controller.Logger.Timestamp);
                        Set(controller, "ReferenceOpen", false);
                        controller.OpenReference("None");
                        var viewer = view.ReferenceViewer;
                        check(controller.ReferenceOpen && controller.ReferenceSection == "" && viewer.Status == "AwaitingSelection",
                            languages[i] + "/controller first open preserves deliberate choice", new { controller.ReferenceOpen, controller.ReferenceSection, viewer.Metadata });
                        viewer.SetVisible(true);
                        var metadata = JObject.FromObject(viewer.Metadata);
                        var text = Field<TMP_Text>(viewer, "contentText");
                        check(!viewer.IsReady && !(bool)metadata["active_reading"] && metadata["content_id"].Type == JTokenType.Null && metadata["error"].Type == JTokenType.Null,
                            languages[i] + "/visible waiting state has no media or reading", metadata);
                        check(viewer.Title == titles[i] && text && text.text == prompts[i] && Field<TMP_Text>(view, "referenceTitle").text == titles[i],
                            languages[i] + "/neutral localized text and shell title", new { viewer.Title, text = text ? text.text : null, shell_title = Field<TMP_Text>(view, "referenceTitle").text });
                        check(!viewer.NavigationVisible && !viewer.CanNext && !viewer.CanPrevious && !viewer.NativeVideoPlayer,
                            languages[i] + "/no media controls before choice", viewer.Metadata);
                        var binding = viewer.BindingToken; var semantic = viewer.SemanticToken;
                        for (int repeat = 0; repeat < 6; repeat++) view.RefreshReference();
                        viewer.SetVisible(true);
                        check(viewer.BindingToken == binding && viewer.SemanticToken == semantic,
                            languages[i] + "/repeated refresh retains binding and semantic token", new { binding, viewer.BindingToken, semantic, viewer.SemanticToken });
                        var waitingEvents = Events(controller);
                        check(!waitingEvents.Any(row => new[] { "INFORMATION_OPEN", "REFERENCE_VIEWER_ACTIVE_BEGIN", "REFERENCE_VIEWER_BOUND", "REFERENCE_VIEWER_ERROR" }.Contains((string)row["event"])),
                            languages[i] + "/no false H1 exposure or media error", waitingEvents.Select(row => (string)row["event"]).ToArray());
                        captures.Add(fixtureType.GetMethod("Capture").Invoke(fixture, new object[] { "awaiting_selection", view.referencePanel }));
                        bool selected = controller.Command("reference_tab.step_by_step", "None");
                        viewer.SetVisible(true);
                        var selectedEvents = Events(controller);
                        check(selected && controller.ReferenceSection == "step_by_step" && viewer.IsReady
                            && selectedEvents.Count(row => (string)row["event"] == "INFORMATION_FORMAT_SELECTED") == 1
                            && selectedEvents.Any(row => (string)row["event"] == "REFERENCE_VIEWER_ACTIVE_BEGIN"),
                            languages[i] + "/explicit choice reaches existing renderer and reading path", new { selected, controller.ReferenceSection, viewer.Metadata });
                        viewer.Bind("valid:error-test", "unknown", true, "");
                        check(viewer.Status == "Error", languages[i] + "/unknown format remains error", viewer.Metadata);
                        viewer.Bind("valid:error-test", "", true, "");
                        check(viewer.Status == "AwaitingSelection" && !Field<TMP_Text>(viewer, "statusLabel").gameObject.activeSelf,
                            languages[i] + "/waiting retires stale error label", viewer.Metadata);
                        viewer.Bind("", "", true, "");
                        check(viewer.Status == "Error", languages[i] + "/blank context remains error", viewer.Metadata);
                        viewer.Bind("valid:approval-test", "", false, "approval gate");
                        check(viewer.Status == "Blocked" && !viewer.IsReady, languages[i] + "/approval gate remains blocked", viewer.Metadata);
                    }
                    catch (Exception error) { check(false, languages[i] + "/fixture exception", error.ToString()); }
                    finally { (fixture as IDisposable)?.Dispose(); }
                }
            }
            finally
            {
                foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value;
            }
            check(before.SequenceEqual(Scenes()) && previews == EditorSceneManager.previewSceneCount,
                "caller scenes, dirty flags and preview count preserved", new { before, after = Scenes(), previews, after_previews = EditorSceneManager.previewSceneCount });
            var result = new { status = failures == 0 ? "EDITOR_COMPONENT_PASS" : "FAIL", failures, rows, captures,
                scope = "Owned stopped-Editor fixture and synthetic mono captures only. Viewer visibility is forced for reading-state checks. No actual Play, headset, placement acceptance or approved S1 content is established." };
            string path = Path.Combine(directory, "reader-first-choice.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(result, Formatting.Indented));
            return path;
        }
    }
}
#endif
