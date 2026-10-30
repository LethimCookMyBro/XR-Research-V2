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
    public static class ResearchV4PartFeedbackLayoutChecks
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags).GetValue(o);
        static object Call(object o, string key, params object[] args) => o.GetType().GetMethod(key, Flags).Invoke(o, args);
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();
        static float Luminance(Color c)
        {
            float Linear(float v) => v <= .04045f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f);
            return .2126f * Linear(c.r) + .7152f * Linear(c.g) + .0722f * Linear(c.b);
        }
        public static string Run(string directory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor with XR inactive required.");
            if (!Path.IsPathRooted(directory) || Directory.Exists(directory)) throw new IOException("Fresh absolute directory required.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Authored three-font donor required.");
            var ownedType = typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned", BindingFlags.NonPublic);
            var before = Scenes(); int previews = EditorSceneManager.previewSceneCount;
            var events = EventSystem.current;
            var captures = new List<object>(); var rows = new List<object>(); var failures = new List<string>();
            var errors = new List<string>(); int assertions = 0;
            Application.LogCallback onLog = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception) errors.Add(message + "\n" + stack); };
            void Check(bool pass, string name, object value = null)
            { assertions++; rows.Add(new { name, passed = pass, value }); if (!pass) failures.Add(name); }
            Directory.CreateDirectory(directory); Application.logMessageReceived += onLog;
            try
            {
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
                foreach (string task in new[] { "A", "B" })
                {
                    object owned = null;
                    Camera ownedCamera = null;
                    Transform originalCameraParent = null;
                    Vector3 originalCameraPosition = default;
                    Quaternion originalCameraRotation = default;
                    bool originalCameraEnabled = false;
                    Scene originalCameraScene = default;
                    ulong originalCameraMask = 0;
                    string prefix = language + "/" + task;
                    try
                    {
                        owned = Activator.CreateInstance(ownedType, Flags, null, new object[] { Path.Combine(directory, language + "_" + task), donor, language }, null);
                        var controller = Field<ResearchSessionFlowController>(owned, "Controller");
                        var view = Field<ResearchFlowView>(owned, "View");
                        // VisualClarity's camera is disabled under an inactive Controller root.
                        // Detach and enable only this owned camera so the real visibility predicate can succeed.
                        ownedCamera = view.Head.GetComponent<Camera>();
                        originalCameraParent = ownedCamera.transform.parent;
                        originalCameraPosition = ownedCamera.transform.position;
                        originalCameraRotation = ownedCamera.transform.rotation;
                        originalCameraEnabled = ownedCamera.enabled;
                        originalCameraScene = ownedCamera.scene;
                        originalCameraMask = ownedCamera.overrideSceneCullingMask;
                        ownedCamera.transform.SetParent(null, true);
                        ownedCamera.enabled = true;
                        ownedCamera.scene = view.gameObject.scene;
                        ownedCamera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(view.gameObject.scene);
                        // This is the scoped initializer; do not call View.Awake, which touches global cursor visuals.
                        Call(view, "PrepareRecoveryUtility");
                        controller.Record.training_version = ResearchUIPolicy.TrainingTrainerV4;
                        Call(owned, "Seed", task == "A" ? ResearchFlowState.Task1Ready : ResearchFlowState.Task2Ready, 5, null);
                        void Click(string command) => Call(owned, "Click", Call(owned, "Action", command, null), 1);
                        Click("task_start"); Click("task_start_confirm");
                        view.mainPanel.Recenter(false, false, reason: "owned_v4_layout_fixture");
                        Canvas.ForceUpdateCanvases();
                        // Look naturally at the production-placed Main, retaining the fixture's 90-degree FOV.
                        var mainCorners = new Vector3[4];
                        view.mainPanel.GetComponent<RectTransform>().GetWorldCorners(mainCorners);
                        var mainCenter = (mainCorners[0] + mainCorners[2]) / 2;
                        if ((mainCenter - ownedCamera.transform.position).sqrMagnitude > .01f)
                            ownedCamera.transform.rotation = Quaternion.LookRotation(mainCenter - ownedCamera.transform.position, Vector3.up);
                        var render = new RenderTexture(1280, 800, 24);
                        var previousTarget = ownedCamera.targetTexture;
                        try { ownedCamera.targetTexture = render; ownedCamera.Render(); }
                        finally { ownedCamera.targetTexture = previousTarget; render.Release(); UnityEngine.Object.DestroyImmediate(render); }
                        var port = controller.taskPorts.Single(p => p.taskId == task);
                        Check(controller.Flow.IsTaskActive && port.InteractionAllowed, prefix + "/actual task action started");
                        Check(controller.ReaderResource.Contains(task + "5_"), prefix + "/new reader routing", controller.ReaderResource);
                        Check(!view.GetComponentsInChildren<ResearchUIAction>(false).Any(a => a.command == "examine_open"), prefix + "/new Examine absent");
                        var state = new S1TaskBState { systemIntegrityValid = true, powerIsolated = true, frontGuardPlacement = S1Placement.Seated,
                            frontGuardAligned = true, frontGuardClipped = true, rearGuardPlacement = S1Placement.Seated, rearLock = S1ThreadState.Tight };
                        port.BindExamineInfo((target, lang) => task == "A" && ResearchS1TaskACatalog.Ids.Contains(target)
                            ? ResearchS1TaskACatalog.Label(target) : task == "B" ? ResearchS1FanObservations.Get(state, target, lang)
                            : ResearchStrings.Get("examine.info.unavailable", lang));
                        void Refresh() { Call(view, "RefreshReaderFeedback"); Canvas.ForceUpdateCanvases(); }
                        TMP_Text Label() => Field<TMP_Text>(view, "readerMainFeedbackLabel");
                        void Capture(string stage)
                        {
                            Call(owned, "Capture", stage, view.mainPanel, captures, rows);
                            Refresh();
                            var text = Label();
                            if (text && text.isActiveAndEnabled)
                            {
                                text.ForceMeshUpdate(true, true);
                                Check(!text.isTextOverflowing, prefix + "/" + stage + "/feedback fits", new { text.text, text.preferredHeight, height = text.rectTransform.rect.height });
                                Check(!text.raycastTarget, prefix + "/" + stage + "/feedback does not own ray");
                            var background = Field<GameObject>(view, "mainContent").transform.Find("ContentSurface").GetComponent<Graphic>().color;
                                float foregroundL = Luminance(text.color), backgroundL = Luminance(background);
                                float contrast = (Mathf.Max(foregroundL, backgroundL) + .05f) / (Mathf.Min(foregroundL, backgroundL) + .05f);
                                Check(text.color.a >= .99f && background.a >= .99f && contrast >= 4.5f,
                                    prefix + "/" + stage + "/opaque text contrast", new { foreground = text.color.ToString(), surface = background.ToString(), contrast });
                            }
                        }
                        var originalPosition = view.mainPanel.transform.position; var originalRotation = view.mainPanel.transform.rotation;
                        string targetId = task == "A" ? "M01" : "B.front_guard_clip";
                        var attempt = port.BeginAttempt(targetId, "SimulatedController");
                        Refresh();
                        var viewport = Field<ScrollRect>(view, "taskBriefScroll").viewport;
                        var viewportCorners = new Vector3[4]; viewport.GetWorldCorners(viewportCorners);
                        rows.Add(new {
                            name = prefix + "/feedback gate snapshot",
                            current_interaction = controller.CurrentInteraction.HasValue,
                            port_interaction = port.CurrentInteraction.HasValue,
                            camera_active = ownedCamera.isActiveAndEnabled,
                            camera_enabled = ownedCamera.enabled,
                            camera_position = ownedCamera.transform.position.ToString(),
                            camera_fov = ownedCamera.fieldOfView,
                            main_active = view.mainPanel.gameObject.activeInHierarchy,
                            main_canvas = view.mainPanel.GetComponent<Canvas>().enabled,
                            main_placement_blocked = view.mainPanel.PlacementBlocked,
                            main_alpha = Field<CanvasGroup>(view, "mainGroup").alpha,
                            viewport_active = viewport.gameObject.activeInHierarchy,
                            viewport_fits = Call(view, "FeedbackRectFits", viewport),
                            viewport_points = viewportCorners.Select(p => ownedCamera.WorldToViewportPoint(p).ToString()).ToArray(),
                            reader_feedback_initialized = Field<TMP_Text>(view, "readerFeedbackLabel") != null,
                            recovery_created = view.RecoveryCanvas != null,
                            main_feedback_predicate = view.GetType().GetProperty("MainCanShowReaderFeedback", Flags).GetValue(view),
                            label_created = Label() != null,
                            label_active = Label() && Label().isActiveAndEnabled
                        });
                        bool feedbackVisible = Label() && Label().isActiveAndEnabled;
                        Check(feedbackVisible, prefix + "/held feedback visible");
                        if (!feedbackVisible) throw new InvalidOperationException("Held feedback unavailable; inspect the recorded real visibility gates.");
                        Check(Label().text.Contains(task == "A" ? "Grip" : "Trigger"), prefix + "/held control matches part", Label().text);
                        Capture("held");
                        port.UpdateInteraction(attempt, PartOperation.Move, PartBlockReason.OtherHeld); Refresh(); Capture("blocked");
                        Check(Label().text.Contains(ResearchStrings.Get("interaction.reason.OtherHeld", controller.Language)), prefix + "/block reason visible");
                        port.UpdateInteraction(attempt, PartOperation.Move, PartBlockReason.None); Refresh(); Capture("clear");
                        Check(!Label().text.Contains(ResearchStrings.Get("interaction.reason.OtherHeld", controller.Language)), prefix + "/success clears reason without audit");
                        Check(Vector3.Distance(originalPosition, view.mainPanel.transform.position) < .00001f && Quaternion.Angle(originalRotation, view.mainPanel.transform.rotation) < .001f,
                            prefix + "/feedback does not move panel");
                        port.EndAttempt(attempt); Refresh(); Capture("release");
                        Check(!Label().gameObject.activeSelf && Field<RectTransform>(view, "taskBrief").gameObject.activeSelf, prefix + "/release restores brief");
                        if (task == "A")
                        foreach (string cable in new[] { "A.sata_data", "A.sata_power", "A.pcie_power" })
                        {
                            var cableAttempt = port.BeginAttempt(cable, "SimulatedController"); Refresh();
                            Check(Label().text.Contains("Trigger") && !Label().text.Contains("Grip"), prefix + "/cable pinch control/" + cable, Label().text);
                            Capture(cable.Replace('.', '_')); port.EndAttempt(cableAttempt); Refresh();
                        }
                        var pausedAttempt = port.BeginAttempt(targetId, "SimulatedController"); Refresh();
                        Click("pause"); Refresh(); Capture("paused");
                        Check(controller.Flow.Paused && controller.CurrentInteraction == null && !Label().gameObject.activeSelf, prefix + "/pause clears feedback");
                        Check(!port.IsAttemptActive(pausedAttempt), prefix + "/pause closes attempt");
                        // Render the historical policy in this disposable fixture; do not restart the running clock.
                        Call(owned, "Seed", task == "A" ? ResearchFlowState.Task1Active : ResearchFlowState.Task2Active, 5, null);
                        // Presentation-only historical record view after Seed; SaveProgress correctly keeps persisted V4 history.
                        controller.Record.training_version = ResearchUIPolicy.TrainingTrainerV4;
                        Check(controller.ReaderResource.Contains(task + "4_"), prefix + "/historical V4 reader preserved", controller.ReaderResource);
                        controller.Record.training_version = ResearchUIPolicy.TrainingTrainerV3;
                        Call(owned, "Refresh");
                        Check(!controller.ReaderResource.Contains(task + "4_") && view.GetComponentsInChildren<ResearchUIAction>(false).Any(a => a.command == "examine_open"),
                            prefix + "/historical T3 reader and Examine retained");
                        Capture("historical_T3");
                        Check(Field<List<string>>(owned, "TextOverflows").Count == 0, prefix + "/all captured TMP fit", Field<List<string>>(owned, "TextOverflows").ToArray());
                    }
                    catch (Exception error)
                    {
                        var actual = error is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : error;
                        Check(false, prefix + "/execution", actual.ToString());
                    }
                    finally
                    {
                        if (ownedCamera)
                        {
                            ownedCamera.enabled = originalCameraEnabled;
                            ownedCamera.scene = originalCameraScene;
                            ownedCamera.overrideSceneCullingMask = originalCameraMask;
                            ownedCamera.transform.SetParent(originalCameraParent, true);
                            ownedCamera.transform.SetPositionAndRotation(originalCameraPosition, originalCameraRotation);
                        }
                        (owned as IDisposable)?.Dispose();
                    }
                }
            }
            finally { Application.logMessageReceived -= onLog; }
            Check(errors.Count == 0, "no Unity errors", errors.ToArray());
            Check(before.SequenceEqual(Scenes()) && previews == EditorSceneManager.previewSceneCount && events == EventSystem.current, "caller surfaces restored");
            string status = failures.Count == 0 ? "PASS" : "FAIL";
            File.WriteAllText(Path.Combine(directory, "v4-part-feedback-layout.json"), JsonConvert.SerializeObject(new
            { status, assertions, failures, rows, captures, scope = "Owned stopped-Editor synthetic UI, TMP fit, programmatic color contrast and screenshots. Does not prove real input, optical readability, task mechanics, media playback, historical credit restoration or wearer acceptance." }, Formatting.Indented));
            return "V4_PART_FEEDBACK_LAYOUT_" + status + " assertions=" + assertions + " captures=" + captures.Count;
        }
    }
}
#endif
