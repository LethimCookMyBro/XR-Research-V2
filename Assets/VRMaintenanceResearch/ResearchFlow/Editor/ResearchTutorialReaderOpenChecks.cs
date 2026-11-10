#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
    /// <summary>Actual pointer-down/up commands in isolated owned Tutorial and task UI.</summary>
    public static class ResearchTutorialReaderOpenChecks
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Instance).GetValue(value);
        static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Instance).Invoke(value, args);
        static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name, Instance).GetValue(value);
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var scene = SceneManager.GetSceneAt(i); return scene.handle + ":" + scene.path + ":" + scene.isDirty; }).ToArray();

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed
                || !Path.IsPathRooted(evidenceDirectory) || Directory.Exists(evidenceDirectory))
                throw new InvalidOperationException("Stopped Editor without XR and fresh absolute evidence directory required.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Authored TH/JA/EN font donor required.");
            var ownedType = typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned", BindingFlags.NonPublic);
            var beforeScenes = Scenes(); int beforePreviews = EditorSceneManager.previewSceneCount;
            string beforePolicy = ResearchUIPolicy.Active;
            var priorEvents = EventSystem.current;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var checks = new List<object>(); var failures = new List<string>(); var captures = new List<object>(); var layout = new List<object>();
            void Check(bool pass, string id, object detail = null)
            { checks.Add(new { id, pass, detail }); if (!pass) failures.Add(id); }
            Directory.CreateDirectory(evidenceDirectory);

            void Context(ParticipantLanguage language, string task, bool missingRegistry)
            {
                string prefix = language + "/" + task + (missingRegistry ? "/missing_registry" : "");
                object owned = null; ResearchFlowView view = null; Action<bool, string> visibility = null;
                IDictionary cache = null; string injectedResource = null; object previousRegistry = null; bool hadRegistry = false;
                try
                {
                    owned = Activator.CreateInstance(ownedType, Instance, null,
                        new object[] { Path.Combine(evidenceDirectory, prefix.Replace('/', '_')), donor, language }, null);
                    var controller = Field<ResearchSessionFlowController>(owned, "Controller");
                    view = Field<ResearchFlowView>(owned, "View");
                    visibility = (Action<bool, string>)Delegate.CreateDelegate(typeof(Action<bool, string>), controller,
                        controller.GetType().GetMethod("ReferenceVisibilityChanged", Instance));
                    view.ReferenceVisibilityChanged += visibility;
                    void Click(string command)
                    {
                        var action = (ResearchUIAction)Call(owned, "Action", command, null);
                        // Owned.Click dispatches ExecuteEvents pointerDown, pointerUp and pointerClick.
                        Call(owned, "Click", action, 1);
                    }
                    void Flush()
                    {
                        Call(owned, "Refresh"); Canvas.ForceUpdateCanvases();
                        bool settled = false;
                        foreach (var panel in new[] { view.mainPanel, view.referencePanel })
                            if (panel && panel.gameObject.activeInHierarchy && panel.RecenterPending)
                            { Call(panel, "TryApplyPendingRecenter", true, true, true, Time.frameCount + 1); settled |= !panel.RecenterPending; }
                        if (settled) { view.RefreshReference(); Canvas.ForceUpdateCanvases(); }
                        var scroll = Field<ScrollRect>(view, "scroll");
                        Call(scroll, "LateUpdate");
                        if (view.ReferenceViewer) Call(view.ReferenceViewer, "Update");
                        Call(view, "SyncReaderVisibility");
                        view.NotifyReferenceVisibility("owned_pointer_fixture");
                        Canvas.ForceUpdateCanvases();
                    }
                    object State() => new
                    {
                        controller.ReferenceOpen, controller.ReferenceSection, controller.ReaderResource,
                        status = view.ReferenceViewer ? view.ReferenceViewer.Status : null,
                        surface = Property<bool>(view, "ReferenceSurfaceShown"), content_visible = view.IsReferenceActuallyVisible,
                        view.referencePanel.HasAcceptedPose, view.referencePanel.RecenterPending, view.referencePanel.PlacementBlocked,
                        exposure = Field<string>(controller, "exposureAction")
                    };
                    if (task == "T") Call(owned, "Seed", ResearchFlowState.TutorialReferenceInspect, 3, (bool?)null);
                    else
                    {
                        if (task == "B") Call(owned, "Seed", ResearchFlowState.Task2Ready, 5, (bool?)null);
                        Click("task_start"); Click("task_start_confirm");
                    }
                    Flush();
                    string locale = language == ParticipantLanguage.Thai ? "th" : language == ParticipantLanguage.Japanese ? "ja" : "en";
                    string expected = "ResearchReaderS1/" + task + "6_" + locale + "/package";
                    Check(controller.ReaderResource == expected && controller.ReaderAuthorized, prefix + "/routing_and_authorization", State());
                    if (missingRegistry)
                    {
                        injectedResource = controller.ReaderResource;
                        cache = (IDictionary)typeof(ResearchReferenceViewer).GetField("registries", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        hadRegistry = cache.Contains(injectedResource); previousRegistry = hadRegistry ? cache[injectedResource] : null;
                        var type = typeof(ResearchReferenceViewer).GetNestedType("Registry", BindingFlags.NonPublic);
                        var broken = Activator.CreateInstance(type, true);
                        type.GetField("error").SetValue(broken, "Missing content registry (owned failure-path fixture)");
                        cache[injectedResource] = broken;
                        Check(ResearchReferenceScheme.Formats.All(f => !controller.ReferenceAvailable(f)), prefix + "/all_formats_unavailable");
                    }
                    Click("reference"); Flush();
                    Check(controller.ReferenceOpen, prefix + "/pointer_open_completed_without_exception", State());
                    Check(Property<bool>(view, "ReferenceSurfaceShown"), prefix + "/reader_surface_shown", State());
                    var viewer = view.ReferenceViewer;
                    if (missingRegistry)
                    {
                        var text = viewer.GetComponentsInChildren<TMP_Text>(true).Where(t => t.gameObject.activeInHierarchy)
                            .Select(t => t.text).Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
                        // Viewer labels belong to scroll descendants, not necessarily the viewer component's subtree.
                        var statusLabel = Field<TMP_Text>(viewer, "statusLabel");
                        var contentText = Field<TMP_Text>(viewer, "contentText");
                        bool message = (statusLabel && statusLabel.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(statusLabel.text))
                            || (contentText && contentText.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(contentText.text));
                        Check(viewer.Status == "Error" && message && !view.IsReferenceActuallyVisible
                            && Field<string>(controller, "exposureAction") == null, prefix + "/unavailable_content_has_error_without_exposure", new { state = State(), text });
                        Click("reference_close"); Flush();
                        Check(!controller.ReferenceOpen && view.mainPanel.gameObject.activeInHierarchy, prefix + "/error_can_close", State());
                        return;
                    }
                    if (task == "T")
                        Check(viewer.IsReady && view.IsReferenceActuallyVisible && Field<string>(controller, "exposureAction") != null,
                            prefix + "/default_manual_ready_and_exposed", State());
                    else
                        Check(viewer.Status == "AwaitingSelection" && !view.IsReferenceActuallyVisible
                            && Field<string>(controller, "exposureAction") == null, prefix + "/chooser_does_not_count_as_reading", State());
                    foreach (string format in ResearchReferenceScheme.Formats)
                    {
                        Click("reference_tab." + format); Flush();
                        Check(viewer.BoundSection == format && viewer.BoundRegistryResource == expected,
                            prefix + "/" + format + "/selected_bound_content", State());
                        if (format == "video")
                            Check(viewer.Status == "Loading" && !viewer.IsReady && !view.IsReferenceActuallyVisible
                                && Field<string>(controller, "exposureAction") == null, prefix + "/video_loading_not_counted_as_decoded", State());
                        else
                            Check(viewer.IsReady && view.IsReferenceActuallyVisible && Field<string>(controller, "exposureAction") != null,
                                prefix + "/" + format + "/ready_visible_and_exposed", State());
                        if (task == "T" && format != "video") Call(owned, "Capture", format, view.referencePanel, captures, layout);
                    }
                    Click("reference_close"); Flush();
                    Check(!controller.ReferenceOpen && view.mainPanel.gameObject.activeInHierarchy
                        && Field<string>(controller, "exposureAction") == null, prefix + "/close_restores_main_and_ends_exposure", State());
                    Click("reference"); Flush();
                    Check(controller.ReferenceOpen && Property<bool>(view, "ReferenceSurfaceShown"), prefix + "/reopen_surface_shown", State());
                    Click("reference_tab.user_manual"); Flush();
                    Check(viewer.IsReady && view.IsReferenceActuallyVisible, prefix + "/reopen_manual_ready", State());
                }
                catch (Exception error) { Check(false, prefix + "/exception", error.ToString()); }
                finally
                {
                    if (cache != null && injectedResource != null)
                    { if (hadRegistry) cache[injectedResource] = previousRegistry; else cache.Remove(injectedResource); }
                    if (view && visibility != null) view.ReferenceVisibilityChanged -= visibility;
                    (owned as IDisposable)?.Dispose();
                }
            }
            try
            {
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
                    foreach (var task in new[] { "T", "A", "B" }) Context(language, task, false);
                Context(ParticipantLanguage.Thai, "T", true);
            }
            finally
            {
                EventSystem.current = priorEvents;
                foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value;
                Check(beforeScenes.SequenceEqual(Scenes()) && beforePreviews == EditorSceneManager.previewSceneCount, "owned_scenes_restored");
                Check(ResearchUIPolicy.Active == beforePolicy, "ui_policy_unchanged", ResearchUIPolicy.Active);
            }
            string status = failures.Count == 0 ? "PASS" : "ISSUES";
            File.WriteAllText(Path.Combine(evidenceDirectory, "report.json"), JsonConvert.SerializeObject(new
            {
                status, evidence = "STOPPED_EDITOR_POINTER_COMMANDS_NOT_NATIVE_CONTROLLER_OR_VIDEO_PLAYBACK",
                checks = checks.Count, failures, rows = checks, captures, layout
            }, Formatting.Indented), new UTF8Encoding(false));
            return "TUTORIAL_READER_OPEN " + status + " checks=" + checks.Count + " failures=" + failures.Count;
        }
    }
}
#endif
