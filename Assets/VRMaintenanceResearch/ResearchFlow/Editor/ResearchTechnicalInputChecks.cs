#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using Oculus.Interaction.Locomotion;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Synthetic InputSystem devices -> existing Meta rig only. No direct pointer dispatch or forced selection.
    public static class ResearchTechnicalInputChecks
    {
        static ResearchSessionFlowController controller;
        static IEnumerator routine;
        static double due, started;
        static readonly List<object> evidence = new List<object>();
        static ResearchTechnicalPointerProbe pointer;
        static Action detachGrabObserver;
        public static string Status { get; private set; } = "NotRun";
        public static string EvidencePath { get; private set; }
        static int previousFrameRate = -1;

        // File/bridge automation: the same legitimate researcher entry (new DEV record + StartSession) the fixture requires.
        public static string BeginWithNewDevSession(string evidenceDirectory = null)
        {
            var flow = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            if (!flow || flow.Record != null) throw new InvalidOperationException("Fresh Play Mode controller with no session required.");
            flow.StartSession(flow.Registry.CreateNext(ParticipantGroup.Dev).participant_id, ParticipantLanguage.Thai, ExperimentInputMode.TechnicalBoth, TaskOrder.AB);
            return Begin(evidenceDirectory);
        }

        public static string Begin(string evidenceDirectory = null)
        {
            if (routine != null || ResearchFlowVisualInput.Running) throw new InvalidOperationException("Stop the existing fixture first.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) throw new InvalidOperationException("Unpaused Play Mode required.");
            controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            if (!controller || controller.Record == null || !controller.Record.test_data || controller.Record.group != ParticipantGroup.Dev
                || controller.Flow?.State != ResearchFlowState.TutorialWelcome)
                throw new InvalidOperationException("Use the legitimate researcher StartSession entry with a new DEV record; TutorialWelcome required.");
            var folder = Path.GetFullPath(evidenceDirectory ?? ("Logs/QuestFinalIntegration/native-input-" +
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(folder); EvidencePath = Path.Combine(folder, "result.json");
            evidence.Clear(); started = due = EditorApplication.timeSinceStartup; Status = "Running";
            // Synthetic drag moves a fixed distance per rendered frame, so its mm error scales with frame rate
            // (32 mm at an uncapped Editor, 12.7 mm at 72). Pin Quest's 72 Hz cadence for the fixture.
            previousFrameRate = Application.targetFrameRate; Application.targetFrameRate = 72;
            ResearchFlowVisualInput.Begin(1.65f); routine = Sequence();
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayState;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            return EvidencePath;
        }

        static void Check(bool passed, string name, object detail = null)
        {
            evidence.Add(new { name, passed, detail, elapsed_seconds = EditorApplication.timeSinceStartup - started });
            if (!passed) throw new InvalidOperationException(name);
        }

        static HandsLabInputGuard Guard(int side) => controller.guards.Single(g => g.side == (side == 0 ? Handedness.Left : Handedness.Right));
        static HandGrabInteractor Grabber(int side) => Guard(side).interactionsRoot.GetComponentsInChildren<HandGrabInteractor>(true)
            .Single(g => g.name == "ControllerHandGrabInteractor");
        static void Drive(int side, Pose pose, float grip = 0, float trigger = 0, Vector2 thumbstick = default) => ResearchFlowVisualInput.SetControllerWorldPose(side, pose, grip, trigger, thumbstick);

        static JObject[] LoggedEvents()
        {
            var rows = new List<JObject>();
            using (var reader = new StreamReader(new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                for (string line; (line = reader.ReadLine()) != null;) rows.Add(JObject.Parse(line));
            return rows.ToArray();
        }

        static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
        static string HierarchyPath(Transform value)
        {
            if (!value) return null;
            string path = value.name;
            while (value.parent) { value = value.parent; path = value.name + "/" + path; }
            return path;
        }
        static object GrabDetail(int side, HandsLabObject item, HandGrabPose authoredPose)
        {
            var guard = Guard(side); var grabber = Grabber(side);
            var ray = guard.interactionsRoot.GetComponentsInChildren<RayInteractor>(true).Single(r => r.name == "ControllerRayInteractor");
            var group = guard.interactionsRoot.GetComponent<BestHoverInteractorGroup>();
            var serialized = new SerializedObject(grabber);
            var volumes = new[] { "_gripCollider", "_pinchCollider" }.Select(name => serialized.FindProperty(name).objectReferenceValue as Collider).Where(c => c).ToArray();
            var registry = HandGrabInteractable.Registry.List(grabber).ToArray();
            var input = guard.controllerReference as IController;
            return new
            {
                target = item.CaptureSnapshot(), active = item.gameObject.activeInHierarchy, grabbable_enabled = item.grabbable.enabled, body_is_kinematic = item.body.isKinematic,
                guard = new { guard.TrackingValid, guard.InteractionsAllowed, guard.WaitingForNeutral, guard.LastReason, guard.SuspensionReason, mode = guard.Mode.ToString() },
                controller_input = new { grip = input?.ControllerInput.Grip, trigger = input?.ControllerInput.Trigger },
                ui_ray = new { active = ray.isActiveAndEnabled, state = ray.State.ToString(),
                    interactable = ray.Interactable ? HierarchyPath(ray.Interactable.transform) : null, origin = V(ray.Origin), forward = V(ray.Forward) },
                priority = group.Interactors.Select((i, index) => new { index, name = (i as Component)?.name, type = i.GetType().Name }).ToArray(),
                interactor = new { grabber.name, active = grabber.isActiveAndEnabled, state = grabber.State.ToString(), grabber.HoverOnZeroStrength,
                    target = grabber.Interactable ? grabber.Interactable.name : null, grabber.Identifier, grabber.IsGrabbing,
                    movement_type = grabber.Movement?.GetType().Name, movement_stopped = grabber.Movement == null ? (bool?)null : grabber.Movement.Stopped,
                    wrist = V(grabber.WristPoint.position), palm = V(grabber.PalmPoint.position), pinch = V(grabber.PinchPoint.position),
                    types = grabber.SupportedGrabTypes.ToString(), hand_valid = grabber.HandGrabApi.Hand.IsTrackedDataValid,
                    middle_pinch = grabber.HandGrabApi.GetFingerPinchStrength(HandFinger.Middle), middle_palm = grabber.HandGrabApi.GetFingerPalmStrength(HandFinger.Middle) },
                authored_pose = new { authoredPose.name, relative = V(authoredPose.RelativePose.position), world = V(authoredPose.RelativeTo.TransformPoint(authoredPose.RelativePose.position)),
                    rotation = (authoredPose.RelativeTo.rotation * authoredPose.RelativePose.rotation).ToString("F6") },
                registry = registry.Select(h => h.name).ToArray(),
                volumes = volumes.Select(c => new { c.name, c.enabled, active = c.gameObject.activeInHierarchy, center = V(c.bounds.center), size = V(c.bounds.size) }).ToArray(),
                targets = item.GetComponentsInChildren<HandGrabInteractable>(true).Select(h => new { h.name, active = h.isActiveAndEnabled,
                    registered = registry.Contains(h), types = h.SupportedGrabTypes.ToString(), colliders_available = h.Colliders != null,
                    colliders = h.Colliders?.Where(c => c).Select(c => new { c.name, c.enabled, active = c.gameObject.activeInHierarchy, center = V(c.bounds.center), size = V(c.bounds.size),
                        overlap = volumes.Select(v => new { v.name, overlaps = Physics.ComputePenetration(v, v.transform.position, v.transform.rotation, c, c.transform.position, c.transform.rotation, out _, out _) }).ToArray() }).ToArray() }).ToArray(),
                interaction_group = guard.interactionsRoot.GetComponentsInChildren<MonoBehaviour>(true).OfType<IInteractor>()
                    .Select(i => new { name = (i as Component)?.name, state = i.State.ToString(), active = (i as Behaviour)?.isActiveAndEnabled }).ToArray(),
                ui_clearance = guard.uiClearanceColliders.Where(c => c).Select(c => new { c.name, c.enabled, active = c.gameObject.activeInHierarchy,
                    distance_from_pinch = Vector3.Distance(c.ClosestPoint(grabber.PinchPoint.position), grabber.PinchPoint.position), guard.uiClearanceMetres }).ToArray()
            };
        }

        static void CheckPointerOwnership()
        {
            var method = typeof(ResearchSessionFlowController).GetMethod("GuardForPointer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var lookup = (Func<int, HandsLabInputGuard>)method.CreateDelegate(typeof(Func<int, HandsLabInputGuard>), controller);
            Func<int, HandsLabInputGuard> original = id => controller.guards.FirstOrDefault(g => g && g.interactionsRoot &&
                g.interactionsRoot.GetComponentsInChildren<MonoBehaviour>(true)
                    .Where(b => b && b.isActiveAndEnabled && !(b is InteractorGroup)).OfType<IInteractor>().Any(i => i.Identifier == id));
            var owners = new[] { Guard(0), Guard(1) };
            var rays = owners.Select(g => g.interactionsRoot.GetComponentsInChildren<RayInteractor>(true)
                .Single(r => r.name == "ControllerRayInteractor")).ToArray();
            Check(!original(int.MinValue) && !lookup(int.MinValue), "unknown pointer has no owner");
            for (int side = 0; side < rays.Length; side++)
            {
                var ray = rays[side]; var guard = owners[side];
                Check(ray.isActiveAndEnabled && lookup(ray.Identifier) == guard && lookup(ray.Identifier) == original(ray.Identifier),
                    guard.side + " native ray ownership matches original query");
                bool enabled = ray.enabled, active = guard.interactionsRoot.activeSelf;
                try
                {
                    ray.enabled = false;
                    Check(!lookup(ray.Identifier) && !original(ray.Identifier), guard.side + " disabled ray has no owner");
                    ray.enabled = enabled; guard.interactionsRoot.SetActive(false);
                    Check(!lookup(ray.Identifier) && !original(ray.Identifier), guard.side + " inactive root has no pointer owner");
                }
                finally { guard.interactionsRoot.SetActive(active); ray.enabled = enabled; }
                Check(lookup(ray.Identifier) == guard && controller.InputSourceForPointer(ray.Identifier) == "SimulatedController",
                    guard.side + " restored ray preserves synthetic input provenance");
            }
            // Warm both paths; timings are diagnostics, never a flaky performance pass threshold.
            for (int i = 0; i < 100; i++) { original(rays[i % 2].Identifier); lookup(rays[i % 2].Identifier); }
            long beforeControl = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[4096]);
            bool allocationCounterSupported = GC.GetAllocatedBytesForCurrentThread() - beforeControl >= 4096;
            object Measure(Func<int, HandsLabInputGuard> query)
            {
                long before = GC.GetAllocatedBytesForCurrentThread(), start = System.Diagnostics.Stopwatch.GetTimestamp();
                int matched = 0;
                for (int i = 0; i < 1000; i++) if (query(rays[i % 2].Identifier) == owners[i % 2]) matched++;
                long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - start, allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(matched == 1000, "pointer microbenchmark preserves all owners");
                return new { calls = 1000, milliseconds = ticks * 1000d / System.Diagnostics.Stopwatch.Frequency,
                    allocated_bytes = allocationCounterSupported ? (long?)allocated : null };
            }
            var oldFirst = Measure(original); var newFirst = Measure(lookup);
            var newSecond = Measure(lookup); var oldSecond = Measure(original);
            evidence.Add(new { name = "pointer lookup microbenchmark", allocation_counter_supported = allocationCounterSupported,
                original_first = oldFirst, current_first = newFirst, current_second = newSecond, original_second = oldSecond,
                hardware_verified = false });
        }

        static IEnumerable<double> RayClick(string command, Action beforeTrigger = null)
        {
            var action = controller.view.GetComponentsInChildren<ResearchUIAction>(true)
                .Single(a => a.command == command && a.gameObject.activeInHierarchy);
            if (pointer) UnityEngine.Object.DestroyImmediate(pointer);
            pointer = action.gameObject.AddComponent<ResearchTechnicalPointerProbe>(); pointer.controller = controller;
            var rect = action.GetComponent<RectTransform>();
            var ray = Guard(1).interactionsRoot.GetComponentsInChildren<RayInteractor>(true).Single(r => r.name == "ControllerRayInteractor");
            object RayDetail()
            {
                var target = rect.TransformPoint(rect.rect.center);
                var input = Guard(1).controllerReference as IController;
                return new
                {
                    pointer.enters, pointer.downs, pointer.clicks, pointer.inputSource,
                    ray_active = ray.isActiveAndEnabled, state = ray.State.ToString(),
                    interactable = ray.Interactable ? HierarchyPath(ray.Interactable.transform) : null,
                    origin = V(ray.Origin), forward = V(ray.Forward), requested_target = HierarchyPath(rect), target_position = V(target),
                    aim_error_degrees = Vector3.Angle(ray.Forward, target - ray.Origin),
                    pokes = Guard(1).interactionsRoot.GetComponentsInChildren<PokeInteractor>(true)
                        .Select(p => new { p.name, state = p.State.ToString(), interactable = p.Interactable ? HierarchyPath(p.Interactable.transform) : null }).ToArray(),
                    button_active = action.gameObject.activeInHierarchy, button_interactable = action.GetComponent<UnityEngine.UI.Button>().IsInteractable(),
                    grip = input?.ControllerInput.Grip, trigger = input?.ControllerInput.Trigger,
                    interactors = Guard(1).interactionsRoot.GetComponentsInChildren<MonoBehaviour>(true).OfType<IInteractor>()
                        .Select(i => new { name = (i as Component)?.name, state = i.State.ToString(), active = (i as Behaviour)?.isActiveAndEnabled }).ToArray()
                };
            }
            // A newly attached probe will not receive Enter for an already-hovered button.
            // Leave/reacquire with real native pose input, keeping UI and the physical candidate enabled.
            var offTarget = ResearchFlowVisualInput.ControllerWorldPose(1);
            offTarget.rotation = Quaternion.AngleAxis(35, controller.view.Head.up) * offTarget.rotation;
            Drive(1, offTarget); yield return .1d;
            for (int i = 0; i < 30; i++)
            {
                var pose = ResearchFlowVisualInput.ControllerWorldPose(1);
                pose.rotation = Quaternion.FromToRotation(ray.Forward, (rect.TransformPoint(rect.rect.center) - ray.Origin).normalized) * pose.rotation;
                Drive(1, pose); yield return .05d;
            }
            Check(ray.isActiveAndEnabled && pointer.enters > 0, command + " native ray hover", RayDetail());
            if (command == "panel_recovery")
            { ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(EvidencePath), "recovery_hover.png")); yield return .1d; }
            beforeTrigger?.Invoke();
            Drive(1, ResearchFlowVisualInput.ControllerWorldPose(1), trigger: 1); yield return .2d;
            Check(pointer.downs > 0, command + " native trigger pointer down", RayDetail());
            if (command == "panel_recovery")
            { ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(EvidencePath), "recovery_pressed.png")); yield return .1d; }
            Drive(1, ResearchFlowVisualInput.ControllerWorldPose(1)); yield return .3d;
            Check(pointer.clicks > 0 && pointer.inputSource == "SimulatedController", command + " native trigger click with provenance", RayDetail());
        }

        static IEnumerator Sequence()
        {
            for (int i = 0; i < 100 && (controller.Transitioning || !controller.DeviceReady); i++) yield return .1d;
            Check(!controller.Transitioning && controller.DeviceReady, "station transition completes with both synthetic Touch poses valid");
            if (controller.Flow.Paused) controller.ResearcherResume(); // Existing researcher entry; no direct gate/Flow mutation.
            for (int i = 0; i < 50 && controller.guards.Any(g => !g.InteractionsAllowed); i++) yield return .1d;
            Check(controller.guards.All(g => g.TrackingValid && g.InteractionsAllowed && !g.WaitingForNeutral),
                "legitimate session gate and continuous neutral activate both interaction roots", ResearchFlowVisualInput.Audit());
            // Guard.Resume precedes Meta's ActiveStateTracker update; wait for both native rays.
            for (int i = 0; i < 30 && controller.guards.Any(g => !g.interactionsRoot.GetComponentsInChildren<RayInteractor>(true)
                .Single(r => r.name == "ControllerRayInteractor").isActiveAndEnabled); i++) yield return .1d;
            CheckPointerOwnership();
            yield return .2d; // Let TrackedPoseDriver consume the explicit synthetic head before placing UI.
            controller.view.RecenterAll(false);

            var action = controller.view.GetComponentsInChildren<ResearchUIAction>(true)
                .Single(a => a.command == "tutorial_start" && a.gameObject.activeInHierarchy);
            pointer = action.gameObject.AddComponent<ResearchTechnicalPointerProbe>(); pointer.controller = controller;
            var rect = action.GetComponent<RectTransform>();
            var ray = Guard(1).interactionsRoot.GetComponentsInChildren<RayInteractor>(true).Single(r => r.name == "ControllerRayInteractor");
            for (int i = 0; i < 30; i++)
            {
                var pose = ResearchFlowVisualInput.ControllerWorldPose(1);
                pose.rotation = Quaternion.FromToRotation(ray.Forward, (rect.TransformPoint(rect.rect.center) - ray.Origin).normalized) * pose.rotation;
                Drive(1, pose); yield return .05d;
            }
            Check(ray.isActiveAndEnabled && pointer.enters > 0, "native Meta controller ray reaches UI hover",
                new { pointer.enters, ray_state = ray.State.ToString(), pointer.inputSource });
            Drive(1, ResearchFlowVisualInput.ControllerWorldPose(1), trigger: 1); yield return .3d;
            Check(pointer.downs > 0, "native trigger produces UI pointer down");
            Drive(1, ResearchFlowVisualInput.ControllerWorldPose(1)); yield return .4d;
            Check(pointer.clicks > 0 && pointer.inputSource == "SimulatedController" && controller.Flow.State == ResearchFlowState.TutorialInteractions,
                "native UI click starts tutorial through the real button", new { pointer.clicks, pointer.inputSource, flow = controller.Flow.State.ToString() });

            foreach (double wait in RayClick("pause")) yield return wait;
            Check(controller.Flow.Paused && controller.guards.All(g => g.InteractionsAllowed && !g.Suspended), "pause preserves tracked native UI guards");
            Check(controller.GetComponentsInChildren<ResearchWorldInteractionGate>(true).All(g => !g.Allowed) &&
                controller.trainingObjects.All(o => !o.IsHeld && !o.grabbable.enabled) && controller.taskPorts.All(p => !p.InteractionAllowed),
                "pause blocks physical interactors, training objects and task ports");
            var pausedOrigin = controller.origin.transform.position;
            Drive(1, ResearchFlowVisualInput.ControllerWorldPose(1), grip: 1, thumbstick: Vector2.up); yield return .3d;
            Drive(1, ResearchFlowVisualInput.ControllerWorldPose(1)); yield return .2d;
            Check(controller.trainingObjects.All(o => !o.IsHeld) && Vector3.Distance(pausedOrigin, controller.origin.transform.position) < .001f &&
                !controller.locomotionInputRoots.Any(g => g.activeInHierarchy), "paused grip and teleport axis cannot affect the world");
            Check(controller.view.mainPanel.GetComponentsInChildren<ResearchUIAction>(false)
                .Select(a => a.command).Where(c => !string.IsNullOrEmpty(c)).SequenceEqual(new[] { "session_resume" }),
                "eligible Pause has one active native ray action");
            Check(!controller.view.mainPanel.GetComponentsInChildren<ResearchUIAction>(true)
                .Any(a => (a.command == "help_request" || a.command == "panel_recovery") && a.gameObject.activeInHierarchy),
                "removed Pause utilities have no active ray targets");
            Check(controller.Command("help_request", "SimulatedController"), "existing Help backend command remains accepted while paused");
            Check(controller.Flow.Paused && controller.HelpRequested && LoggedEvents().Any(e => (string)e["event"] == "HELP_REQUESTED" &&
                (string)e["input_source"] == "SimulatedController"), "Help backend command logs HELP_REQUESTED without a claimed native ray click");
            Check(controller.view.mainPanel.GetComponentsInChildren<TMPro.TMP_Text>(false).Any(t => t.text.Contains(ResearchStrings.Get("pause.safety", controller.Language))),
                "Pause retains the instruction to tell the researcher, not a delivered-alert claim");
            foreach (double wait in RayClick("session_resume")) yield return wait;
            Check(!controller.Flow.Paused && controller.GetComponentsInChildren<ResearchWorldInteractionGate>(true).All(g => g.Allowed) &&
                controller.trainingObjects.All(o => o.grabbable.enabled) && controller.locomotionInputRoots.All(g => g.transform.parent.gameObject.activeSelf) &&
                LoggedEvents().Any(e => (string)e["event"] == "SESSION_RESUMED" && (string)e["input_source"] == "SimulatedController"),
                "native Continue ray click logs SESSION_RESUMED and restores world after neutral",
                new { paused = controller.Flow.Paused, gates = controller.GetComponentsInChildren<ResearchWorldInteractionGate>(true).Select(g => g.Allowed).ToArray(),
                    objects = controller.trainingObjects.Select(o => o.grabbable.enabled).ToArray(),
                    locomotion_gates = controller.locomotionInputRoots.Select(g => g.transform.parent.gameObject.activeSelf).ToArray() });

            var slate = controller.view.mainPanel;
            var handle = slate.GetComponentInChildren<ResearchDragHandle>();
            var utilities = slate.GetComponentsInChildren<ResearchUIAction>(true).Where(a => a.command == "main_minimize" || a.command == "main_recenter").ToArray();
            Check(utilities.Length == 2 && utilities.All(a => !a.gameObject.activeInHierarchy),
                "Tutorial hides primary Minimize/Recenter utilities that compete with the current lesson");
            var tutorialCommands = slate.GetComponentsInChildren<ResearchUIAction>(false).Select(a => a.command).ToArray();
            Check(tutorialCommands.SequenceEqual(new[] { "pause" }) && handle && handle.isActiveAndEnabled
                && ((RectTransform)handle.transform).rect.width >= 64 && ((RectTransform)handle.transform).rect.height >= 64,
                "movement Tutorial exposes only relevant Pause action and a generous native panel move handle");

            // Only the UI context is staged. The DEV record, tutorial evidence and native input/world gates stay intact.
            var tutorialFlow = controller.Flow; var tutorialFact = controller.TutorialFact;
            var recordBeforeUtilities = JsonConvert.SerializeObject(controller.Record);
            var tutorialFacts = (HashSet<string>)typeof(ResearchSessionFlowController)
                .GetField("tutorialEvidence", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
            var factsBeforeUtilities = tutorialFacts.OrderBy(fact => fact, StringComparer.Ordinal).ToArray();
            var flowSetter = typeof(ResearchSessionFlowController).GetProperty("Flow").GetSetMethod(true);
            long beforeUtilityContext = LoggedEvents().Select(e => (long)e["seq"]).DefaultIfEmpty(-1).Max();
            try
            {
                controller.Log("DEMO_INPUT", "ui.task_utilities", "TaskContextEntered", after: new { staged_state = "Task1Active", native_world_gates_changed = false }, source: "Tester");
                flowSetter.Invoke(controller, new object[] { new ResearchSessionFlow(tutorialFlow.Order, ResearchFlowState.Task1Active, 5) });
                controller.view.RecenterAll(false); yield return .3d;
                Check(controller.Flow.IsTaskActive && utilities.All(a => a.gameObject.activeInHierarchy)
                    && controller.taskPorts.All(port => !port.InteractionAllowed), "isolated DEV task UI exposes utilities without starting or enabling a research task");
                Check(utilities.Length == 2 && utilities.All(a =>
                    a.GetComponentInChildren<ResearchUtilityIcon>(true) && a.GetComponent<ResearchControlHint>()
                    && !a.transform.Find("Label").gameObject.activeSelf
                    && ((RectTransform)a.transform).rect.width >= 64 && ((RectTransform)a.transform).rect.height >= 64),
                    "task slate has consistent quiet mesh utilities with contextual labels and generous native targets");
                var recovery = controller.view.RecoveryCanvas;
                var utilityState = controller.Flow.State; var utilityFact = controller.TutorialFact;
                long beforeRecenterSequence = LoggedEvents().Select(e => (long)e["seq"]).DefaultIfEmpty(-1).Max();
                foreach (double wait in RayClick("main_recenter")) yield return wait;
                Check(LoggedEvents().Count(e => (long)e["seq"] > beforeRecenterSequence && (string)e["event"] == "PANEL_RECENTER"
                    && (string)e["target"] == "task_information" && (string)e["input_source"] == "SimulatedController") == 1,
                    "native task Recenter is accepted once without changing the staged task state");
                Check(controller.Flow.State == utilityState && controller.TutorialFact == utilityFact,
                    "native task Recenter preserves the isolated task UI context");
                foreach (double wait in RayClick("main_minimize")) yield return wait;
                Check(!slate.transform.Find("Expanded").gameObject.activeInHierarchy && slate.transform.Find("MinimizedTask").gameObject.activeInHierarchy,
                    "native Minimize icon leaves an accessible task restore pill");
                Check(recovery && recovery != slate.GetComponent<Canvas>() && recovery != controller.view.referencePanel.GetComponent<Canvas>()
                    && controller.view.ParticipantCanvases.Contains(recovery) && recovery.GetComponentInChildren<PointableCanvas>(true)
                    && recovery.GetComponentInChildren<RayInteractable>(true)
                    && controller.guards.All(g => g.uiClearanceSurfaces.Any(c => c && c.transform.IsChildOf(recovery.transform) && c.Surface != null)),
                    "independent Recovery/Pause Canvas has the native ray stack and participates in every controller UI clearance guard");
                var clearance = typeof(HandsLabInputGuard).GetMethod("UIClear", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var recoverySurface = recovery.GetComponentInChildren<RayInteractable>(true).Surface;
                Check(recoverySurface.ClosestSurfacePoint(recovery.transform.position, out var recoveryHit), "Recovery native surface exposes its actual clipped bounds");
                foreach (var g in controller.guards)
                {
                    var priorSurfaces = g.uiClearanceSurfaces; var priorColliders = g.uiClearanceColliders;
                    try
                    {
                        g.uiClearanceSurfaces = new[] { recovery.GetComponentInChildren<RayInteractable>(true) }; g.uiClearanceColliders = Array.Empty<Collider>();
                        Check(!(bool)clearance.Invoke(g, new object[] { recoveryHit.Point + recoveryHit.Normal * .02f }),
                            "Native Canvas clearance blocks a fingertip 20 mm from its surface without a PhysicsCollider");
                        Check((bool)clearance.Invoke(g, new object[] { recoveryHit.Point + recoveryHit.Normal * .051f }),
                            "Native Canvas clearance releases a fingertip beyond the 50 mm neutral threshold");
                        recovery.enabled = false;
                        Check((bool)clearance.Invoke(g, new object[] { recoveryHit.Point }), "Concealed Canvas does not block neutral hand clearance");
                    }
                    finally { recovery.enabled = true; g.uiClearanceSurfaces = priorSurfaces; g.uiClearanceColliders = priorColliders; }
                }
                Check(Vector3.Distance(recovery.transform.position, controller.view.Head.position) >= .8f
                    && !Guard(1).interactionsRoot.GetComponentsInChildren<PokeInteractor>(true)
                        .Any(p => p.State == InteractorState.Select && p.Interactable && p.Interactable.transform.IsChildOf(recovery.transform)),
                    "Recovery remains outside the resting-hand near-poke region before deliberate ray acquisition",
                    new { distance = Vector3.Distance(recovery.transform.position, controller.view.Head.position), recovery_position = V(recovery.transform.position) });
                long beforeRecoverySequence = LoggedEvents().Select(e => (long)e["seq"]).DefaultIfEmpty(-1).Max();
                foreach (double wait in RayClick("panel_recovery")) yield return wait;
                Check(LoggedEvents().Count(e => (long)e["seq"] > beforeRecoverySequence && (string)e["event"] == "PANEL_RECENTER"
                    && (string)e["target"] == "all_visible_panels" && (string)e["result"] == "RecoveryRequested"
                    && (string)e["input_source"] == "SimulatedController") == 1,
                    "native labeled Recovery is accepted by the existing controller command path and recorded once");
                Check(controller.Flow.State == utilityState && controller.TutorialFact == utilityFact
                    && slate.transform.Find("MinimizedTask").gameObject.activeInHierarchy,
                    "native labeled Recovery repositions panels without restoring content or advancing the staged task");
                foreach (double wait in RayClick("main_restore")) yield return wait;
                Check(slate.transform.Find("Expanded").gameObject.activeInHierarchy && controller.Flow.State == utilityState && controller.TutorialFact == utilityFact,
                    "native task restore preserves the isolated task UI workflow state");
            }
            finally
            {
                // Restore the exact original Flow object before the native Tutorial drag and handling cases.
                flowSetter.Invoke(controller, new object[] { tutorialFlow });
                controller.view.RecenterAll(false);
                controller.Log("DEMO_INPUT", "ui.task_utilities", "TutorialContextRestored", source: "Tester");
            }
            yield return .3d;
            Check(ReferenceEquals(controller.Flow, tutorialFlow) && controller.TutorialFact == tutorialFact
                && JsonConvert.SerializeObject(controller.Record) == recordBeforeUtilities
                && tutorialFacts.OrderBy(fact => fact, StringComparer.Ordinal).SequenceEqual(factsBeforeUtilities),
                "task utility fixture restores the original Tutorial flow, fact, DEV record and accumulated evidence");
            Check(!LoggedEvents().Any(e => (long)e["seq"] > beforeUtilityContext && new[]
                { "TASK_STARTED", "TASK_COMPLETED", "INSPECT", "TUTORIAL_STEP_COMPLETED", "RUN_STARTED", "RUN_COMPLETED", "SESSION_PAUSED", "SESSION_RESUMED" }
                .Contains((string)e["event"])), "isolated utility callbacks emit no artificial research task, Tutorial or session transitions");
            Check(utilities.All(a => !a.gameObject.activeInHierarchy) && handle.isActiveAndEnabled
                && controller.guards.All(guard => guard.TrackingValid && guard.InteractionsAllowed && !guard.WaitingForNeutral && !guard.Suspended),
                "restored Tutorial retains hidden utilities, native drag access and released neutral guards");
            for (int i = 0; i < 30; i++)
            {
                var pose = ResearchFlowVisualInput.ControllerWorldPose(1);
                pose.rotation = Quaternion.FromToRotation(ray.Forward, (handle.transform.position - ray.Origin).normalized) * pose.rotation;
                Drive(1, pose); yield return .05d;
            }
            Drive(1, ResearchFlowVisualInput.ControllerWorldPose(1), trigger: 1); yield return .2d;
            Check(handle.IsDragging, "native ray pointer down captures slate drag without screen-space movement");
            var dragStart = slate.transform.position;
            var heldPose = ResearchFlowVisualInput.ControllerWorldPose(1);
            const System.Reflection.BindingFlags dragFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            float dragDistance = (float)typeof(ResearchDragHandle).GetField("rayDistance", dragFlags).GetValue(handle);
            var dragOffset = (Vector3)typeof(ResearchDragHandle).GetField("offset", dragFlags).GetValue(handle);
            var dragFrames = new List<object>();
            var segmentEnds = new List<Vector3>();
            int frozenFrames = 0, jumpFrames = 0, stalledTargets = 0, lostDrags = 0;
            float maxTargetError = 0;
            bool dragSequenceDone = false;
            int moveSamplesBefore = handle.MoveSamples, placementCallsBefore = slate.PlacementCalls;
            double moveMsBefore = handle.MoveMilliseconds, placementMsBefore = slate.PlacementMilliseconds;
            var towardHead = (controller.view.Head.position - dragStart).normalized;
            var dragRight = controller.view.Head.right;
            // Capture after LateUpdate, not in EditorApplication.update: its ticks are not rendered frames.
            IEnumerator SampleNativeDrag()
            {
                var endOfFrame = new WaitForEndOfFrame();
                var previousActual = slate.transform.position;
                var previousTarget = previousActual;
                int previousFrame = Time.frameCount;
                foreach (var segment in new[] { (step: towardHead * .005f, count: 30), (step: dragRight * .004f, count: 30), (step: -dragRight * .004f, count: 60) })
                {
                    for (int i = 0; i < segment.count; i++)
                    {
                        heldPose.position += segment.step;
                        int submittedFrame = Time.frameCount;
                        Drive(1, heldPose, trigger: 1);
                        do { yield return endOfFrame; } while (Time.frameCount <= submittedFrame);
                        var head = controller.view.Head;
                        var delta = ray.Ray.GetPoint(dragDistance) + dragOffset - head.position;
                        var target = head.position + delta.normalized * Mathf.Clamp(delta.magnitude, .35f, 2.1f);
                        target.y = Mathf.Clamp(target.y, head.position.y - .65f, head.position.y + .45f);
                        var actual = slate.transform.position;
                        float targetStep = Vector3.Distance(target, previousTarget), actualStep = Vector3.Distance(actual, previousActual);
                        float error = Vector3.Distance(actual, target);
                        if (targetStep < .001f) stalledTargets++;
                        // Stall = not moving while trailing; a filtered pose at rest ON target (direction reversal) is not a freeze.
                        if (targetStep >= .001f && actualStep < .0005f && error > .002f) frozenFrames++;
                        // Overshoot: moved farther than the remaining distance to the new target (filtered catch-up is allowed).
                        if (actualStep > Vector3.Distance(previousActual, target) + .001f) jumpFrames++;
                        if (!handle.IsDragging) lostDrags++;
                        maxTargetError = Mathf.Max(maxTargetError, error);
                        dragFrames.Add(new { frame = Time.frameCount, submitted_frame = submittedFrame, frame_gap = Time.frameCount - previousFrame,
                            target = V(target), actual = V(actual), target_step = targetStep, actual_step = actualStep, target_error = error });
                        previousActual = actual; previousTarget = target; previousFrame = Time.frameCount;
                    }
                    segmentEnds.Add(slate.transform.position);
                }
                dragSequenceDone = true;
            }
            var dragRoutine = controller.StartCoroutine(SampleNativeDrag());
            double dragDeadline = EditorApplication.timeSinceStartup + 30;
            while (!dragSequenceDone && EditorApplication.timeSinceStartup < dragDeadline) yield return 0d;
            if (!dragSequenceDone) controller.StopCoroutine(dragRoutine);
            evidence.Add(new { name = "native drag consecutive rendered frame targets", hardware_verified = false, completed = dragSequenceDone,
                frozenFrames, jumpFrames, stalledTargets, lostDrags, maxTargetError, frames = dragFrames,
                move_samples = handle.MoveSamples - moveSamplesBefore, rejected_samples = handle.RejectedSamples,
                filter_latency_ms = handle.FilterLatencyMilliseconds, filter_max_error_mm = handle.FilterMaxErrorMillimetres,
                move_ms = handle.MoveMilliseconds - moveMsBefore, max_move_ms = handle.MaxMoveMilliseconds,
                placement_calls = slate.PlacementCalls - placementCallsBefore, placement_ms = slate.PlacementMilliseconds - placementMsBefore });
            Check(dragSequenceDone && dragFrames.Count == 120, "native drag records 120 targets after distinct rendered frames");
            // The One-Euro target filter trails by design; bound it instead of requiring zero error.
            Check(frozenFrames == 0 && jumpFrames == 0 && stalledTargets == 0 && lostDrags == 0 && maxTargetError < .025f &&
                handle.FilterLatencyMilliseconds < 45,
                "native ray drag has no freeze or overshoot and bounded stabilization lag: max " + maxTargetError + " m, " + handle.FilterLatencyMilliseconds + " ms");
            Check(Vector3.Distance(segmentEnds[0], controller.view.Head.position) < Vector3.Distance(dragStart, controller.view.Head.position) - .08f,
                "native incremental drag pulls slate closer in depth");
            Check(Vector3.Dot(segmentEnds[1] - segmentEnds[0], dragRight) > .07f, "native incremental drag moves slate right");
            Check(Vector3.Dot(segmentEnds[2] - segmentEnds[1], dragRight) < -.15f, "native incremental drag moves slate left without canvas feedback");
            // Ownership: a canvas-side pointer-up while the owning ray still selects must not drop the panel.
            int metricsBefore = LoggedEvents().Count(e => (string)e["event"] == "PANEL_DRAG_METRICS");
            handle.OnPointerUp(new PointerEventData(EventSystem.current) { pointerId = ray.Identifier });
            Check(handle.IsDragging && ray.State == InteractorState.Select, "canvas pointer-up while Trigger is held keeps drag ownership");
            Drive(1, heldPose); yield return .2d;
            Check(!handle.IsDragging, "native trigger release ends slate drag");
            var releaseMetrics = LoggedEvents().Where(e => (string)e["event"] == "PANEL_DRAG_METRICS").ToArray();
            Check(releaseMetrics.Length == metricsBefore + 1 && (string)releaseMetrics.Last()["after"]["release_cause"] == "ray_released"
                && (int)releaseMetrics.Last()["after"]["ignored_pointer_ups"] == 1,
                "one PANEL_DRAG_METRICS per drag; release comes from the owning ray; ignored pointer-up counted",
                releaseMetrics.Last()["after"]);
            var releasedPosition = slate.transform.position;
            heldPose.position += Vector3.up * .12f;
            Drive(1, heldPose); yield return .2d;
            Check(Vector3.Distance(releasedPosition, slate.transform.position) < .001f, "released slate stops following controller");
            controller.view.RecenterAll(false); yield return .2d;

            // Explicit DEV priming isolates native handling from the separately checked locomotion lesson.
            foreach (var fact in new[] { "teleport", "snap", "roomscale", "select" })
            {
                controller.DemoTutorialEvidence(fact); yield return .8d;
                if (fact == "snap") Check(controller.Flow.TutorialCompleted == 0 && controller.TutorialFact == "roomscale", "visible room-scale prompt uses a fresh local head baseline");
            }
            yield return .8d;
            Check(controller.Flow.TutorialCompleted == 2 && controller.TutorialFact == "grab", "DEV priming reaches handling; subsequent credit uses native input");
            foreach (var pair in new[] { (id: "handslab.pinch_stone", side: 0, mode: "hidden_grab"), (id: "handslab.pinch_stone", side: 0, mode: "feedback_grab"), (id: "handslab.pinch_stone", side: 0, mode: "early_drop"), (id: "handslab.pinch_stone", side: 0, mode: "pause"), (id: "handslab.pinch_stone", side: 0, mode: "drop"),
                (id: "handslab.grip_mug", side: 1, mode: "drop"), (id: "handslab.grip_mug", side: 1, mode: "contact"), (id: "handslab.pinch_stone", side: 0, mode: "throw") })
            {
                var item = controller.trainingObjects.Single(o => o.stableId == pair.id);
                const System.Reflection.BindingFlags handlingFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var practiceFacts = (HashSet<string>)typeof(ResearchSessionFlowController).GetField("tutorialEvidence", handlingFlags).GetValue(controller);
                if (pair.id == "handslab.pinch_stone" && pair.mode != "throw")
                {
                    // DEV-only reset isolates cases now that legitimate completed facts survive an interrupted hold.
                    controller.Log("DEMO_INPUT", "training.case_reset", "Injected", after: new { pair.mode }, source: "Tester");
                    typeof(ResearchSessionFlowController).GetProperty("Flow").GetSetMethod(true).Invoke(controller,
                        new object[] { new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 2) });
                    practiceFacts.Clear(); controller.view.CancelTutorialConfirmation(); controller.view.Refresh();
                }
                // DEV fixture reset isolates every case from the previous drop's position/rotation; recovery is checked separately.
                item.ResetToAuthored(); yield return .3d;
                evidence.Add(new { name = "native case starting pose", mode = pair.mode, source = "Tester", detail = item.CaptureSnapshot() });
                var grabber = Grabber(pair.side);
                var handedness = pair.side == 0 ? Handedness.Left : Handedness.Right;
                var targetPose = item.GetComponentsInChildren<HandGrabPose>(true)
                    .Where(p => p.HandPose.Handedness == handedness && (pair.id != "handslab.grip_mug" || p.RelativePose.position.x < -.1f))
                    .OrderBy(p => Vector3.Distance(p.transform.position, grabber.WristPoint.position)).First();
                bool observedHover = false;
                Action<PointerEvent> observe = evt =>
                {
                    if (evt.Identifier != grabber.Identifier) return;
                    if (evt.Type == PointerEventType.Hover) observedHover = true;
                    if (evt.Type == PointerEventType.Hover || evt.Type == PointerEventType.Select || evt.Type == PointerEventType.Unselect)
                        evidence.Add(new { name = pair.id + " native pointer event", type = evt.Type.ToString(), evt.Identifier,
                            elapsed_seconds = EditorApplication.timeSinceStartup - started });
                };
                item.grabbable.WhenPointerEventRaised += observe;
                detachGrabObserver = () => { if (item && item.grabbable) item.grabbable.WhenPointerEventRaised -= observe; };
                Vector3 restingPosition = item.transform.position;
                if (pair.side == 1)
                {
                    var pauseTarget = controller.view.GetComponentsInChildren<ResearchUIAction>(true)
                        .Single(a => a.command == "pause" && a.gameObject.activeInHierarchy).GetComponent<RectTransform>();
                    var contactCollider = item.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger);
                    // Keep the palm at the physical candidate while aiming the visible Pause button.
                    // UI stays enabled throughout; this tests trigger/grip ambiguity, not a hidden-panel workaround.
                    for (int i = 0; i < 24; i++)
                    {
                        var pose = ResearchFlowVisualInput.ControllerWorldPose(1);
                        pose.rotation = Quaternion.FromToRotation(ray.Forward, (pauseTarget.TransformPoint(pauseTarget.rect.center) - ray.Origin).normalized) * pose.rotation;
                        Vector3 palm = grabber.PalmPoint.position, contact = contactCollider.ClosestPoint(palm);
                        pose.position += contact + (contactCollider.bounds.center - contact).normalized * .018f - palm;
                        Drive(1, pose); yield return .06d;
                    }
                    foreach (double wait in RayClick("pause", () =>
                    {
                        float distance = Vector3.Distance(grabber.PalmPoint.position, contactCollider.ClosestPoint(grabber.PalmPoint.position));
                        Check(distance < .025f && !item.IsHeld && item.grabbable.enabled,
                            "UI trigger regression starts with palm near the live grab collider and grip released", new { palm_distance = distance, detail = GrabDetail(1, item, targetPose) });
                    })) yield return wait;
                    Check(controller.Flow.Paused && !item.IsHeld, "native UI trigger near a world candidate pauses without grabbing it");
                    foreach (double wait in RayClick("session_resume")) yield return wait;
                    Check(!controller.Flow.Paused && controller.GetComponentsInChildren<ResearchWorldInteractionGate>(true).All(g => g.Allowed),
                        "near-candidate UI Continue restores world after neutral");
                }
                // Same authored wrist pose and contact approach used by HandsLab's moving-controller regression.
                for (int i = 0; i < 24; i++)
                {
                    var pose = ResearchFlowVisualInput.ControllerWorldPose(pair.side);
                    pose.rotation = targetPose.RelativeTo.rotation * targetPose.RelativePose.rotation * Quaternion.Inverse(grabber.WristPoint.rotation) * pose.rotation;
                    pose.position += targetPose.RelativeTo.TransformPoint(targetPose.RelativePose.position) - grabber.WristPoint.position;
                    Drive(pair.side, pose); yield return .06d;
                }
                for (int i = 0; i < 12; i++)
                {
                    Vector3 palm = (pair.id == "handslab.pinch_stone" ? grabber.PinchPoint : grabber.PalmPoint).position;
                    var collider = item.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger)
                        .OrderBy(c => (c.ClosestPoint(palm) - palm).sqrMagnitude).First();
                    Vector3 contact = collider.ClosestPoint(palm);
                    var pose = ResearchFlowVisualInput.ControllerWorldPose(pair.side);
                    pose.position += contact + (collider.bounds.center - contact).normalized * .018f - palm;
                    Drive(pair.side, pose); yield return .08d;
                }
                // Meta's default HoverOnZeroStrength=false intentionally allows no candidate with an open hand.
                // Observe the actual Hover event during squeeze instead of requiring pre-grip hover.
                evidence.Add(new { name = pair.id + " pre_grip_observation", detail = GrabDetail(pair.side, item, targetPose) });
                if (pair.side == 1)
                {
                    var overlappingRay = Guard(1).interactionsRoot.GetComponentsInChildren<RayInteractor>(true).Single(r => r.name == "ControllerRayInteractor");
                    Check(overlappingRay.State == InteractorState.Hover && overlappingRay.Interactable && !item.IsHeld,
                        "right near-grip regression begins with an overlapping native UI ray hover", GrabDetail(pair.side, item, targetPose));
                }
                if (pair.mode == "feedback_grab")
                {
                    controller.Log("DEMO_INPUT", "training.confirmation", "Injected", source: "Tester");
                    controller.view.TutorialFactCompleted("select"); // DEV reproduces the previous lesson's secondary feedback before native Select.
                }
                if (pair.mode == "hidden_grab")
                {
                    controller.Log("DEMO_INPUT", "training.canvas_visibility", "Injected", source: "Tester");
                    controller.view.CancelTutorialConfirmation(); controller.view.mainPanel.GetComponent<Canvas>().enabled = false;
                }
                for (int i = 0; i < 12; i++)
                {
                    var pose = ResearchFlowVisualInput.ControllerWorldPose(pair.side); pose.position += Vector3.right * .0001f;
                    Drive(pair.side, pose, grip: 1); yield return .03d;
                    if (item.IsHeld) break;
                }
                yield return .1d;
                // Native Select precedes the SDK snap tween; wait for its completion rather than equating two different clocks.
                for (int settle = 0; settle < 30 && !grabber.IsGrabbing; settle++) yield return .03d;
                Check(observedHover, pair.id + " native object hover during squeeze", GrabDetail(pair.side, item, targetPose));
                Check(item.IsHeld && grabber.IsGrabbing && item.body.isKinematic, pair.id + " native grip selects through HandsLab Meta stack", GrabDetail(pair.side, item, targetPose));
                if (pair.mode == "hidden_grab")
                {
                    if (pair.mode == "hidden_grab") controller.view.mainPanel.GetComponent<Canvas>().enabled = true;
                    yield return .8d;
                    Check(controller.TutorialFact == "grab" && controller.TutorialNeedsRelease && !controller.view.TutorialActionReady("grab")
                        && controller.view.mainPanel.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.name == "Body" && t.text.Contains(ResearchStrings.Get("tutorial.try.release.controller", controller.Language).Split('\n')[0])),
                        "object acquired before Grab instruction asks for Release without automatic credit");
                    ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(EvidencePath), pair.mode + "_release_cue.png"));
                    yield return .1d;
                    long beforeRelease = LoggedEvents().Max(e => (long)e["seq"]);
                    Drive(pair.side, ResearchFlowVisualInput.ControllerWorldPose(pair.side)); yield return .15d;
                    Check(!item.IsHeld && !item.body.isKinematic && controller.TutorialFact == "grab" && controller.view.TutorialActionReady("grab")
                        && LoggedEvents().Count(e => (long)e["seq"] > beforeRelease && (string)e["event"] == "ACTION" && (string)e["result"] == "Release") == 1,
                        "ordinary native Release restores actionable Grab after pre-instruction hold");
                    var awayPre = ResearchFlowVisualInput.ControllerWorldPose(pair.side); awayPre.position += Vector3.up * .3f; Drive(pair.side, awayPre);
                    yield return .5d; detachGrabObserver(); detachGrabObserver = null; continue;
                }
                if (pair.mode == "early_drop" || pair.mode == "feedback_grab")
                {
                    Check(controller.TutorialFact == "move" && controller.view.TutorialActionReady("move"), "native Grab immediately exposes Lift while completed feedback is secondary");
                    long beforeRelease = LoggedEvents().Max(e => (long)e["seq"]);
                    Drive(pair.side, ResearchFlowVisualInput.ControllerWorldPose(pair.side)); yield return .15d;
                    var releaseEvents = LoggedEvents().Where(e => (long)e["seq"] > beforeRelease).ToArray();
                    Check(!item.IsHeld && !item.body.isKinematic && controller.Flow.TutorialCompleted == 2 && controller.TutorialFact == "move"
                        && controller.TutorialNeedsGrab && practiceFacts.SetEquals(new[] { "grab" }) && !controller.view.TutorialActionReady("move"),
                        "early native release restores dynamic physics and preserves Grab before regrabbing for Lift");
                    Check(releaseEvents.Count(e => (string)e["event"] == "ACTION" && (string)e["result"] == "Release") == 1
                        && releaseEvents.Count(e => (string)e["event"] == "TUTORIAL_PRACTICE_INTERRUPTED" && (string)e["reason"] == "released_before_instruction") == 1
                        && !releaseEvents.Any(e => (string)e["event"] == "TUTORIAL_STEP_COMPLETED"), "early native release logs once without completing Handling or clearing credited facts", releaseEvents);
                    var awayEarly = ResearchFlowVisualInput.ControllerWorldPose(pair.side); awayEarly.position += Vector3.up * .3f; Drive(pair.side, awayEarly);
                    yield return .5d;
                    detachGrabObserver(); detachGrabObserver = null; continue;
                }
                var heldPosition = item.transform.position; var heldRotation = item.transform.rotation;
                var start = ResearchFlowVisualInput.ControllerWorldPose(pair.side);
                if (pair.id == "handslab.pinch_stone" && pair.mode == "drop")
                {
                    var practice = new Pose(start.position, Quaternion.AngleAxis(40, Vector3.up) * start.rotation);
                    Drive(pair.side, practice, grip: 1); yield return .1d;
                    Check(controller.TutorialFact == "move" && controller.view.TutorialActionReady("move"), "out-of-order native rotation cannot skip the visible Lift instruction");
                    practice.position += Vector3.right * .1f;
                    Drive(pair.side, practice, grip: 1); yield return .15d;
                    Check(controller.TutorialFact == "move", "horizontal native movement is not Lift");
                    controller.view.LocalCommand("main_minimize", "None"); // DEV visibility injection isolates hidden-instruction credit; native utility acquisition is checked above.
                    practice.position += Vector3.up * .1f; practice.rotation = Quaternion.AngleAxis(40, Vector3.up) * practice.rotation;
                    Drive(pair.side, practice, grip: 1); yield return .15d;
                    Check(controller.TutorialFact == "move" && !controller.view.TutorialActionReady("move"), "native held movement behind DEV-minimized instruction cannot receive credit");
                    controller.view.LocalCommand("main_restore", "None");
                    yield return .15d;
                    Check(controller.TutorialFact == "move" && controller.view.TutorialActionReady("move"), "DEV Restore starts a fresh Lift baseline for native held input");
                    practice.position += Vector3.up * .1f;
                    Drive(pair.side, practice, grip: 1); yield return .15d;
                    Check(controller.TutorialFact == "rotate", "new native vertical lift credits only Lift");
                    Check(controller.view.TutorialActionReady("rotate"), "Rotate is immediately actionable after Lift without another forced hold");
                    practice.rotation = Quaternion.AngleAxis(40, Vector3.up) * practice.rotation;
                    Drive(pair.side, practice, grip: 1); yield return .15d;
                    Check(controller.TutorialFact == "release", "new native rotation credits Rotate");
                    Check(controller.view.TutorialActionReady("release"), "Release is immediately actionable after Rotate without another forced hold");
                }
                else for (int i = 1; i <= 20; i++)
                {
                    float t = i / 20f;
                    Drive(pair.side, new Pose(start.position + Vector3.up * (.18f * t), Quaternion.AngleAxis(40 * t, Vector3.up) * start.rotation), grip: 1);
                    yield return .03d;
                }
                yield return .2d;
                Check(item.IsHeld && item.transform.position.y - heldPosition.y > .08f, pair.id + " lift follows controller", item.CaptureSnapshot());
                Check(Quaternion.Angle(heldRotation, item.transform.rotation) > 25, pair.id + " rotation follows controller");
                if (pair.mode == "contact" || pair.mode == "throw")
                {
                    foreach (double wait in pair.mode == "contact" ? ResearchHeldObjectChecks.NativeContact(controller, item, pair.side, Check)
                        : ResearchHeldObjectChecks.NativeThrow(controller, item, pair.side, Check)) yield return wait;
                    var clear = ResearchFlowVisualInput.ControllerWorldPose(pair.side); clear.position += Vector3.up * .3f; Drive(pair.side, clear);
                    detachGrabObserver(); detachGrabObserver = null;
                    continue;
                }
                if (pair.mode == "pause")
                {
                    var retainedBeforePause = practiceFacts.ToArray(); string pendingBeforePause = controller.TutorialFact;
                    long beforeHeldPause = LoggedEvents().Max(e => (long)e["seq"]);
                    var pauseButton = controller.view.GetComponentsInChildren<ResearchUIAction>(true)
                        .Single(a => a.command == "pause" && a.gameObject.activeInHierarchy).GetComponent<UnityEngine.UI.Button>();
                    bool observedPausedClick = false, heldAtClick = true, dynamicAtClick = false;
                    Vector3 velocityAtClick = Vector3.positiveInfinity, angularAtClick = Vector3.positiveInfinity;
                    // Registered after ResearchUIAction: inspect the completed native pause command synchronously,
                    // before a subsequent physics step adds gravity to the intentionally released object.
                    UnityEngine.Events.UnityAction afterPause = () =>
                    {
                        observedPausedClick = controller.Flow.Paused;
                        heldAtClick = item.IsHeld; dynamicAtClick = !item.body.isKinematic;
                        velocityAtClick = item.body.linearVelocity; angularAtClick = item.body.angularVelocity;
                    };
                    pauseButton.onClick.AddListener(afterPause);
                    var movingPose = ResearchFlowVisualInput.ControllerWorldPose(0);
                    var heldBeforePauseAim = item.transform.position;
                    float observedHeldMotion = 0;
                    int pauseMotionSample = 0;
                    try
                    {
                        foreach (double wait in RayClick("pause"))
                        {
                            var moving = movingPose;
                            moving.position += controller.view.Head.right * (.04f * Mathf.Sin(++pauseMotionSample * .35f));
                            Drive(0, moving, grip: 1);
                            yield return wait;
                            if (item.IsHeld) observedHeldMotion = Mathf.Max(observedHeldMotion, Vector3.Distance(item.transform.position, heldBeforePauseAim));
                        }
                    }
                    finally { if (pauseButton) pauseButton.onClick.RemoveListener(afterPause); }
                    var pausedObjectEvents = LoggedEvents().Where(e => (long)e["seq"] > beforeHeldPause && (string)e["target"] == item.stableId).ToArray();
                    Check(observedHeldMotion > .01f && observedPausedClick && !heldAtClick && dynamicAtClick &&
                        velocityAtClick.magnitude < .001f && angularAtClick.magnitude < .001f,
                        "native pause while left object moves cancels hold without applying throw velocity",
                        new { observedHeldMotion, observedPausedClick, heldAtClick, dynamicAtClick, velocity = V(velocityAtClick), angular_velocity = V(angularAtClick) });
                    Check(pausedObjectEvents.Count(e => (string)e["event"] == "SYSTEM_RELEASE" && (string)e["source"] == "System") == 1 &&
                        !pausedObjectEvents.Any(e => (string)e["event"] == "ACTION" && (string)e["result"] == "Release"),
                        "pause cancellation logs one SYSTEM_RELEASE and no participant ACTION Release", pausedObjectEvents);
                    Check(controller.Flow.TutorialCompleted == 2 && controller.TutorialFact == pendingBeforePause && controller.TutorialNeedsGrab
                        && practiceFacts.SetEquals(retainedBeforePause) && !practiceFacts.Contains("release") && LoggedEvents().Count(e =>
                        (string)e["event"] == "TUTORIAL_PRACTICE_INTERRUPTED" && (string)e["reason"] == "system_release") == 1,
                        "native cancellation preserves completed Handling facts without participant Release credit");
                    Drive(0, ResearchFlowVisualInput.ControllerWorldPose(0));
                    foreach (double wait in RayClick("session_resume")) yield return wait;
                    Check(!controller.Flow.Paused && controller.GetComponentsInChildren<ResearchWorldInteractionGate>(true).All(g => g.Allowed),
                        "native Continue restores physical gates after releasing the held grip");
                    var pausedAway = ResearchFlowVisualInput.ControllerWorldPose(0); pausedAway.position += Vector3.up * .3f; Drive(0, pausedAway);
                    detachGrabObserver(); detachGrabObserver = null;
                    continue; // The next full acquisition repeats this left object, preserving ordinary release checks.
                }
                yield return .4d; // Stop hand motion before testing an ordinary drop.
                for (int i = 0; i < 12; i++)
                {
                    var pose = ResearchFlowVisualInput.ControllerWorldPose(pair.side); pose.position -= Vector3.right * .0001f;
                    Drive(pair.side, pose); yield return .03d;
                }
                yield return .8d;
                Check(!item.IsHeld && !grabber.IsGrabbing && !item.body.isKinematic, pair.id + " native release restores dynamic physics", item.CaptureSnapshot());
                if (pair.id == "handslab.pinch_stone" && pair.mode == "drop") Check(controller.Flow.TutorialCompleted == 3 && LoggedEvents().Count(e =>
                    (string)e["event"] == "TUTORIAL_STEP_COMPLETED" && (string)e["target"] == "training." + TutorialStep.GrabRotateRelease) == 1,
                    "native ordered handling completes once after visible Release instruction");
                Check(item.transform.position.y > restingPosition.y - .2f && Vector3.Distance(item.transform.position, restingPosition) < .6f
                    && item.body.linearVelocity.magnitude < 2.5f, pair.id + " controlled drop stays near its support without explosive velocity", item.CaptureSnapshot());
                // Move the released hand away before approaching the second object.
                var away = ResearchFlowVisualInput.ControllerWorldPose(pair.side); away.position += Vector3.up * .3f; Drive(pair.side, away);
                detachGrabObserver(); detachGrabObserver = null;
            }

            var destination = controller.transform.Find("NeutralRoomDestinations/Destination_04");
            Check(destination, "neutral aisle destination exists");
            var teleport = Guard(1).interactionsRoot.GetComponentsInChildren<TeleportInteractor>(true).Single(t => t.name == "TeleportControllerInteractor");
            Check(controller.locomotionInputRoots.Any(g => g.activeInHierarchy) && Guard(1).InteractionsAllowed,
                "native locomotion input remains enabled during tutorial");
            long beforeLocomotion = LoggedEvents().Max(e => (long)e["seq"]);
            Vector3 beforeOrigin = controller.origin.transform.position;
            float beforeEyeY = controller.origin.Camera.transform.position.y;
            Vector3 forward = Vector3.ProjectOnPlane(destination.position - controller.origin.Camera.transform.position, Vector3.up).normalized;
            var aimPose = new Pose(controller.origin.Camera.transform.position + forward * .35f - Vector3.up * .25f, Quaternion.LookRotation(forward));
            // SDK205: y > .5 activates teleport; y < .25 commits. Keep the stick held while aiming.
            Drive(1, aimPose, thumbstick: Vector2.up); yield return .2d;
            bool validDestination = false;
            for (int pitch = -10; pitch <= 80; pitch += 3)
            {
                var desired = Quaternion.AngleAxis(pitch, Vector3.Cross(Vector3.up, forward)) * forward;
                aimPose = ResearchFlowVisualInput.ControllerWorldPose(1);
                aimPose.rotation = Quaternion.FromToRotation(teleport.ArcOrigin.forward, desired) * aimPose.rotation;
                Drive(1, aimPose, thumbstick: Vector2.up); yield return .08d;
                validDestination = teleport.Interactable && teleport.Interactable.transform.IsChildOf(destination) && teleport.HasValidDestination();
                if (validDestination) break;
            }
            Check(validDestination, "native controller arc acquires valid neutral floor destination", new
            {
                active = teleport.isActiveAndEnabled, state = teleport.State.ToString(), target = teleport.Interactable ? teleport.Interactable.name : null,
                target_position = V(teleport.TeleportTarget.position), expected = V(destination.position), arc_origin = V(teleport.ArcOrigin.position),
                arc_forward = V(teleport.ArcOrigin.forward), axis = (Guard(1).controllerReference as IController)?.ControllerInput.Primary2DAxis.ToString()
            });
            Vector3 selectedDestination = teleport.TeleportTarget.position;
            Drive(1, ResearchFlowVisualInput.ControllerWorldPose(1)); yield return .6d;
            var teleportEvents = LoggedEvents().Where(e => (long)e["seq"] > beforeLocomotion && (string)e["input_source"] == "SimulatedController"
                && ((string)e["event"] == "TELEPORT_INITIATED" || (string)e["event"] == "TELEPORT_COMPLETED")).ToArray();
            Check(teleportEvents.Select(e => (string)e["event"]).SequenceEqual(new[] { "TELEPORT_INITIATED", "TELEPORT_COMPLETED" })
                && (string)teleportEvents[0]["action_id"] == (string)teleportEvents[1]["action_id"], "native axis release logs correlated teleport request and completion", teleportEvents);
            Vector3 afterOrigin = controller.origin.transform.position;
            Check(Vector3.ProjectOnPlane(afterOrigin - selectedDestination, Vector3.up).magnitude < .15f
                && Vector3.Distance(beforeOrigin, afterOrigin) > .5f, "native teleport moves origin to selected destination", new { before = V(beforeOrigin), after = V(afterOrigin), expected = V(selectedDestination) });
            Check(Mathf.Abs(afterOrigin.y - beforeOrigin.y) < .02f && Mathf.Abs(controller.origin.Camera.transform.position.y - beforeEyeY) < .02f,
                "native teleport preserves origin floor and tracked eye height", new { floor_before = beforeOrigin.y, floor_after = afterOrigin.y, eye_before = beforeEyeY, eye_after = controller.origin.Camera.transform.position.y });

            long beforeSnap = LoggedEvents().Max(e => (long)e["seq"]);
            float yaw = controller.origin.transform.eulerAngles.y;
            Drive(1, ResearchFlowVisualInput.ControllerWorldPose(1), thumbstick: Vector2.right); yield return .12d;
            Drive(1, ResearchFlowVisualInput.ControllerWorldPose(1)); yield return .4d;
            var snapEvents = LoggedEvents().Where(e => (long)e["seq"] > beforeSnap && (string)e["event"] == "SNAP_TURN" && (string)e["input_source"] == "SimulatedController").ToArray();
            float turned = Mathf.DeltaAngle(yaw, controller.origin.transform.eulerAngles.y);
            Check(snapEvents.Length == 1 && Mathf.Abs(Mathf.Abs(turned) - 45) < .5f, "native horizontal stick commits one logged 45 degree snap turn", new { actual_degrees = turned, events = snapEvents });
            Check(Mathf.Abs(controller.origin.transform.position.y - beforeOrigin.y) < .02f, "snap turn preserves floor height");
            var handContact = ResearchHandContactChecks.NativeSequence(controller, Check);
            while (handContact.MoveNext()) yield return handContact.Current;
        }

        static void Tick()
        {
            if (routine == null) return;
            if (!EditorApplication.isPlaying || !ResearchFlowVisualInput.Running) { Finish(false, "Play Mode or input fixture stopped"); return; }
            if (EditorApplication.timeSinceStartup < due) return;
            try
            {
                if (!routine.MoveNext()) { Finish(true, "Synthetic native-input sequence passed; hardware remains unverified"); return; }
                due = EditorApplication.timeSinceStartup + Convert.ToDouble(routine.Current);
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        static void Finish(bool passed, string detail)
        {
            EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= PlayState; AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            Application.targetFrameRate = previousFrameRate;
            var finishedRoutine = routine; routine = null;
            try { (finishedRoutine as IDisposable)?.Dispose(); }
            catch (Exception cleanupError) { passed = false; detail += "\nFixture cleanup failed: " + cleanupError; }
            Status = passed ? "Passed" : "Failed";
            try
            {
                File.WriteAllText(EvidencePath, JsonConvert.SerializeObject(new
                {
                    passed, detail, hardware_verified = false, input_source = "SimulatedController", technical_test = true, synthetic_hmd_height_metres = 1.65f,
                    participant = controller ? controller.Record?.participant_id : null,
                    events = controller ? controller.Logger?.EventsPath : null, evidence,
                    input = ResearchFlowVisualInput.Audit(),
                    objects = controller ? controller.trainingObjects.Where(o => o).Select(o => o.CaptureSnapshot()).ToArray() : null
                }, Formatting.Indented));
            }
            finally { detachGrabObserver?.Invoke(); detachGrabObserver = null; ResearchFlowVisualInput.Stop(); if (pointer) UnityEngine.Object.DestroyImmediate(pointer); pointer = null; }
        }
        public static void Stop() { if (routine != null) Finish(false, "Fixture stopped before completion"); }
        static void PlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Stop(); }
    }

    public sealed class ResearchTechnicalPointerProbe : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerClickHandler
    {
        public ResearchSessionFlowController controller;
        public int enters, downs, clicks;
        public string inputSource;
        void Source(PointerEventData data) { inputSource = controller.InputSourceForPointer(data.pointerId); }
        public void OnPointerEnter(PointerEventData data) { enters++; Source(data); }
        public void OnPointerDown(PointerEventData data) { downs++; Source(data); }
        public void OnPointerClick(PointerEventData data) { clicks++; Source(data); }
    }
}
#endif
