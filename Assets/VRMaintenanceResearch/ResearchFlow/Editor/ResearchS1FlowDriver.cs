#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Walks a DEV session through the production flow with the owned simulated Touch controllers only:
    // participant UI is pressed by aiming the SDK controller ray and pulling the trigger, Training is performed with
    // native grip/stick/head input, and researcher steps are mouse clicks on the drawn Researcher Console buttons.
    // No onClick.Invoke, DemoTutorialEvidence or direct ResearcherResume. Not Quest/wearer evidence.
    public static class ResearchS1FlowDriver
    {
        static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        static ResearchSessionFlowController controller;
        static double deadline;
        public static string Status { get; private set; } = "Idle";
        public static string Detail { get; private set; } = "";
        public static readonly List<string> Steps = new List<string>();
        // When set, a Game View PNG is written after each Training credit and at each flow screen (participant view evidence).
        public static string CaptureDir;
        public static void SetCaptureDir(string dir) { CaptureDir = string.IsNullOrEmpty(dir) ? null : dir; if (CaptureDir != null) System.IO.Directory.CreateDirectory(CaptureDir); }
        static int captureIndex;
        static void Capture(string label)
        {
            if (CaptureDir == null) return;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(CaptureDir, (++captureIndex).ToString("000") + "_" + label + ".png"));
        }

        public static string Report() => controller == null ? Status : JsonConvert.SerializeObject(new
        {
            status = Status, detail = Detail, participant = controller.Record?.participant_id, session = controller.Record?.session_id,
            run = controller.Record?.run_id, layout = controller.Record?.task_a_layout, state = controller.Flow?.State.ToString(), task = controller.Flow?.CurrentTask,
            paused = controller.Flow?.Paused, inspect_passed = controller.Flow?.InspectPassed, error = controller.Error,
            registry = controller.Record?.session_state.ToString(), export = controller.LastExportPath, steps = Steps.Count, last_step = Steps.LastOrDefault()
        });

        public static string BeginToFirstTask(string language = "Thai", bool orderAB = true, string layout = null) =>
            Start(ToFirstTask(language == "Japanese" ? 1 : 0, layout == null ? 0 : (orderAB ? 1 : 3) + (layout == "Q" ? 1 : 0)), 420);

        // Participant presses Inspect; a Fail is acknowledged with the only offered Continue.
        public static string Inspect() => Start(InspectRoutine(), 40);

        // Researcher clicks the checkpoint Resume in the Researcher Console, then the participant starts the next task.
        public static string Checkpoint() => Start(CheckpointRoutine(), 90);

        // Participant Pause, then the self-resume "Continue" (both by ray), during an active task or Training.
        public static string PauseAndContinue() => Start(PauseRoutine(), 40);

        // Teardown only: ends an open DEV session through the production abort path (terminal events + partial export).
        public static string Abort(string reason = "technical_failure")
        {
            controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>() ?? throw new InvalidOperationException("No flow controller.");
            Require(controller.Record == null || controller.Record.test_data, "Driver only ends test sessions.");
            if (controller.Record?.session_state == RegistryState.InProgress) controller.ResearcherAbort(false, reason);
            return Report();
        }

        // Teardown for a DEV session left InProgress by a stopped Play run: Resume Saved Session, then the production abort.
        public static string AbortSaved(string participantId, string reason = "technical_failure")
        {
            controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>() ?? throw new InvalidOperationException("No flow controller.");
            Require(controller.Record == null && participantId.StartsWith("DEV", StringComparison.Ordinal), "Only an unattached DEV ID can be closed.");
            var saved = controller.Registry.List(ParticipantGroup.Dev).Single(r => r.participant_id == participantId);
            Require(saved.session_state == RegistryState.InProgress && saved.test_data, participantId + " is an InProgress test session.");
            controller.StartSession(participantId, saved.language, saved.input_mode);
            Require(controller.Record?.participant_id == participantId, "Saved session resumed: " + controller.Error);
            controller.ResearcherAbort(false, reason);
            return Report();
        }

        static string Start(IEnumerator routine, double seconds)
        {
            if (Status == "Running") throw new InvalidOperationException("Driver already running.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) throw new InvalidOperationException("Enter unpaused Play Mode first.");
            controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>() ?? throw new InvalidOperationException("No flow controller.");
            FocusGame(); // the tester returns to the Game View before driving the participant
            routines.Clear(); routines.Push(routine);
            deadline = EditorApplication.timeSinceStartup + seconds;
            Status = "Running"; Detail = "";
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            return Report();
        }

        static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Play Mode ended or driver timeout at: " + Steps.LastOrDefault());
                if (!Application.isFocused) return;
                if (routines.Count == 0) { Stop("Done", null); return; }
                var current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); return; }
                if (current.Current is IEnumerator nested) routines.Push(nested);
            }
            catch (Exception error) { Stop("Failed", error.ToString()); }
        }

        static void Stop(string status, string detail)
        {
            EditorApplication.update -= Tick; routines.Clear();
            Status = status; Detail = detail ?? Detail;
            Debug.Log("RESEARCH_S1_FLOW_DRIVER " + Report());
        }

        public static string Diagnose()
        {
            controller = controller ? controller : UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            return JsonConvert.SerializeObject(new
            {
                input = controller.Record?.input_mode.ToString(), state = controller.Flow?.State.ToString(), focused = Application.isFocused,
                guards = controller.guards.Where(g => g).Select(g => new { side = g.side.ToString(), policy = g.policy.ToString(), mode = g.Mode.ToString(),
                    connected = (g.controllerReference as IController)?.IsConnected, pose = (g.controllerReference as IController)?.IsPoseValid,
                    g.Suspended, g.WaitingForNeutral, g.InteractionsAllowed, g.LastReason }).ToArray(),
                devices = UnityEngine.InputSystem.InputSystem.devices.Select(d => d.name + ":" + d.layout).ToArray(), steps = Steps
            });
        }

        static string Locomotion() => string.Join(" | ", controller.guards.Where(g => g).Select(g => g.side + ": " + string.Join(",",
            g.interactionsRoot.GetComponentsInChildren<Oculus.Interaction.Locomotion.TeleportInteractor>(true).Select(t => t.name + "/" + t.gameObject.activeInHierarchy + "/" + t.State + "/" + (t.Interactable ? t.Interactable.name : "-")))
            + " turners=" + string.Join(",", g.interactionsRoot.GetComponentsInChildren<Oculus.Interaction.Locomotion.TurnerEventBroadcaster>(true).Select(t => t.name + "/" + t.isActiveAndEnabled))));

        // Rendered colour of each visible participant control: Graphic.color x CanvasRenderer x parent CanvasGroup alpha (design_rule 3.1).
        public static string MeasureControls()
        {
            controller = controller ? controller : UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            Color Rendered(Graphic g) { var c = g.color * g.canvasRenderer.GetColor(); foreach (var cg in g.GetComponentsInParent<CanvasGroup>()) c.a *= cg.alpha; return c; }
            string Hex(Color c) => ColorUtility.ToHtmlStringRGBA(c);
            return JsonConvert.SerializeObject(new
            {
                state = controller.Flow?.State.ToString(),
                controls = controller.view.GetComponentsInChildren<ResearchUIAction>(false).Select(a =>
                {
                    var button = a.GetComponent<Button>(); var label = a.GetComponentInChildren<TMPro.TMP_Text>();
                    return new { a.command, interactable = button.IsInteractable(), fill = Hex(Rendered(button.targetGraphic)), label = label ? Hex(Rendered(label)) : null, size = label ? label.fontSize : 0 };
                }).ToArray(),
                text = controller.view.mainPanel.GetComponentsInChildren<TMPro.TMP_Text>(false).Where(t => t.name == "Title" || t.name == "Body")
                    .Select(t => new { t.name, color = Hex(Rendered(t)), t.fontSize, overflow = t.isTextOverflowing, lines = t.textInfo.lineCount }).ToArray()
            });
        }

        // Editor main-loop stall watch: any update gap over 0.4 s, with what the driver/runners were doing (Movement QC attribution).
        static double lastUpdate;
        public static readonly List<object> Stalls = new List<object>();
        public static string StallWatch(bool on)
        {
            EditorApplication.update -= Watch; Stalls.Clear(); lastUpdate = 0;
            if (on) EditorApplication.update += Watch;
            return "stall watch " + on;
        }
        static void Watch()
        {
            double now = EditorApplication.timeSinceStartup;
            if (lastUpdate > 0 && now - lastUpdate > .4)
                Stalls.Add(new { at_wall = DateTime.Now.ToString("HH:mm:ss.fff"), gap_s = Math.Round(now - lastUpdate, 3), focused = Application.isFocused,
                    playing = EditorApplication.isPlaying, logger_t = controller && controller.Logger != null ? controller.Logger.Timestamp : 0, last_step = Steps.LastOrDefault(),
                    gc0 = GC.CollectionCount(0), gc2 = GC.CollectionCount(2) });
            lastUpdate = now;
        }
        public static string GateReport()
        {
            controller = controller ? controller : UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            return JsonConvert.SerializeObject(new
            {
                state = controller.Flow?.State.ToString(), paused = controller.Flow?.Paused, can_interact = controller.Flow?.CanInteract, floor = controller.FloorReady,
                device = controller.DeviceReady, headset = controller.HeadsetReady, transitioning = controller.Transitioning, error = controller.Error,
                world_gates = controller.GetComponentsInChildren<ResearchWorldInteractionGate>(true).Concat(controller.guards.Where(g => g).SelectMany(g => g.GetComponents<ResearchWorldInteractionGate>()))
                    .Distinct().Select(g => new { g.name, g.Allowed, g.isActiveAndEnabled }).ToArray(),
                guards = controller.guards.Where(g => g).Select(g => new { side = g.side.ToString(), g.InteractionsAllowed, g.WaitingForNeutral, g.LastReason, g.InputNeutral, g.TrackingValid, g.SystemCancelling, g.Suspended }).ToArray(),
                ports = controller.taskPorts.Where(p => p).Select(p => new { p.taskId, p.InteractionAllowed }).ToArray()
            });
        }

        public static string StallReport() => JsonConvert.SerializeObject(Stalls);

        static void Step(string text) { Steps.Add(EditorApplication.timeSinceStartup.ToString("F2") + " " + text); if (Steps.Count > 400) Steps.RemoveAt(0); }

        static IEnumerator ToFirstTask(int languageIndex, int cell)
        {
            Steps.Clear();
            if (controller.Record == null)
            {
                var console = Console();
                // "Create next ID" is a GenericMenu item that synthetic IMGUI events cannot open; invoke that item's own action.
                Require(console.Group == ParticipantGroup.Dev, "Console group is DEV.");
                yield return ConsoleOpened(console);
                console.CreateNextId();
                Step("console menu Create next ID -> " + console.SelectedId);
                if (!ResearchFlowVisualInput.Running) ResearchFlowVisualInput.Begin(1.65f);
                yield return Wait(.6);
                yield return ConsoleClick(console, "language", languageIndex, 3);
                yield return ConsoleClick(console, "cell", cell, 5);
                yield return ConsoleClick(console, "input", (int)ExperimentInputMode.Controller, 3);
                Require(console.SelectedId.StartsWith("DEV", StringComparison.Ordinal), "Console selected a DEV ID: " + console.SelectedId);
                Step("console assignment: " + console.AssignmentText);
                var chosen = controller.Registry.List(ParticipantGroup.Dev).FirstOrDefault(r => r.participant_id == console.SelectedId);
                Require(controller.ResearcherStartBlocker(chosen) == null, "Console Start enabled: " + controller.ResearcherStartBlocker(chosen));
                yield return ConsoleClick(console, "start");
                Require(controller.Record != null && controller.Record.participant_id == console.SelectedId, "Console Start opened the selected session: " + controller.Error);
                Require(controller.Record.input_mode == ExperimentInputMode.Controller && controller.Record.language == (languageIndex == 1 ? ParticipantLanguage.Japanese : ParticipantLanguage.Thai)
                    && (cell == 0 || controller.Record.task_order == (cell <= 2 ? TaskOrder.AB : TaskOrder.BA) && controller.Record.task_a_layout == (cell % 2 == 1 ? "P" : "Q")),
                    "Session carries the clicked Console settings: " + controller.Record.input_mode + " " + controller.Record.language + " " + controller.Record.task_order + "_" + controller.Record.task_a_layout);
            }
            else Require(controller.Record.session_state == RegistryState.InProgress && controller.Flow.IsTutorial, "Only an open DEV tutorial session can be continued.");
            Require(string.IsNullOrEmpty(controller.Error) && controller.Record?.test_data == true, "DEV test session opened: " + controller.Error);
            if (!ResearchFlowVisualInput.Running) ResearchFlowVisualInput.Begin(1.65f);
            yield return Settle();
            Require(controller.Flow.State == ResearchFlowState.TutorialWelcome, "Tutorial welcome entered.");
            yield return RayClick("tutorial_start");
            yield return Training();
            yield return RayClick("tutorial_continue");
            yield return Settle();
            yield return StartTask();
        }

        static IEnumerator Training()
        {
            while (controller.Flow.IsTutorial && controller.Flow.State != ResearchFlowState.TutorialComplete)
            {
                string fact = controller.TutorialFact;
                double until = EditorApplication.timeSinceStartup + 8;
                while (!controller.view.TutorialActionReady(fact) || controller.TutorialArmedFact != fact)
                {
                    Require(EditorApplication.timeSinceStartup < until, "Tutorial action never became armed: " + fact);
                    yield return null;
                }
                int before = controller.TutorialCompletedFactCount;
                Step("training " + fact); Capture("prompt_" + fact);
                switch (fact)
                {
                    case "teleport": yield return Teleport(); break;
                    case "snap": yield return Snap(); break;
                    case "roomscale": yield return RoomScale(); break;
                    case "select": yield return RayClick("tutorial_select"); break;
                    case "grab": yield return Grab(); break;
                    case "move": yield return MoveHeld(Vector3.up * .16f, 0); break;
                    case "rotate": yield return MoveHeld(Vector3.zero, 45); break;
                    case "release": yield return Release(); break;
                    case "reference": yield return RayClick("reference"); break;
                    case "switch": yield return SwitchReference(); break;
                    case "reposition": yield return DragReference(); break;
                    case "close": yield return RayClick("reference_close"); break;
                    case "inspect":
                        if (controller.trainingStation) yield return MakeTrainerReady();
                        yield return RayClick("inspect"); break;
                    case "inspect_fail": yield return RayClick("inspect"); break;
                    case "examine": yield return ExamineTrainer(); break;
                    case "insert_wrong": yield return InsertModule(180, false); break;
                    case "insert": yield return InsertModule(0, true); break;
                    case "lock": case "unlock": yield return ToggleLatch(fact == "unlock"); break;
                    case "rotate_access": yield return TurnStation(130); break;
                    case "fasten": yield return TwistScrew(1); break;
                    case "loosen": yield return TwistScrew(-1); break;
                    case "cable_connect": yield return MoveLead(true); break;
                    case "cable_disconnect": yield return MoveLead(false); break;
                    default: throw new InvalidOperationException("No native driver for tutorial fact " + fact);
                }
                until = EditorApplication.timeSinceStartup + 8;
                while (controller.TutorialCompletedFactCount == before && controller.Flow.State != ResearchFlowState.TutorialComplete)
                {
                    Require(EditorApplication.timeSinceStartup < until, "Tutorial fact did not complete from native input: " + fact);
                    yield return null;
                }
                yield return Wait(.2); Capture("after_" + fact);
                Require(controller.TutorialLastCompletedFact == fact || controller.Flow.State == ResearchFlowState.TutorialComplete || controller.TutorialCompletedFactCount > before,
                    "Credit belongs to " + fact);
                yield return Settle();
            }
        }

        static IEnumerator StartTask()
        {
            Require(controller.Flow.State == ResearchFlowState.Task1Ready || controller.Flow.State == ResearchFlowState.Task2Ready, "Task ready screen expected.");
            yield return Wait(.3); Capture(controller.Flow.State.ToString());
            yield return RayClick("task_start");
            yield return Settle();
            Require(controller.Flow.IsTaskActive, "Task active after task_start.");
            ParkControllers();
            yield return Wait(.5); Capture(controller.Flow.State + "_" + controller.Flow.CurrentTask);
            Detail = "task " + controller.Flow.CurrentTask + " active";
        }

        static IEnumerator InspectRoutine()
        {
            Require(controller.Flow.IsTaskActive, "Inspect needs an active task.");
            var state = controller.Flow.State;
            yield return RayClick("inspect");
            double until = EditorApplication.timeSinceStartup + 8;
            while (controller.Flow.State == state && !controller.Flow.InspectPassed.HasValue)
            {
                Require(EditorApplication.timeSinceStartup < until, "Inspect produced no result.");
                yield return null;
            }
            until = EditorApplication.timeSinceStartup + 8;
            while (!controller.InspectionFeedbackVisible)
            {
                Require(EditorApplication.timeSinceStartup < until, "Inspect feedback never became visible.");
                yield return null;
            }
            bool passed = controller.Flow.InspectPassed == true;
            Detail = (passed ? "inspect_pass:" : "inspect_fail:") + controller.InspectionFeedbackKey;
            yield return RayClick("inspect_continue");
            yield return Settle();
            yield return AwaitViewerless(() => !controller.InspectionFeedbackVisible || controller.Flow.State != state, "Continue dismisses the Inspect feedback", 8);
            Detail += " -> " + controller.Flow.State;
        }

        static IEnumerator CheckpointRoutine()
        {
            Require(controller.Flow.State == ResearchFlowState.PostTask1Checkpoint || controller.Flow.State == ResearchFlowState.PostTask2Checkpoint, "Checkpoint expected.");
            Require(controller.ResearcherResumeBlocker() == null, "Checkpoint Resume enabled: " + controller.ResearcherResumeBlocker());
            var before = controller.Flow.State;
            yield return ConsoleClick(Console(), "resume");
            yield return Settle();
            Require(controller.Flow.State != before, "Console Resume advanced the checkpoint.");
            if (controller.Flow.State == ResearchFlowState.Task2Ready) yield return StartTask();
            else Detail = "after checkpoint: " + controller.Flow.State;
        }

        static IEnumerator PauseRoutine()
        {
            var state = controller.Flow.State;
            yield return RayClick("pause");
            yield return Settle();
            Require(controller.Flow.Paused, "Pause by ray paused the session.");
            yield return RayClick("session_resume");
            yield return Settle();
            Require(!controller.Flow.Paused && controller.Flow.State == state, "Continue by ray resumed the same state.");
            Detail = "paused and continued in " + state;
        }

        static IEnumerator Settle()
        {
            double start = EditorApplication.timeSinceStartup;
            int frame = Time.frameCount;
            while (controller.Transitioning || EditorApplication.timeSinceStartup - start < .5 || Time.frameCount < frame + 3)
            {
                Require(EditorApplication.timeSinceStartup - start < 15, "Transition did not settle.");
                yield return null;
            }
            Require(string.IsNullOrEmpty(controller.Error), "Controller error: " + controller.Error);
        }

        static IEnumerator Wait(double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until) yield return null;
        }

        static HandsLabInputGuard Guard(int side) => controller.guards.Single(g => g && g.side == (side == 0 ? Handedness.Left : Handedness.Right));
        static RayInteractor Ray(int side) => Guard(side).interactionsRoot.GetComponentsInChildren<RayInteractor>(true).Single(r => r.name == "ControllerRayInteractor");
        static HandGrabInteractor Grabber(int side) => Guard(side).interactionsRoot.GetComponentsInChildren<HandGrabInteractor>(true).Single(g => g.name == "ControllerHandGrabInteractor");
        static void Drive(int side, Pose pose, float grip = 0, float trigger = 0, Vector2 stick = default) => ResearchFlowVisualInput.SetControllerWorldPose(side, pose, grip, trigger, stick);
        static Pose Hand(int side) => ResearchFlowVisualInput.ControllerWorldPose(side);

        // Return a controller to a relaxed pointing position in front of the body (where a seated/standing user holds it).
        static void HandNearBody(int side)
        {
            var head = controller.origin.Camera.transform;
            var flat = Quaternion.LookRotation(Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized, Vector3.up);
            var position = head.position + flat * new Vector3(side == 0 ? -.22f : .22f, -.25f, .4f);
            if (Vector3.Distance(Hand(side).position, position) > .25f) Drive(side, new Pose(position, flat));
        }

        // Lower both controllers beside the body with rays pointing at the floor, so no idle ray rests on a panel.
        static void ParkControllers()
        {
            for (int side = 0; side < 2; side++)
            {
                var head = controller.origin.Camera.transform;
                var position = head.position + head.right * (side == 0 ? -.25f : .25f) - Vector3.up * .55f + head.forward * .15f;
                Drive(side, new Pose(position, Quaternion.LookRotation(Vector3.down, head.forward)));
            }
        }

        // Aim the right SDK controller ray until it hits the visible button, then press and release the trigger.
        static IEnumerator RayClick(string command)
        {
            double until = EditorApplication.timeSinceStartup + 10;
            ResearchUIAction action;
            while (!(action = controller.view.GetComponentsInChildren<ResearchUIAction>(false).FirstOrDefault(a => a.command == command && a.GetComponent<Button>().IsInteractable())))
            {
                Require(EditorApplication.timeSinceStartup < until, "No interactable button: " + command + " in " + controller.Flow?.State);
                yield return null;
            }
            var ray = Ray(1);
            while (!ray.isActiveAndEnabled || ray.State == InteractorState.Disabled)
            {
                Require(EditorApplication.timeSinceStartup < until, "Right controller ray enabled by its guard: " + JsonConvert.SerializeObject(ResearchFlowVisualInput.Audit()));
                yield return null;
            }
            var rect = action.GetComponent<RectTransform>();
            HandNearBody(1);
            // Leave any previously hovered target first so this acquisition is fresh.
            var away = Hand(1); away.rotation = Quaternion.AngleAxis(40, Vector3.up) * away.rotation; Drive(1, away);
            yield return Wait(.12);
            bool onTarget = false;
            for (int i = 0; i < 40 && !onTarget; i++)
            {
                var pose = Hand(1);
                pose.rotation = Quaternion.FromToRotation(ray.Forward, (rect.TransformPoint(rect.rect.center) - ray.Origin).normalized) * pose.rotation;
                Drive(1, pose);
                yield return Wait(.04);
                onTarget = ray.isActiveAndEnabled && ray.State == InteractorState.Hover && ray.CollisionInfo.HasValue && Inside(rect, ray.CollisionInfo.Value.Point);
            }
            Require(onTarget, "SDK ray hovers " + command + ": state=" + ray.State + " hit=" + (ray.CollisionInfo.HasValue ? ray.CollisionInfo.Value.Point.ToString("F3") : "none"));
            Drive(1, Hand(1), trigger: 1);
            yield return Wait(.15);
            Drive(1, Hand(1));
            yield return Wait(.25);
            Step("ray click " + command);
        }

        static bool Inside(RectTransform rect, Vector3 world)
        {
            var local = rect.InverseTransformPoint(world);
            return rect.rect.Contains(new Vector2(local.x, local.y));
        }

        static IEnumerator SwitchReference()
        {
            string current = controller.ReferenceSection;
            var tab = controller.view.GetComponentsInChildren<ResearchUIAction>(false)
                .FirstOrDefault(a => a.command.StartsWith("reference_tab.", StringComparison.Ordinal) && a.command != "reference_tab." + current && a.GetComponent<Button>().IsInteractable());
            Require(tab, "Another Reference format is offered.");
            yield return RayClick(tab.command);
        }

        // Grab the reference grip with the ray, pull the trigger, sweep 12 cm sideways, release.
        static IEnumerator DragReference()
        {
            var handle = controller.view.referencePanel.GetComponentsInChildren<ResearchDragHandle>(false).FirstOrDefault();
            Require(handle, "Reference panel has a drag grip.");
            var ray = Ray(1);
            HandNearBody(1);
            bool onHandle = false;
            for (int i = 0; i < 40 && !onHandle; i++)
            {
                var pose = Hand(1);
                pose.rotation = Quaternion.FromToRotation(ray.Forward, (handle.transform.position - ray.Origin).normalized) * pose.rotation;
                Drive(1, pose);
                yield return Wait(.04);
                onHandle = ray.State == InteractorState.Hover && ray.CollisionInfo.HasValue && Inside((RectTransform)handle.transform, ray.CollisionInfo.Value.Point);
            }
            Require(onHandle, "SDK ray hovers the Reference grip.");
            var held = Hand(1);
            Drive(1, held, trigger: 1);
            yield return Wait(.2);
            Require(handle.IsDragging, "Trigger on the grip starts a drag.");
            var right = controller.origin.Camera.transform.right;
            for (int i = 0; i < 30; i++) { held.position += right * .004f; Drive(1, held, trigger: 1); yield return Wait(.03); }
            Drive(1, held);
            yield return Wait(.3);
            Require(!handle.IsDragging, "Trigger release ends the drag.");
        }

        static IEnumerator Teleport()
        {
            // Hold the stick forward and sweep the controller toward an authored hotspot until the SDK arc hovers it; release to commit.
            var head = controller.origin.Camera.transform;
            var teleport = Guard(1).interactionsRoot.GetComponentsInChildren<Oculus.Interaction.Locomotion.TeleportInteractor>(true).Single(t => t.name == "TeleportControllerInteractor");
            var targets = UnityEngine.Object.FindObjectsByType<Oculus.Interaction.Locomotion.TeleportInteractable>(FindObjectsSortMode.None)
                .Where(t => t.isActiveAndEnabled && Vector3.ProjectOnPlane(t.transform.position - head.position, Vector3.up).magnitude > .6f)
                .OrderBy(t => Vector3.Distance(t.transform.position, head.position)).ToArray();
            Require(targets.Length > 0, "An enabled teleport hotspot exists.");
            var basePosition = head.position + head.right * .2f - Vector3.up * .3f + head.forward * .25f;
            bool hovering = false;
            foreach (var target in targets.Take(4))
            {
                var flat = Vector3.ProjectOnPlane(target.transform.position - basePosition, Vector3.up).normalized;
                for (float pitch = -55; pitch <= 25 && !hovering; pitch += 5)
                {
                    var aim = new Pose(basePosition, Quaternion.AngleAxis(pitch, Vector3.Cross(Vector3.up, flat)) * Quaternion.LookRotation(flat, Vector3.up));
                    Drive(1, aim, stick: new Vector2(0, 1));
                    yield return Wait(.08);
                    hovering = teleport.State == InteractorState.Hover && teleport.Interactable;
                }
                if (hovering) { Step("teleport hover " + teleport.Interactable.name); break; }
            }
            Require(hovering, "SDK teleport arc hovers a hotspot: " + Locomotion());
            Drive(1, Hand(1));
            yield return Wait(.8);
        }

        static IEnumerator Snap()
        {
            var pose = Hand(1);
            Drive(1, pose, stick: new Vector2(1, 0));
            yield return Wait(.25);
            Drive(1, pose);
            yield return Wait(.6);
        }

        static IEnumerator RoomScale()
        {
            ResearchFlowVisualInput.SetHeadMotion(new Vector3(.25f, 0, 0), 0);
            yield return Wait(.6);
            ResearchFlowVisualInput.SetHeadMotion(Vector3.zero, 0);
            yield return Wait(.3);
        }

        static HandsLabObject heldItem;
        static int heldSide;

        static IEnumerator Grab()
        {
            heldSide = 1;
            heldItem = controller.trainingObjects.Where(o => o && o.gameObject.activeInHierarchy && !o.IsHeld)
                .OrderBy(o => Vector3.Distance(o.transform.position, controller.origin.Camera.transform.position)).First();
            var grabber = Grabber(heldSide);
            var handedness = heldSide == 0 ? Handedness.Left : Handedness.Right;
            var target = heldItem.GetComponentsInChildren<HandGrabPose>(true).Where(p => p.HandPose.Handedness == handedness)
                .OrderBy(p => Vector3.Distance(p.transform.position, grabber.WristPoint.position)).First();
            for (int i = 0; i < 24; i++)
            {
                var pose = Hand(heldSide);
                pose.rotation = target.RelativeTo.rotation * target.RelativePose.rotation * Quaternion.Inverse(grabber.WristPoint.rotation) * pose.rotation;
                pose.position += target.RelativeTo.TransformPoint(target.RelativePose.position) - grabber.WristPoint.position;
                Drive(heldSide, pose); yield return Wait(.05);
            }
            for (int i = 0; i < 12; i++)
            {
                Vector3 palm = grabber.PalmPoint.position;
                var collider = heldItem.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).OrderBy(c => (c.ClosestPoint(palm) - palm).sqrMagnitude).First();
                Vector3 contact = collider.ClosestPoint(palm);
                var pose = Hand(heldSide); pose.position += contact + (collider.bounds.center - contact).normalized * .018f - palm;
                Drive(heldSide, pose); yield return Wait(.06);
            }
            for (int i = 0; i < 16 && !heldItem.IsHeld; i++) { Drive(heldSide, Hand(heldSide), grip: 1); yield return Wait(.04); }
            Require(heldItem.IsHeld, "Native grip holds " + heldItem.stableId);
        }

        static IEnumerator MoveHeld(Vector3 offset, float yaw)
        {
            Require(heldItem && heldItem.IsHeld, "Still holding the practice object.");
            var start = Hand(heldSide);
            for (int i = 1; i <= 20; i++)
            {
                float t = i / 20f;
                Drive(heldSide, new Pose(start.position + offset * t, Quaternion.AngleAxis(yaw * t, Vector3.up) * start.rotation), grip: 1);
                yield return Wait(.04);
            }
        }

        static IEnumerator Release()
        {
            Drive(heldSide, Hand(heldSide));
            yield return Wait(.3);
            Require(!heldItem.IsHeld, "Grip release drops the practice object.");
            var away = Hand(heldSide); away.position += Vector3.up * .25f; Drive(heldSide, away);
        }

        // ---- Trainer_FX1 practice, all through the owned simulated controllers ----
        static ResearchTrainingStation Station => controller.trainingStation;

        static Vector3 GrabPoint(int side, bool pinch) => pinch ? Grabber(side).PinchPoint.position : Grabber(side).PalmPoint.position;

        // Bring the palm (grip) or pinch point (trigger) onto the part's nearest solid surface, then squeeze until the SDK selects it.
        static IEnumerator Acquire(int side, Component part, bool pinch, Func<bool> held)
        {
            var colliders = part.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
            Require(colliders.Length > 0, "Grab collider on " + part.name);
            for (int i = 0; i < 14; i++)
            {
                Vector3 point = GrabPoint(side, pinch);
                var collider = colliders.OrderBy(c => (c.ClosestPoint(point) - point).sqrMagnitude).First();
                Vector3 contact = collider.ClosestPoint(point);
                var pose = Hand(side); pose.position += contact + (collider.bounds.center - contact).normalized * .01f - point;
                Drive(side, pose); yield return Wait(.05);
            }
            for (int i = 0; i < 20 && !held(); i++) { Drive(side, Hand(side), pinch ? 0 : 1, pinch ? 1 : 0); yield return Wait(.04); }
            Require(held(), "SDK grab selected " + part.name + (pinch ? " (trigger pinch)" : " (grip)"));
        }

        static IEnumerator Let(int side) { Drive(side, Hand(side)); yield return Wait(.35); }

        static void Lift(int side) { var away = Hand(side); away.position += Vector3.up * .15f; Drive(side, away); }

        // Move the held controller so the part's free (hand-driven) root reaches the target, in small steps.
        static IEnumerator Carry(int side, Func<Matrix4x4> freeRoot, Matrix4x4 target, bool pinch, int steps = 16)
        {
            var start = freeRoot();
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                var goal = Matrix4x4.TRS(Vector3.Lerp(start.GetColumn(3), target.GetColumn(3), t), Quaternion.Slerp(start.rotation, target.rotation, t), Vector3.one);
                var hand = Hand(side);
                var delta = goal * freeRoot().inverse;
                Drive(side, new Pose(delta.MultiplyPoint3x4(hand.position), delta.rotation * hand.rotation), pinch ? 0 : 1, pinch ? 1 : 0);
                yield return Wait(.04);
                if (i == 1 || i == steps) Step("carry " + i + "/" + steps + " err " + (Vector3.Distance(freeRoot().GetColumn(3), goal.GetColumn(3)) * 1000).ToString("F0") + "mm " + Quaternion.Angle(freeRoot().rotation, goal.rotation).ToString("F0") + "deg");
            }
            yield return Wait(.15);
        }

        static Matrix4x4 SeatTarget(AssetLibraryQA.QASocket socket, AssetLibraryQA.QAPlug plug, float depthMm, float turnDeg) =>
            AssetLibraryQA.SocketRules.RootPose(socket.seat.localToWorldMatrix, depthMm, turnDeg, plug.MateInRoot(plug.mates[0]));

        static IEnumerator InsertModule(float turn, bool seat)
        {
            var station = Station; var plug = station.module; var socket = station.moduleSocket;
            if (!plug.IsHeld) yield return Acquire(1, plug, false, () => plug.IsHeld);
            yield return Carry(1, () => plug.LastFreePose, SeatTarget(socket, plug, 60, turn), false);
            yield return Carry(1, () => plug.LastFreePose, SeatTarget(socket, plug, 18, turn), false, 10);
            yield return Carry(1, () => plug.LastFreePose, SeatTarget(socket, plug, -2, turn), false, 10);
            Step("module offered at turn " + turn + ": socket " + socket.State + " stop=" + socket.Last.stop + " depth=" + socket.Last.depthMm.ToString("F1"));
            if (seat) { yield return Let(1); Lift(1); Require(station.ModuleSeated, "Released module is seated."); yield break; }
            yield return Wait(.3);
            // Wrong way stays blocked; lift it back out while still holding it so the next attempt starts free.
            yield return Carry(1, () => plug.LastFreePose, SeatTarget(socket, plug, 60, turn), false, 10);
        }

        static IEnumerator ToggleLatch(bool thenPull)
        {
            var station = Station; bool before = station.LatchClosed;
            var view = station.latchInteractable as IInteractableView;
            yield return Acquire(0, station.latch, false, () => view.State == InteractableState.Select);
            yield return Let(0); Lift(0);
            Require(station.LatchClosed != before, "Latch toggled by grip select: closed=" + station.LatchClosed);
            if (!thenPull) yield break;
            var plug = station.module;
            yield return Acquire(1, plug, false, () => plug.IsHeld);
            var up = Matrix4x4.Translate(Vector3.up * .08f) * plug.LastFreePose;
            yield return Carry(1, () => plug.LastFreePose, up, false, 12);
            yield return Let(1); Lift(1);
            // In the fixed-UI order unlock is the last fact: completion resets the trainer, so the credit is the evidence of removal.
            Require(station.moduleSocket.Occupant == null || controller.TutorialLastCompletedFact == "unlock", "Unlatched module pulled out of its seat.");
        }

        static IEnumerator TurnStation(float degrees)
        {
            var station = Station; var handle = station.turnHandle;
            var grab = handle.GetComponent<Grabbable>();
            yield return Acquire(1, handle, false, () => grab.SelectingPointsCount > 0);
            var pivot = station.turntable.position;
            for (int i = 1; i <= 13; i++)
            {
                var hand = Hand(1); var r = Quaternion.AngleAxis(degrees / 13, Vector3.up);
                Drive(1, new Pose(pivot + r * (hand.position - pivot), r * hand.rotation), 1);
                yield return Wait(.06);
            }
            yield return Let(1); Lift(1);
            Step("turntable yaw " + station.turntable.localEulerAngles.y.ToString("F0"));
        }

        static IEnumerator TwistScrew(int direction)
        {
            var screw = Station.screw;
            yield return Acquire(1, screw, true, () => screw.IsHeld);
            float before = screw.Progress;
            for (int i = 0; i < 44; i++)
            {
                var hand = Hand(1);
                // Unity is left-handed: a positive turn about world up is clockwise seen from above (tighten).
                var r = Quaternion.AngleAxis(15 * direction, Vector3.up);
                Drive(1, new Pose(hand.position, r * hand.rotation), 0, 1);
                yield return Wait(.03);
            }
            Step("screw " + before.ToString("F0") + " -> " + screw.Progress.ToString("F0"));
            yield return Let(1); Lift(1);
        }

        static IEnumerator MoveLead(bool connect)
        {
            var station = Station; var plug = station.leadHead; var socket = station.leadSocket;
            yield return Acquire(1, plug, true, () => plug.IsHeld);
            if (connect)
            {
                yield return Carry(1, () => plug.LastFreePose, SeatTarget(socket, plug, 20, 0), true, 12);
                yield return Carry(1, () => plug.LastFreePose, SeatTarget(socket, plug, -2, 0), true, 10);
            }
            else yield return Carry(1, () => plug.LastFreePose, SeatTarget(socket, plug, 50, 0), true, 12);
            var offer = AssetLibraryQA.SocketRules.Measure(socket.seat.localToWorldMatrix, plug.LastFreePose * plug.MateInRoot(plug.mates[0]));
            Step("lead offered: socket " + socket.State + " occupant=" + (socket.Occupant ? socket.Occupant.name : "-") + " stop=" + socket.Last.stop + " free " + offer + " held=" + plug.IsHeld);
            yield return Let(1); Lift(1);
            Require(station.LeadConnected == connect, "Lead connected=" + connect);
        }

        static IEnumerator MakeTrainerReady()
        {
            var station = Station;
            if (!station.ModuleSeated) yield return InsertModule(0, true);
            if (!station.LatchClosed) yield return ToggleLatch(false);
            if (!station.screw.Tight) yield return TwistScrew(1);
            Require(station.Ready, "Trainer ready before the Pass sample.");
        }

        static IEnumerator ExamineTrainer()
        {
            var target = Station.GetComponentInChildren<ResearchExamineTarget>(true);
            var ray = Ray(1); HandNearBody(1);
            var module = Station.module.transform;
            bool visible = false;
            for (int i = 0; i < 40 && !visible; i++)
            {
                var pose = Hand(1);
                pose.rotation = Quaternion.FromToRotation(ray.Forward, (module.position - ray.Origin).normalized) * pose.rotation;
                Drive(1, pose); yield return Wait(.05);
                visible = target.ChipVisible;
            }
            Require(visible, "Pointing at the training module shows the Examine chip.");
            yield return RayPress((RectTransform)target.transform, "examine chip");
        }

        // Aim the right ray onto a visible rect and pull/release the trigger.
        static IEnumerator RayPress(RectTransform rect, string label)
        {
            var ray = Ray(1);
            bool onTarget = false;
            for (int i = 0; i < 40 && !onTarget; i++)
            {
                var pose = Hand(1);
                pose.rotation = Quaternion.FromToRotation(ray.Forward, (rect.TransformPoint(rect.rect.center) - ray.Origin).normalized) * pose.rotation;
                Drive(1, pose); yield return Wait(.04);
                onTarget = ray.State == InteractorState.Hover && ray.CollisionInfo.HasValue && Inside(rect, ray.CollisionInfo.Value.Point);
            }
            Require(onTarget, "SDK ray hovers " + label);
            Drive(1, Hand(1), trigger: 1); yield return Wait(.15);
            Drive(1, Hand(1)); yield return Wait(.25);
            Step("ray click " + label);
        }

        // Participant opens Reference by ray during an active task and uses every format: PDF scroll (stick) and paging,
        // Steps/Images paging, Video play / seek / pause; then closes it. Records what the viewer reports at each stop.
        public static string ReaderTour() => Start(ReaderTourRoutine(), 180);
        public static readonly List<object> ReaderLog = new List<object>();

        static object ViewerState(string label)
        {
            var viewer = controller.view.ReferenceViewer;
            var meta = Newtonsoft.Json.Linq.JObject.FromObject(viewer.Metadata);
            var entry = new { label, status = viewer.Status, viewer.PageIndex, viewer.PageCount, viewer.BoundSection, content = (string)meta["content_id"],
                approval = (string)meta["approval"], normalized = (float?)meta["normalized_offset"], time = (double?)meta["time_seconds"],
                playing = viewer.NativeVideoPlayer && viewer.NativeVideoPlayer.isPlaying, error = (string)meta["status"] };
            ReaderLog.Add(entry); Step("reader " + label + " " + viewer.Status + " " + viewer.PageIndex + "/" + viewer.PageCount);
            return entry;
        }

        static IEnumerator AwaitViewer(Func<bool> ready, string what, double seconds = 12)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (!ready()) { Require(EditorApplication.timeSinceStartup < until, "Reader never reached: " + what + " (" + controller.view.ReferenceViewer.Status + ")"); yield return null; }
        }

        static IEnumerator ReaderTourRoutine()
        {
            ReaderLog.Clear();
            Require(controller.Flow.IsTaskActive, "Reader tour needs an active task.");
            yield return RayClick("reference");
            yield return Settle();
            var viewer = controller.view.ReferenceViewer;
            foreach (var section in ResearchReferenceScheme.Active)
            {
                if (controller.ReferenceSection != section) yield return RayClick("reference_tab." + section);
                yield return AwaitViewer(() => viewer.IsReady && viewer.BoundSection == section, section);
                yield return Wait(.6); ViewerState(section + "_open"); Capture("reader_" + section);
                if (section == "video")
                {
                    yield return RayClick("reference_media.play");
                    yield return AwaitViewer(() => viewer.NativeVideoPlayer && viewer.NativeVideoPlayer.isPlaying && viewer.NativeVideoPlayer.time > .5, "video playing");
                    ViewerState("video_playing"); Capture("reader_video_playing");
                    double before = viewer.NativeVideoPlayer.time;
                    yield return RayClick("reference_media.seek_forward");
                    yield return AwaitViewer(() => viewer.NativeVideoPlayer.time > before + 5, "seek forward");
                    ViewerState("video_seek_forward");
                    yield return RayClick("reference_media.pause");
                    yield return AwaitViewer(() => !viewer.NativeVideoPlayer.isPlaying, "video paused");
                    yield return Wait(.4); ViewerState("video_paused"); Capture("reader_video_paused");
                    continue;
                }
                if (viewer.CanScrollDocument)
                {
                    float start = viewer.GuardedScrollbar ? viewer.GuardedScrollbar.value : 1;
                    var hand = Hand(1);
                    for (int i = 0; i < 25; i++) { Drive(1, hand, stick: new Vector2(0, -1)); yield return Wait(.04); }
                    Drive(1, hand); yield return Wait(.4);
                    ViewerState(section + "_scrolled"); Capture("reader_" + section + "_scrolled");
                }
                if (viewer.CanNext)
                {
                    int page = viewer.PageIndex;
                    yield return RayClick("reference_next");
                    yield return AwaitViewer(() => viewer.PageIndex == page + 1, section + " next page");
                    yield return Wait(.4); ViewerState(section + "_next"); Capture("reader_" + section + "_next");
                }
            }
            yield return RayClick("reference_close");
            yield return Settle();
            Require(!controller.ReferenceOpen, "Reference closed by ray.");
            Detail = JsonConvert.SerializeObject(ReaderLog);
        }

        // Fault cases during an active task, all through real inputs: participant Pause/Continue by ray, a simulated
        // controller tracking loss (the app's own suspend), researcher Resume by Console click, then wait out the time limit.
        public static string FaultTour(bool awaitTimeout) => Start(FaultTourRoutine(awaitTimeout), 600);

        static IEnumerator FaultTourRoutine(bool awaitTimeout)
        {
            Require(controller.Flow.IsTaskActive, "Fault tour needs an active task.");
            var active = controller.Flow.State;
            yield return PauseRoutine();
            Step("fault participant pause/continue ok");
            ResearchFlowVisualInput.SetControllerTracked(1, false);
            yield return AwaitViewerless(() => controller.Flow.Paused, "tracking loss suspends the task", 6);
            Step("fault tracking loss -> paused kind=" + controller.PauseKind); Capture("fault_tracking_loss");
            Require(!controller.ParticipantMayResume || controller.IsTechnical, "Tracking pause is researcher-owned for study sessions.");
            string blockedWhileLost = controller.ResearcherResumeBlocker();
            Step("resume blocker while lost: " + blockedWhileLost);
            ResearchFlowVisualInput.SetControllerTracked(1, true);
            yield return AwaitViewerless(() => controller.ResearcherResumeBlocker() == null, "resume becomes available once tracked", 8);
            yield return ConsoleClick(Console(), "resume");
            yield return Settle();
            Require(!controller.Flow.Paused && controller.Flow.State == active, "Console Resume returned to the task.");
            Step("fault researcher resume ok"); Capture("fault_resumed");
            ParkControllers();
            if (awaitTimeout)
            {
                yield return AwaitViewerless(() => controller.Flow.State != active, "task time limit ends the task", 470);
                Step("fault timeout -> " + controller.Flow.State); Capture("fault_timeout");
            }
            Detail = string.Join(" | ", Steps.Where(s => s.Contains("fault") || s.Contains("blocker")));
        }

        static IEnumerator AwaitViewerless(Func<bool> condition, string what, double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (!condition()) { Require(EditorApplication.timeSinceStartup < until, "Never observed: " + what + " (" + controller.Flow.State + ", paused=" + controller.Flow.Paused + ")"); yield return null; }
        }

        // Researcher ends the session from the Console: two real clicks (Abort, then Confirm). Data must still export.
        public static string ConsoleAbort() => Start(ConsoleAbortRoutine(), 30);
        static IEnumerator ConsoleAbortRoutine()
        {
            Require(controller.Record?.session_state == RegistryState.InProgress && controller.Record.test_data, "Open DEV session.");
            var console = Console();
            yield return ConsoleClick(console, "abort");
            Require(controller.Record.session_state == RegistryState.InProgress, "First Abort click only asks for confirmation.");
            yield return ConsoleClick(console, "abort");
            double until = EditorApplication.timeSinceStartup + 10;
            while (controller.Record.session_state == RegistryState.InProgress || string.IsNullOrEmpty(controller.LastExportPath))
            { Require(EditorApplication.timeSinceStartup < until, "Console Abort closed the session and exported."); yield return null; }
            yield return Wait(.5); Capture("after_console_abort");
            Detail = "aborted by console: " + controller.Record.session_state + " export=" + controller.LastExportPath;
        }

        static ResearcherConsole Console()
        {
            var console = Resources.FindObjectsOfTypeAll<ResearcherConsole>().FirstOrDefault() ?? EditorWindow.GetWindow<ResearcherConsole>("Researcher Console", false);
            FocusGame();
            return console;
        }

        static IEnumerator ConsoleOpened(ResearcherConsole console)
        {
            double until = EditorApplication.timeSinceStartup + 5;
            while (!console.Bound) { Require(EditorApplication.timeSinceStartup < until, "Researcher Console bound to the flow controller."); console.Repaint(); yield return null; }
        }

        // Mouse down/up at the centre of the control the Console last drew (segment index for toolbars).
        static IEnumerator ConsoleClick(ResearcherConsole console, string id, int segment = 0, int segments = 1)
        {
            double until = EditorApplication.timeSinceStartup + 5;
            console.ControlRects.Remove(id);
            while (!console.ControlRects.ContainsKey(id))
            {
                Require(EditorApplication.timeSinceStartup < until, "Researcher Console never drew control " + id);
                console.Repaint(); yield return null;
            }
            var rect = console.ControlRects[id];
            // IMGUI rects are content-local; window events are in host-view space, below the tab header.
            var origin = console.rootVisualElement.worldBound.position;
            var point = origin + new Vector2(rect.x + rect.width * (segment + .5f) / segments, rect.center.y);
            console.SendEvent(new Event { type = EventType.MouseDown, mousePosition = point, button = 0, clickCount = 1 });
            console.SendEvent(new Event { type = EventType.MouseUp, mousePosition = point, button = 0, clickCount = 1 });
            console.Repaint();
            FocusGame();
            yield return Wait(.3);
            Step("console click " + id + (segments > 1 ? "[" + segment + "]" : ""));
        }

        // The Editor Game View owns Application.isFocused; a researcher click elsewhere is a real focus loss the app pauses on.
        static void FocusGame() => EditorWindow.FocusWindowIfItsOpen(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));

        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
#endif
