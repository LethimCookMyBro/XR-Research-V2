#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oculus.Interaction.Input;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchRecoveryTemporalChecks
    {
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

        public static string Run(string directory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Requires stopped Play Mode without a running XR subsystem.");
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            string evidence = Path.Combine(directory, "recovery-temporal.json");
            if (File.Exists(evidence)) throw new IOException("Evidence already exists: " + evidence);
            var caller = SceneManager.GetActiveScene();
            bool dirty = caller.isDirty;
            int previews = EditorSceneManager.previewSceneCount;
            var colors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var rows = new List<object>();
            var frames = new List<object>();
            int failures = 0;
            void Check(bool pass, string name, object observed)
            { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            try
            {
                using (var fixture = new ResearchTutorialAcceptanceChecks.Fixture(directory, 0))
                {
                    var controller = fixture.controller;
                    var view = controller.view;
                    view.enabled = false;
                    view.transform.SetParent(null, true);
                    view.gameObject.SetActive(true);
                    fixture.Present();
                    var camera = fixture.head.GetComponent<Camera>();
                    camera.enabled = false;
                    camera.scene = view.gameObject.scene;
                    camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(view.gameObject.scene);
                    var initialPosition = view.mainPanel.transform.position;
                    var initialRotation = view.mainPanel.transform.rotation;
                    var initialHead = fixture.head.rotation;
                    long epoch = controller.TutorialInstructionEpoch;
                    bool recoveryInitially = view.RecoveryCanvas.gameObject.activeSelf;

                    void Frame(string label, float seconds)
                    {
                        view.RefreshRecoveryUtility();
                        Canvas.ForceUpdateCanvases();
                        foreach (var text in view.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                        string path = Path.Combine(directory, "frame-" + frames.Count.ToString("D3") + ".png");
                        Capture(camera, path);
                        frames.Add(new { label, input_time_seconds = seconds, observed_editor_time = EditorApplication.timeSinceStartup,
                            path, readable = view.mainPanel.IsReadableFromHead, recovery = view.RecoveryCanvas.gameObject.activeSelf,
                            epoch = controller.TutorialInstructionEpoch, armed = controller.TutorialArmedFact,
                            completed = controller.TutorialCompletedFactCount, panel = view.mainPanel.Pose() });
                    }

                    Frame("instruction_presented", 0);
                    fixture.head.rotation = initialHead * Quaternion.Euler(45, 85, 0);
                    controller.TutorialPromptShown();
                    Frame("look_down_and_away_to_aim", .15f);
                    Check(!view.mainPanel.IsReadableFromHead && !view.RecoveryCanvas.gameObject.activeSelf,
                        "normal gaze away does not open recovery", new { readable = view.mainPanel.IsReadableFromHead, popup = view.RecoveryCanvas.gameObject.activeSelf });
                    Check(controller.TutorialInstructionEpoch == epoch && controller.TutorialArmedFact == "teleport",
                        "gaze away preserves the armed instruction", new { before = epoch, after = controller.TutorialInstructionEpoch, controller.TutorialArmedFact });
                    fixture.head.rotation = initialHead;
                    controller.TutorialPromptShown();
                    Frame("return_to_instruction", .3f);
                    Check(controller.TutorialInstructionEpoch == epoch && controller.TutorialCompletedFactCount == 0,
                        "look back does not restart or credit the skill", new { epoch = controller.TutorialInstructionEpoch, completed = controller.TutorialCompletedFactCount });
                    Check(view.mainPanel.transform.position == initialPosition && view.mainPanel.transform.rotation == initialRotation,
                        "ordinary head turn does not move the panel", view.mainPanel.Pose());

                    float low = 0, high = 100;
                    for (int i = 0; i < 16; i++)
                    {
                        float middle = (low + high) / 2;
                        fixture.head.rotation = initialHead * Quaternion.Euler(0, middle, 0);
                        if (view.mainPanel.IsReadableFromHead) low = middle; else high = middle;
                    }
                    int toggles = 0;
                    bool prior = view.RecoveryCanvas.gameObject.activeSelf;
                    for (int i = 0; i < 12; i++)
                    {
                        fixture.head.rotation = initialHead * Quaternion.Euler(0, low + (i % 2 == 0 ? -.2f : .2f), 0);
                        view.RefreshRecoveryUtility();
                        bool current = view.RecoveryCanvas.gameObject.activeSelf;
                        if (current != prior) toggles++;
                        prior = current;
                        Frame("viewport_edge_" + i, .4f + i / 90f);
                    }
                    Check(toggles == 0 && !view.RecoveryCanvas.gameObject.activeSelf,
                        "viewport edge does not toggle automatic recovery", new { toggles, boundary_degrees = low, recoveryInitially });
                    fixture.head.rotation = initialHead;
                    fixture.Present();
                    var evt = new Oculus.Interaction.Locomotion.LocomotionEvent(98761007,
                        fixture.head.position + fixture.head.forward,
                        Oculus.Interaction.Locomotion.LocomotionEvent.TranslationType.Absolute);
                    fixture.Requested(evt);
                    fixture.head.rotation = initialHead * Quaternion.Euler(45, 85, 0);
                    fixture.Completed(evt);
                    Frame("teleport_committed_off_view", .7f);
                    int completed = controller.TutorialCompletedFactCount;
                    fixture.Completed(evt);
                    Check(completed == 1 && controller.TutorialCompletedFactCount == 1,
                        "off-view committed teleport gets one credit only", new { completed, after_duplicate = controller.TutorialCompletedFactCount });

                    fixture.head.rotation = initialHead;
                    var completedFrame = typeof(ResearchLocomotionEvents).GetField("teleportCompletedFrame", Hidden);
                    if (completedFrame != null) completedFrame.SetValue(fixture.locomotion, -1);
                    view.mainPanel.Recenter(false, false);
                    var input = (RecoveryControllerFixture)fixture.guard.controllerReference;
                    foreach (string held in new[] { "aim", "trigger", "grip", "button" })
                    {
                        var start = view.mainPanel.transform.position;
                        input.value.Clear();
                        if (held == "aim") input.value.SetAxis2D(ControllerAxis2DUsage.Primary2DAxis, Vector2.up);
                        else if (held == "trigger") input.value.SetAxis1D(ControllerAxis1DUsage.Trigger, 1);
                        else if (held == "grip") input.value.SetAxis1D(ControllerAxis1DUsage.Grip, 1);
                        else input.value.SetButton(ControllerButtonUsage.PrimaryButton, true);
                        fixture.head.position += Vector3.right * .25f;
                        view.mainPanel.Recenter(false, false);
                        Frame(held + "_held", 1 + frames.Count / 90f);
                        Check(view.mainPanel.transform.position == start,
                            held + " defers placement without stealing pose", new { start = new[] { start.x, start.y, start.z }, after = view.mainPanel.Pose(), view.mainPanel.RecenterPending });
                        input.value.Clear();
                        Settle(view.mainPanel);
                        Frame(held + "_released", 1 + frames.Count / 90f);
                        Check(view.mainPanel.IsReadableFromHead && !view.mainPanel.RecenterPending,
                            held + " release applies pending placement", new { readable = view.mainPanel.IsReadableFromHead, view.mainPanel.RecenterPending });
                    }

                    var objectHeldPosition = view.mainPanel.transform.position;
                    fixture.Held(true);
                    fixture.head.position += Vector3.right * .25f;
                    view.mainPanel.Recenter(false, false);
                    Frame("object_held", 1.5f);
                    Check(view.mainPanel.transform.position == objectHeldPosition,
                        "held object defers panel pose", view.mainPanel.Pose());
                    fixture.Held(false);
                    Settle(view.mainPanel);
                    Check(view.mainPanel.IsReadableFromHead && !view.mainPanel.RecenterPending,
                        "object release applies pending placement", view.mainPanel.Pose());

                    var heldAction = view.GetComponentsInChildren<ResearchUIAction>(true).First(a => a.command == "pause" && a.gameObject.activeInHierarchy);
                    BindAction(heldAction);
                    var pointer = new PointerEventData(EventSystem.current) { pointerId = 871 };
                    var pressed = ExecuteEvents.ExecuteHierarchy(heldAction.GetComponentInChildren<TMP_Text>().gameObject,
                        pointer, ExecuteEvents.pointerDownHandler);
                    var pressPosition = view.mainPanel.transform.position;
                    fixture.head.position += Vector3.right * .25f;
                    view.mainPanel.Recenter(false, false);
                    Frame("ui_pointer_down", 1.6f);
                    Check(view.mainPanel.transform.position == pressPosition,
                        "UI pointer down defers panel pose", view.mainPanel.Pose());
                    ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerUpHandler);
                    Settle(view.mainPanel);
                    Check(view.mainPanel.IsReadableFromHead && !view.mainPanel.RecenterPending,
                        "UI pointer release applies pending placement", view.mainPanel.Pose());

                    typeof(ResearchSessionFlowController).GetProperty("ReferenceOpen").GetSetMethod(true).Invoke(controller, new object[] { true });
                    view.RefreshReference();
                    var scroll = (UnityEngine.UI.ScrollRect)typeof(ResearchFlowView).GetField("scroll", Hidden).GetValue(view);
                    var body = (TMP_Text)typeof(ResearchFlowView).GetField("referenceBody", Hidden).GetValue(view);
                    view.referencePanel.GetComponent<Canvas>().worldCamera = camera;
                    var moduleRoot = new GameObject("Recovery_UI_Module_Fixture");
                    moduleRoot.transform.SetParent(fixture.root.transform);
                    var events = moduleRoot.AddComponent<EventSystem>();
                    var module = moduleRoot.AddComponent<Oculus.Interaction.PointableCanvasModule>();
                    typeof(BaseInputModule).GetField("m_EventSystem", Hidden).SetValue(module, events);
                    var readerPointer = new PointerEventData(events) { pointerId = 872, button = PointerEventData.InputButton.Left,
                        position = camera.WorldToScreenPoint(scroll.viewport.position), delta = Vector2.up * 120,
                        useDragThreshold = false, eligibleForClick = true };
                    readerPointer.pressPosition = readerPointer.position;
                    readerPointer.pointerPressRaycast = new RaycastResult { gameObject = body.gameObject,
                        module = view.referencePanel.GetComponent<UnityEngine.UI.GraphicRaycaster>() };
                    readerPointer.pointerPress = ExecuteEvents.ExecuteHierarchy(body.gameObject, readerPointer, ExecuteEvents.pointerDownHandler);
                    readerPointer.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(body.gameObject);
                    ExecuteEvents.Execute(readerPointer.pointerDrag, readerPointer, ExecuteEvents.initializePotentialDrag);
                    readerPointer.position += readerPointer.delta;
                    typeof(Oculus.Interaction.PointableCanvasModule).GetMethod("ProcessDrag", Hidden).Invoke(module, new object[] { readerPointer });
                    var readerPosition = view.mainPanel.transform.position;
                    fixture.head.position += Vector3.right * .25f;
                    view.mainPanel.Recenter(false, false);
                    Frame("reader_native_module_drag", 1.65f);
                    Check(readerPointer.dragging && view.mainPanel.RecenterPending && view.mainPanel.transform.position == readerPosition,
                        "production canvas module reader drag retains pose until endDrag", new { readerPointer.dragging,
                            press_owner = readerPointer.pointerPress ? readerPointer.pointerPress.name : null,
                            drag_owner = readerPointer.pointerDrag ? readerPointer.pointerDrag.name : null,
                            view.mainPanel.RecenterPending, pose = view.mainPanel.Pose() });
                    ExecuteEvents.Execute(readerPointer.pointerPress, readerPointer, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(readerPointer.pointerDrag, readerPointer, ExecuteEvents.endDragHandler);
                    readerPointer.dragging = false;
                    Settle(view.mainPanel);
                    Check(view.mainPanel.IsReadableFromHead && !view.mainPanel.RecenterPending,
                        "production reader endDrag releases pending placement", view.mainPanel.Pose());
                    typeof(ResearchSessionFlowController).GetProperty("ReferenceOpen").GetSetMethod(true).Invoke(controller, new object[] { false });
                    view.RefreshReference();

                    var drag = view.mainPanel.GetComponentInChildren<ResearchDragHandle>(true);
                    view.mainPanel.GetComponent<Canvas>().worldCamera = camera;
                    pointer.position = camera.WorldToScreenPoint(drag.transform.position);
                    pointer.pointerCurrentRaycast = new RaycastResult { gameObject = drag.gameObject,
                        module = view.mainPanel.GetComponent<UnityEngine.UI.GraphicRaycaster>() };
                    pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
                    ExecuteEvents.Execute(drag.gameObject, pointer, ExecuteEvents.beginDragHandler);
                    var dragPosition = view.mainPanel.transform.position;
                    var dragRotation = view.mainPanel.transform.rotation;
                    fixture.head.position += Vector3.right * .2f;
                    fixture.head.rotation = initialHead * Quaternion.Euler(0, 30, 0);
                    view.OnSpatialDiscontinuity("controlled_drag_and_head_movement");
                    Settle(view.mainPanel);
                    Frame("panel_drag_with_head_movement", 1.7f);
                    Check(drag.IsDragging && view.mainPanel.transform.position == dragPosition && view.mainPanel.transform.rotation == dragRotation,
                        "drag owns pose while head moves", new { drag.IsDragging, pose = view.mainPanel.Pose() });
                    ExecuteEvents.Execute(drag.gameObject, pointer, ExecuteEvents.endDragHandler);
                    Settle(view.mainPanel);
                    Frame("panel_drag_release", 1.8f);
                    Check(!drag.IsDragging && !view.mainPanel.RecenterPending && view.mainPanel.IsReadableFromHead,
                        "drag release settles without a competing writer", new { drag.IsDragging, view.mainPanel.RecenterPending, readable = view.mainPanel.IsReadableFromHead });

                    fixture.head.rotation = initialHead * Quaternion.Euler(0, 180, 0);
                    var menuInput = (RecoveryControllerFixture)controller.guards.First(g => g.side == Handedness.Left).controllerReference;
                    menuInput.value.SetButton(ControllerButtonUsage.MenuButton, true);
                    fixture.Tick();
                    Frame("app_menu_pauses_with_main_behind", 2);
                    Check(controller.Flow.Paused && controller.PauseKind == "participant",
                        "existing app Menu pauses even with main out of view", new { controller.Flow.Paused, controller.PauseKind });
                    Check(!view.mainPanel.GetComponentsInChildren<ResearchUIAction>(true)
                        .Any(a => (a.command == "panel_recovery" || a.command == "help_request") && a.gameObject.activeInHierarchy),
                        "eligible Pause has no secondary ray targets", new { controller.Flow.Paused });
                    menuInput.value.Clear(); fixture.Tick();
                    fixture.head.rotation = initialHead * Quaternion.Euler(0, 180, 0);
                    menuInput.value.SetButton(ControllerButtonUsage.MenuButton, true); fixture.Tick();
                    menuInput.value.Clear(); fixture.Tick();
                    Settle(view.mainPanel);
                    Frame("explicit_paused_menu_recovery", 2.1f);
                    Check(view.mainPanel.IsReadableFromHead && controller.Flow.Paused,
                        "synthetic existing Menu input restores offview panel without resuming", new { readable = view.mainPanel.IsReadableFromHead, controller.Flow.Paused });
                    if (controller.Flow.Paused) controller.Command("session_resume", "Controller");
                    Frame("participant_resumes_tutorial", 2.2f);
                    Check(!controller.Flow.Paused && controller.TutorialCompletedFactCount == 1 && !view.RecoveryCanvas.gameObject.activeSelf,
                        "resume retains credited skill and closes recovery fallback", new { controller.Flow.Paused, controller.TutorialCompletedFactCount, recovery = view.RecoveryCanvas.gameObject.activeSelf });

                    foreach (var task in new[] { ResearchFlowState.Task1Active, ResearchFlowState.Task2Active })
                    {
                        typeof(ResearchSessionFlowController).GetProperty("Flow").GetSetMethod(true).Invoke(controller,
                            new object[] { new ResearchSessionFlow(TaskOrder.AB, task, 5) });
                        view.Refresh();
                        string session = controller.Record.session_id, run = controller.Record.run_id;
                        string choice = controller.ReferenceSection;
                        bool open = controller.ReferenceOpen;
                        string before = JsonConvert.SerializeObject(controller.CaptureTaskState());
                        fixture.head.rotation = initialHead * Quaternion.Euler(0, 180, 0);
                        view.RefreshRecoveryUtility();
                        bool command = view.LocalCommand("panel_recovery", "Controller");
                        Check(command && controller.ReferenceOpen == open && controller.ReferenceSection == choice
                            && controller.Record.session_id == session && controller.Record.run_id == run
                            && JsonConvert.SerializeObject(controller.CaptureTaskState()) == before,
                            task + " recovery preserves identity, chosen information and criteria", new { command, controller.ReferenceOpen, controller.ReferenceSection, session, run });
                        Frame(task + "_neutral_recovery", 2.3f);
                    }
                }
            }
            finally
            {
                foreach (var color in colors) if (color.Key) color.Key.HoverColor = color.Value;
            }
            Check(SceneManager.GetActiveScene() == caller && caller.isDirty == dirty && EditorSceneManager.previewSceneCount == previews,
                "caller scene remains unchanged", new { caller.path, dirty, current_dirty = caller.isDirty });
            string summary = "RECOVERY_TEMPORAL_" + (failures == 0 ? "PASS" : "FAIL") + " " + (rows.Count - failures) + "/" + rows.Count + " failures=" + failures;
            File.WriteAllText(evidence, JsonConvert.SerializeObject(new { summary, rows, frames,
                scope = "Production ResearchFlow callbacks, Unity ExecuteEvents hierarchy dispatch and unchanged-head Camera.Render in an isolated synthetic Editor sequence. Tracked-neutral controller input and later-frame placement arguments are fixture inputs. Input timestamps are scripted, not actual elapsed wearer timings. Mono OnEnable is explicitly delivered if stopped EditMode does not deliver it. No SDK ray acquisition, native Quest or physical acceptance.", hardware_verified = false }, Formatting.Indented));
            return summary + "; " + evidence;
        }

        public static string BeginTimeline(string directory)
        {
            if (!ResearchFloorPolicy.EditorFixtureAllowed || !EditorApplication.isPlaying
                || SceneManager.GetActiveScene().path != "Assets/VRMaintenanceResearch/ResearchFlow/Tests/Editor/RecoveryTemporalPlayFixture.unity")
                throw new InvalidOperationException("Requires isolated fixture Play Mode without a running XR subsystem.");
            directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            string evidence = Path.Combine(directory, "timeline.json"), marker = Path.Combine(directory, "timeline.started");
            if (File.Exists(marker)) return "TIMELINE_ALREADY_STARTED; " + evidence;
            using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream)) writer.Write(DateTime.UtcNow.ToString("O"));
            var caller = SceneManager.GetActiveScene(); bool dirty = caller.isDirty; int previews = EditorSceneManager.previewSceneCount;
            var colors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var rows = new List<object>(); var frames = new List<object>(); int failures = 0, next = 0, updates = 0;
            double start = EditorApplication.timeSinceStartup, lastCapture = -1, lastStep = 0;
            float lastStepGame = Time.unscaledTime; string stage = "initializing", error = null;
            ResearchTutorialAcceptanceChecks.Fixture f = null; EditorApplication.CallbackFunction update = null;
            void Check(bool pass, string name, object observed) { rows.Add(new { name, pass, observed, editor_time = EditorApplication.timeSinceStartup, frame = Time.frameCount, unscaled_time = Time.unscaledTime }); if (!pass) failures++; }
            void Finish()
            {
                EditorApplication.update -= update;
                try { f?.Dispose(); } catch (Exception ex) { error = (error ?? "") + "\nDispose: " + ex; failures++; }
                foreach (var color in colors) if (color.Key) color.Key.HoverColor = color.Value;
                Check(SceneManager.GetActiveScene() == caller && caller.isDirty == dirty && EditorSceneManager.previewSceneCount == previews, "caller scene and preview count preserved", new { caller.path, dirty, current_dirty = caller.isDirty });
                string summary = "RECOVERY_TIMELINE_" + (failures == 0 ? "PASS" : "FAIL") + " " + (rows.Count - failures) + "/" + rows.Count;
                File.WriteAllText(evidence, JsonConvert.SerializeObject(new { summary, error, updates, rows, frames,
                    scope = "Continuous EditorApplication.update, production callbacks and Camera.Render at original fixture head/FOV. Scripted synthetic head/controller inputs; Editor fixture placement injects XR readiness and Settle may use frame+1. Native observed game clocks distinguish isolated Play Mode from stopped EditMode. Explicit readiness-gate probes are synthetic. Actual observed editor/frame/unscaled clocks are recorded, not substituted. No SDK ray acquisition, Quest/HMD or performance speedup claim.", hardware_verified = false }, Formatting.Indented));
                Debug.Log(summary + "; " + evidence);
            }
            try
            {
                f = new ResearchTutorialAcceptanceChecks.Fixture(directory, 0);
                var c = f.controller; var v = c.view; var panel = v.mainPanel;
                v.enabled = false; v.transform.SetParent(null, true); v.gameObject.SetActive(true); f.Present();
                var camera = f.head.GetComponent<Camera>(); camera.enabled = false; camera.scene = v.gameObject.scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(v.gameObject.scene);
                var facing = f.head.rotation; var pose = panel.transform.position; var rotation = panel.transform.rotation;
                long epoch = c.TutorialInstructionEpoch; int idleCalls = 0, idleUpdates = 0; float idleTime = 0, edgeTime = 0;
                var input = (RecoveryControllerFixture)f.guard.controllerReference;
                var menu = (RecoveryControllerFixture)c.guards.First(g => g.side == Handedness.Left).controllerReference;
                var pointer = new PointerEventData(EventSystem.current) { pointerId = 873 }; GameObject press = null;
                ResearchDragHandle drag = null; PointerEventData reader = null; Oculus.Interaction.PointableCanvasModule module = null;
                var e = new Oculus.Interaction.Locomotion.LocomotionEvent(98761008, f.head.position + f.head.forward, Oculus.Interaction.Locomotion.LocomotionEvent.TranslationType.Absolute);
                var pending = typeof(ResearchPanelMotion).GetMethod("TryApplyPendingRecenter", Hidden);
                var steps = new List<(double at, string label, Action action)> {
                    (0, "instruction_presented", () => Check(c.TutorialArmedFact == "teleport" && panel.IsReadableFromHead, "initial instruction presented", new { c.TutorialArmedFact })),
                    (.20, "look_down_aim_hold", () => { pose = panel.transform.position; input.value.SetAxis2D(ControllerAxis2DUsage.Primary2DAxis, Vector2.up); f.head.rotation = facing * Quaternion.Euler(45, 85, 0); f.Requested(e); }),
                    (.45, "teleport_committed_while_aiming", () => { f.Completed(e); f.Completed(e); v.Refresh(); Check(c.TutorialCompletedFactCount == 1 && panel.transform.position == pose, "one credit and new fact placement deferred while aiming", new { c.TutorialCompletedFactCount, panel.RecenterPending, c.TutorialFact }); }),
                    (.70, "aim_release_still_looking_down", () => { input.value.Clear(); Settle(panel); Check(panel.RecenterPending && panel.transform.position == pose, "viewport-only rejection retains the event request without moving UI", new { panel.RecenterPending, panel.LastRejection }); }),
                    (.80, "look_up_after_teleport", () => { f.head.rotation = facing; Settle(panel); c.TutorialPromptShown(); Check(panel.IsReadableFromHead && !panel.RecenterPending && c.TutorialCompletedFactCount == 1, "look up completes the queued event once", panel.Pose()); epoch = c.TutorialInstructionEpoch; pose = panel.transform.position; rotation = panel.transform.rotation; }),
                    (.95, "look_at_object", () => { f.head.rotation = facing * Quaternion.Euler(45, -80, 0); c.TutorialPromptShown(); Check(!v.RecoveryCanvas.gameObject.activeSelf, "ordinary gaze away never opens recovery", new { c.TutorialInstructionEpoch }); }),
                    (1.15, "return_to_instruction", () => { f.head.rotation = facing; c.TutorialPromptShown(); Check(c.TutorialInstructionEpoch == epoch && c.TutorialCompletedFactCount == 1 && panel.transform.position == pose && panel.transform.rotation == rotation, "gaze return preserves instruction credit and pose", panel.Pose()); }),
                    (1.35, "hierarchy_ui_press", () => { var a = v.GetComponentsInChildren<ResearchUIAction>(true).First(x => x.command == "pause" && x.gameObject.activeInHierarchy); BindAction(a); press = ExecuteEvents.ExecuteHierarchy(a.GetComponentInChildren<TMP_Text>().gameObject, pointer, ExecuteEvents.pointerDownHandler); pose = panel.transform.position; f.head.position += Vector3.right * .15f; panel.Recenter(false, false); Check(press && panel.transform.position == pose && panel.RecenterPending, "real hierarchy UI press owns pose", panel.Pose()); }),
                    (1.55, "hierarchy_ui_release", () => { ExecuteEvents.Execute(press, pointer, ExecuteEvents.pointerUpHandler); Settle(panel); Check(panel.IsReadableFromHead && !panel.RecenterPending, "UI release settles", panel.Pose()); }),
                    (1.75, "reader_native_process_drag", () => {
                        typeof(ResearchSessionFlowController).GetProperty("ReferenceOpen").GetSetMethod(true).Invoke(c, new object[] { true }); v.RefreshReference();
                        var scroll = (UnityEngine.UI.ScrollRect)typeof(ResearchFlowView).GetField("scroll", Hidden).GetValue(v);
                        var body = (TMP_Text)typeof(ResearchFlowView).GetField("referenceBody", Hidden).GetValue(v);
                        v.referencePanel.GetComponent<Canvas>().worldCamera = camera;
                        var go = new GameObject("Timeline_PointableCanvasModule"); go.transform.SetParent(f.root.transform);
                        var events = go.AddComponent<EventSystem>(); module = go.AddComponent<Oculus.Interaction.PointableCanvasModule>();
                        typeof(BaseInputModule).GetField("m_EventSystem", Hidden).SetValue(module, events);
                        reader = new PointerEventData(events) { pointerId = 874, button = PointerEventData.InputButton.Left, position = camera.WorldToScreenPoint(scroll.viewport.position), delta = Vector2.up * 120, useDragThreshold = false, eligibleForClick = true };
                        reader.pressPosition = reader.position; reader.pointerPressRaycast = new RaycastResult { gameObject = body.gameObject, module = v.referencePanel.GetComponent<UnityEngine.UI.GraphicRaycaster>() };
                        reader.pointerPress = ExecuteEvents.ExecuteHierarchy(body.gameObject, reader, ExecuteEvents.pointerDownHandler); reader.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(body.gameObject);
                        ExecuteEvents.Execute(reader.pointerDrag, reader, ExecuteEvents.initializePotentialDrag); var contentBefore = scroll.content.anchoredPosition; reader.position += reader.delta;
                        typeof(Oculus.Interaction.PointableCanvasModule).GetMethod("ProcessDrag", Hidden).Invoke(module, new object[] { reader });
                        reader.position += reader.delta; typeof(Oculus.Interaction.PointableCanvasModule).GetMethod("ProcessDrag", Hidden).Invoke(module, new object[] { reader }); Check(reader.dragging && reader.pointerPress == reader.pointerDrag, "production ProcessDrag owns the reader input surface", new { content_has_scroll_range = scroll.content.rect.height > scroll.viewport.rect.height, before = new[] { contentBefore.x, contentBefore.y }, after = new[] { scroll.content.anchoredPosition.x, scroll.content.anchoredPosition.y }, long_document_movement_verified_separately = "ResearchFocusedReaderInputChecks" });
                        pose = panel.transform.position; f.head.position += Vector3.right * .15f; panel.Recenter(false, false);
                        Check(reader.dragging && panel.RecenterPending && panel.transform.position == pose && reader.pointerPress == reader.pointerDrag, "production ProcessDrag cancellation path retains scroll owner", new { reader.dragging, press_owner = reader.pointerPress ? reader.pointerPress.name : null, drag_owner = reader.pointerDrag ? reader.pointerDrag.name : null }); }),
                    (2.0, "reader_end_drag", () => { ExecuteEvents.Execute(reader.pointerPress, reader, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(reader.pointerDrag, reader, ExecuteEvents.endDragHandler); reader.dragging = false; Settle(panel); Check(!panel.RecenterPending && panel.IsReadableFromHead, "reader endDrag releases pose", panel.Pose()); typeof(ResearchSessionFlowController).GetProperty("ReferenceOpen").GetSetMethod(true).Invoke(c, new object[] { false }); v.RefreshReference(); }),
                    (2.2, "panel_drag_head_movement", () => { drag = panel.GetComponentInChildren<ResearchDragHandle>(true); panel.GetComponent<Canvas>().worldCamera = camera; pointer.position = camera.WorldToScreenPoint(drag.transform.position); pointer.pointerCurrentRaycast = new RaycastResult { gameObject = drag.gameObject, module = panel.GetComponent<UnityEngine.UI.GraphicRaycaster>() }; pointer.pointerPressRaycast = pointer.pointerCurrentRaycast; ExecuteEvents.Execute(drag.gameObject, pointer, ExecuteEvents.beginDragHandler); pose = panel.transform.position; rotation = panel.transform.rotation; f.head.position += Vector3.right * .15f; f.head.rotation *= Quaternion.Euler(0, 30, 0); v.OnSpatialDiscontinuity("synthetic_timeline_drag"); }),
                    (2.4, "panel_drag_release", () => { Check(drag.IsDragging && panel.transform.position == pose && panel.transform.rotation == rotation, "held drag does not steal pose", panel.Pose()); ExecuteEvents.Execute(drag.gameObject, pointer, ExecuteEvents.endDragHandler); Settle(panel); Check(!panel.RecenterPending && panel.IsReadableFromHead, "panel drag release settles", panel.Pose()); }),
                    (2.6, "menu_pause_main_lost", () => { f.head.rotation *= Quaternion.Euler(0, 180, 0); menu.value.SetButton(ControllerButtonUsage.MenuButton, true); f.Tick(); menu.value.Clear(); Settle(panel); f.head.rotation *= Quaternion.Euler(0, 180, 0); Check(c.Flow.Paused && c.PauseKind == "participant", "existing left Menu pauses offview", new { c.Flow.Paused, c.PauseKind }); edgeTime = Time.unscaledTime; v.RefreshRecoveryUtility(); Check(!v.RecoveryCanvas.gameObject.activeSelf, "offview hysteresis does not open at zero elapsed", new { edgeTime, now = Time.unscaledTime }); }),
                    (2.8, "paused_no_secondary_fallback", () => { Check(Time.unscaledTime - edgeTime >= .08f && !v.RecoveryCanvas.gameObject.activeSelf, "eligible Pause does not create a secondary fallback", new { edgeTime, now = Time.unscaledTime });
                        menu.value.Clear(); f.Tick(); input.value.SetAxis2D(ControllerAxis2DUsage.Primary2DAxis, Vector2.up); menu.value.SetButton(ControllerButtonUsage.MenuButton, true); f.Tick(); input.value.Clear(); menu.value.Clear(); f.Tick();
                        foreach (var readiness in new[] { new[] { false, true, true }, new[] { true, false, false }, new[] { true, true, false } }) { pending.Invoke(panel, new object[] { readiness[0], readiness[1], readiness[2], Time.frameCount + 1 }); Check(panel.RecenterPending && !v.RecoveryCanvas.gameObject.activeSelf, "explicit Menu request waits for valid pose/presence without secondary UI", new { readiness, panel.RecenterPending }); } input.value.SetAxis2D(ControllerAxis2DUsage.Primary2DAxis, Vector2.up); }),
                    (3.0, "explicit_paused_menu_recovery", () => { input.value.Clear(); menu.value.Clear(); f.Tick(); menu.value.SetButton(ControllerButtonUsage.MenuButton, true); f.Tick(); menu.value.Clear(); f.Tick(); Settle(panel); Check(c.Flow.Paused && panel.IsReadableFromHead && !v.RecoveryCanvas.gameObject.activeSelf, "restored readiness explicit Menu recovers without resume or fallback UI", panel.Pose()); }),
                    (3.2, "resume_retained_credit", () => { c.Command("session_resume", "Controller"); Check(!c.Flow.Paused && c.TutorialCompletedFactCount == 1 && !v.RecoveryCanvas.gameObject.activeSelf, "resume keeps one credit and hides recovery", new { c.Flow.Paused, c.TutorialCompletedFactCount }); idleCalls = panel.PlacementCalls; idleUpdates = updates; idleTime = Time.unscaledTime; }),
                    (3.8, "bounded_idle_queries", () => { int count = panel.PlacementCalls - idleCalls; Check(count <= Mathf.CeilToInt((Time.unscaledTime - idleTime) / .1f) + 2 && updates > idleUpdates, "idle query count bounded by observed elapsed clock", new { count, update_count = updates - idleUpdates, elapsed = Time.unscaledTime - idleTime }); }),
                    (4.0, "real_collider_entry", () => { var obstacle = new GameObject("TimelineObstacle"); obstacle.transform.SetParent(f.root.transform); obstacle.transform.position = panel.transform.position; obstacle.AddComponent<BoxCollider>().size = Vector3.one * .2f; obstacle.transform.SetParent(null, true); SceneManager.MoveGameObjectToScene(obstacle, caller); obstacle.SetActive(true); Physics.SyncTransforms(); Check(!panel.PlacementClear(panel.transform.position, panel.transform.rotation) && panel.LastRejection.StartsWith("collider:"), "real collider blocks placement", new { panel.LastRejection, panel.PlacementCalls }); UnityEngine.Object.DestroyImmediate(obstacle); Physics.SyncTransforms(); }),
                    (4.25, "clear_after_obstacle_exit", () => { panel.Recenter(false, false); Settle(panel); Check(panel.IsReadableFromHead && !panel.PlacementBlocked, "readable after obstacle removal", panel.Pose()); }),
                    (4.5, "final_retained_state", () => Check(c.TutorialCompletedFactCount == 1 && !c.Flow.Paused, "final credit and running state retained", new { c.TutorialCompletedFactCount, c.Flow.Paused }))
                };
                update = () => {
                    try {
                        if (!ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("XR subsystem started during isolated timeline.");
                        double elapsed = EditorApplication.timeSinceStartup - start; updates++; bool advanced = false;
                        double interval = next == 0 ? 0 : next < steps.Count ? steps[next].at - steps[next - 1].at : 0;
                        if (next < steps.Count && elapsed - lastStep >= interval && Time.unscaledTime - lastStepGame >= interval) {
                            advanced = true; stage = steps[next].label; steps[next++].action();
                            lastStep = elapsed; lastStepGame = Time.unscaledTime;
                        }
                        foreach (var p in v.GetComponentsInChildren<ResearchPanelMotion>(false)) typeof(ResearchPanelMotion).GetMethod("LateUpdate", Hidden).Invoke(p, null);
                        v.RefreshRecoveryUtility(); Canvas.ForceUpdateCanvases();
                        if (advanced || elapsed - lastCapture >= .08) {
                            foreach (var text in v.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                            string path = Path.Combine(directory, "timeline-" + frames.Count.ToString("D3") + ".png"); Capture(camera, path); lastCapture = elapsed;
                            frames.Add(new { stage, path, elapsed_editor_seconds = elapsed, editor_time = EditorApplication.timeSinceStartup, frame = Time.frameCount, unscaled_time = Time.unscaledTime, head_position = new[] { f.head.position.x, f.head.position.y, f.head.position.z }, head_rotation = new[] { f.head.rotation.x, f.head.rotation.y, f.head.rotation.z, f.head.rotation.w }, fov = camera.fieldOfView, panel = panel.Pose(), readable = panel.IsReadableFromHead, recovery = v.RecoveryCanvas.gameObject.activeSelf, panel.RecenterPending, panel.PlacementCalls, c.TutorialInstructionEpoch, c.TutorialCompletedFactCount });
                        }
                        if (next == steps.Count && elapsed - lastStep >= .5) { Check(frames.Count > steps.Count && updates > steps.Count, "rendered observations between milestones", new { frames = frames.Count, updates }); Finish(); }
                        else EditorApplication.QueuePlayerLoopUpdate();
                    } catch (Exception ex) { error = ex.ToString(); Check(false, "timeline execution error", error); Finish(); }
                };
                start = EditorApplication.timeSinceStartup; EditorApplication.update += update; EditorApplication.QueuePlayerLoopUpdate();
                return "TIMELINE_STARTED; " + evidence;
            }
            catch (Exception ex) { error = ex.ToString(); Check(false, "timeline initialization error", error); Finish(); return "TIMELINE_FAILED; " + evidence; }
        }

        static void Capture(Camera camera, string path)
        {
            const int width = 960, height = 960;
            var beforeTarget = camera.targetTexture;
            var beforeActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.13f, .17f, .2f);
                camera.targetTexture = target;
                target.Create(); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = beforeTarget; RenderTexture.active = beforeActive;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        static void BindAction(ResearchUIAction action)
        {
            if (typeof(ResearchUIAction).GetField("button", Hidden).GetValue(action) == null)
                typeof(ResearchUIAction).GetMethod("OnEnable", Hidden).Invoke(action, null);
        }

        static void Settle(ResearchPanelMotion panel)
        {
            typeof(ResearchPanelMotion).GetMethod("LateUpdate", Hidden).Invoke(panel, null);
            typeof(ResearchPanelMotion).GetMethod("TryApplyPendingRecenter", Hidden).Invoke(panel,
                new object[] { true, true, true, Time.frameCount + 1 });
        }
    }

    public sealed class RecoveryControllerFixture : MonoBehaviour, IController
    {
        public ControllerInput value;
        public Handedness side = Handedness.Right;
        public Handedness Handedness => side;
        public float Scale => 1;
        public bool IsConnected => true;
        public bool IsPoseValid => true;
        public ControllerInput ControllerInput => value;
        public event Action WhenUpdated { add { } remove { } }
        public bool TryGetPose(out Pose pose) { pose = Pose.identity; return true; }
        public bool TryGetPointerPose(out Pose pose) { pose = Pose.identity; return true; }
        public bool IsButtonUsageAnyActive(ControllerButtonUsage usage) => (value.ButtonUsageMask & usage) != 0;
        public bool IsButtonUsageAllActive(ControllerButtonUsage usage) => (value.ButtonUsageMask & usage) == usage;
    }
}
#endif
