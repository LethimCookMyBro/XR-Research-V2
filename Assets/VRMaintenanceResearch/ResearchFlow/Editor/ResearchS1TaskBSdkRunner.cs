#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1TaskBSdkRunner
    {
        static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        static ResearchS1TaskBMechanics task;
        static HandGrabInteractor hand;
        static ResearchS1FanPart selected;
        static EditorWindow gameView;
        static string output, session, run, step, error;
        static int lastFrame, side, cleanupFrame;
        static bool pinch, cleanupObserved;
        static double deadline, cleanupDeadline;
        // A hand turns a thread slightly past its end; Turn clamps at 0/full, while an exact orbit sum lands ~0.02 deg short.
        const float EndOvershoot = 6;
        public static bool Running { get; private set; }
        public static string Status { get; private set; } = "NotStarted";
        static bool captureFrames = true;
        public static void SetCaptureFrames(bool value)
        {
            if (Running) throw new InvalidOperationException("Do not change capture mode during a run.");
            captureFrames = value;
        }

        public static object Begin(string evidenceDirectory, int controllerSide = 1)
        {
            if (Running) throw new InvalidOperationException("An SDK runner already owns its input.");
            if (controllerSide < 0 || controllerSide > 1) throw new ArgumentOutOfRangeException(nameof(controllerSide));
            task = UnityEngine.Object.FindObjectsByType<ResearchS1TaskBMechanics>(FindObjectsSortMode.None).Single();
            side = controllerSide;
            CheckContext(false);
            session = task.controller.Record.session_id;
            run = task.controller.Record.run_id;
            if (task.Capture().rearLock != S1ThreadState.EngagedLoose || !task.frontClip.ClipClosed ||
                task.frontGuard.Placement != S1Placement.Seated || task.spinner.ThreadState != S1ThreadState.Tight ||
                task.blade.Placement != S1Placement.Seated)
                throw new InvalidOperationException("Expected the actual authored one-fault B assembly, not an intermediate state.");
            hand = UnityEngine.Object.FindObjectsByType<HandGrabInteractor>(FindObjectsSortMode.None)
                .Single(h => h.isActiveAndEnabled && h.Hand != null && h.Hand.Handedness == (side == 0 ? Handedness.Left : Handedness.Right));
            if (hand.State == InteractorState.Select) throw new InvalidOperationException("Release the selected hand before starting.");
            output = Path.GetFullPath(evidenceDirectory);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Use a fresh evidence directory.");
            Directory.CreateDirectory(output);
            error = null; selected = null; step = "initial"; lastFrame = -1; cleanupObserved = false;
            deadline = EditorApplication.timeSinceStartup + 120;
            gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            gameView.Focus(); gameView.Repaint();
            routines.Clear(); routines.Push(Run()); Running = true; Status = "Running";
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayState;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            Record("begin");
            return Audit();
        }

        static void CheckContext(bool identity)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || !ResearchFlowVisualInput.Running || !task ||
                !task.controller || task.controller.Record == null || !task.controller.Record.test_data ||
                !task.controller.Record.participant_id.StartsWith("DEV", StringComparison.Ordinal) ||
                task.controller.Flow == null || !task.controller.Flow.IsTaskActive || task.controller.Flow.CurrentTask != "B" ||
                !task.InteractionAllowed || !task.port.demoMode || !task.Capture().systemIntegrityValid)
                throw new InvalidOperationException("Requires unpaused active B, DEV test_data, running simulator and existing production interaction gates.");
            if (identity && (session != task.controller.Record.session_id || run != task.controller.Record.run_id))
                throw new InvalidOperationException("Session identity changed while the runner owned input.");
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("SDK runner exceeded 120 seconds.");
                CheckContext(true);
                if (!Application.isFocused) throw new InvalidOperationException("Game View focus lost. No guard override or automatic resume.");
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (routines.Count == 0) { Finish(null); return; }
                var current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); return; }
                if (current.Current is IEnumerator nested) routines.Push(nested);
            }
            catch (Exception failure)
            {
                try { Record("failure"); }
                catch (Exception evidenceFailure) { failure = new AggregateException(failure, evidenceFailure); }
                Finish(failure);
            }
        }

        static IEnumerator Run()
        {
            yield return WaitFrames(3);
            yield return Capture("00-initial");
            yield return Acquire(task.frontClip, true);
            Require(!task.frontClip.ClipClosed, "Actual clip did not open after SDK pinch.");
            yield return Release();
            yield return Capture("01-clip-open");
            yield return Acquire(task.frontGuard, false);
            yield return Rotate(task.frontGuard, task.frontGuard.insertTurnDegrees);
            Require(Mathf.Abs(Mathf.DeltaAngle(task.frontGuard.Pose.turnDeg, task.frontGuard.insertTurnDegrees)) < 1,
                "Front guard did not reach the authored bayonet entry turn.");
            yield return WithdrawAndPark(task.frontGuard, .28f, .50f);
            yield return Capture("02-front-guard-removed");
            yield return Acquire(task.spinner, true);
            yield return Rotate(task.spinner, -task.spinner.fullTurnDegrees - EndOvershoot);
            Require(task.spinner.ThreadProgressDegrees < .1f && task.spinner.ThreadState == S1ThreadState.Absent,
                "LH spinner did not loosen from clockwise input viewed from its head.");
            yield return WithdrawAndPark(task.spinner, .16f, -.35f);
            yield return Acquire(task.blade, false);
            yield return WithdrawAndPark(task.blade, .36f, -.52f);
            yield return Capture("03-blade-removed");
            yield return Acquire(task.rearLock, true);
            yield return Rotate(task.rearLock, -(task.rearLock.fullTurnDegrees - task.rearLock.ThreadProgressDegrees) - EndOvershoot);
            Require(task.rearLock.ThreadState == S1ThreadState.Tight, "RH rear lock did not tighten from clockwise input viewed from its head.");
            yield return Release();
            Require(task.rearLock.ThreadState == S1ThreadState.Tight && !task.rearLock.IsHeld, "Rear lock did not retain tight state after release.");
            yield return Capture("04-rear-lock-tight");
            yield return Acquire(task.blade, false);
            yield return ReturnToSeat(task.blade, 0);
            yield return Release();
            Require(task.blade.Placement == S1Placement.Seated && task.blade.Aligned && !task.blade.IsHeld,
                "Blade did not retain its released keyed seat.");
            yield return Capture("05-blade-seated");
            yield return Acquire(task.spinner, true);
            yield return ReturnToSeat(task.spinner, task.spinner.threadTravelMm);
            Require(task.spinner.Aligned && task.spinner.ThreadProgressDegrees < .1f,
                "Spinner did not reach its aligned thread entry.");
            yield return Rotate(task.spinner, task.spinner.fullTurnDegrees + EndOvershoot);
            yield return Release();
            Require(task.spinner.ThreadState == S1ThreadState.Tight && !task.spinner.IsHeld,
                "LH spinner did not retain tight state after positive rotation and release.");
            yield return Capture("06-spinner-tight");
            yield return Acquire(task.frontGuard, false);
            Require(Mathf.Abs(Mathf.DeltaAngle(task.frontGuard.Pose.turnDeg, task.frontGuard.insertTurnDegrees)) < 1,
                "Parked front guard lost its bayonet entry orientation.");
            yield return ReturnToSeat(task.frontGuard, 0);
            yield return Rotate(task.frontGuard, -task.frontGuard.insertTurnDegrees);
            yield return Release();
            Require(task.FrontGuardLocked && !task.frontGuard.IsHeld, "Front guard did not retain its released bayonet lock.");
            yield return Capture("07-front-guard-locked");
            yield return Acquire(task.frontClip, true);
            Require(task.frontClip.ClipClosed, "Actual front clip did not close after SDK pinch.");
            yield return Release();
            yield return Capture("08-front-clip-closed");
            var state = task.Capture();
            Require(state.systemIntegrityValid && state.powerIsolated && !state.motorRunning && state.baseStable && !state.assemblyObstructed &&
                state.frontGuardPlacement == S1Placement.Seated && state.frontGuardAligned && state.frontGuardClipped &&
                state.bladePlacement == S1Placement.Seated && state.bladeCompatible && state.bladeKeyAligned && state.spinner == S1ThreadState.Tight &&
                state.rearGuardPlacement == S1Placement.Seated && state.rearGuardAligned && state.rearLock == S1ThreadState.Tight,
                "Released physical snapshot does not match the repaired assembly.");
            Record("repair-and-reassembly-observed-inspection-not-invoked");
        }

        // After a stall or tracking block the input guard re-arms only on a continuously neutral hand,
        // like a person who lets go before reaching again. The runner never forces the guard.
        static IEnumerator AwaitGuardRearmed()
        {
            var guard = task.controller.guards.First(g => g && g.side == (side == 0 ? Handedness.Left : Handedness.Right));
            if (guard.InteractionsAllowed && !guard.WaitingForNeutral) yield break;
            Record("guard_blocked_" + guard.LastReason);
            double until = EditorApplication.timeSinceStartup + 6;
            while (!guard.InteractionsAllowed || guard.WaitingForNeutral)
            {
                Require(EditorApplication.timeSinceStartup < until, "Input guard did not re-arm after a neutral hold: " + guard.LastReason);
                ResearchFlowVisualInput.SetControllerWorldPose(side, ResearchFlowVisualInput.ControllerWorldPose(side));
                yield return null;
            }
            Record("guard_rearmed");
        }

        static IEnumerator Acquire(ResearchS1FanPart part, bool usePinch)
        {
            step = "acquire:" + part.stableId; Record("before");
            Require(!selected && !part.IsHeld && hand.State != InteractorState.Select, "Input ownership must be neutral before acquisition.");
            yield return AwaitGuardRearmed();
            pinch = usePinch;
            var collider = part.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger &&
                    c.gameObject.activeInHierarchy && c.attachedRigidbody == part.body)
                .OrderBy(c => Vector3.Distance(c.bounds.center, task.controller.origin.Camera.transform.position)).First();
            var pose = ResearchFlowVisualInput.ControllerWorldPose(side);
            Vector3 approachTarget = collider.bounds.center;
            pose.position += approachTarget - (pinch ? hand.PinchPoint.position : hand.PalmPoint.position);
            ResearchFlowVisualInput.SetControllerWorldPose(side, pose);
            yield return AwaitObservation(() => Vector3.Distance(approachTarget, pinch ? hand.PinchPoint.position : hand.PalmPoint.position) <= .003f,
                "neutral hand approach");
            Require(hand.State != InteractorState.Select, "Neutral approach unexpectedly selected an object.");
            selected = part;
            Send(ResearchFlowVisualInput.ControllerWorldPose(side));
            double until = EditorApplication.timeSinceStartup + 3;
            while (!Owns(part))
            {
                if (hand.State == InteractorState.Select && hand.Interactable != part.interactable)
                    throw new InvalidOperationException("SDK selected a different interactable. No forced selection permitted.");
                if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("SDK did not select " + part.stableId);
                yield return null;
            }
            Record("sdk-selected-before-settle");
            yield return AwaitObservation(SdkSettled, "initial SDK grab movement", part);
            Record("selected-settled");
        }

        static IEnumerator Rotate(ResearchS1FanPart part, float degrees)
        {
            step = "rotate:" + part.stableId; Record("before");
            float remaining = degrees;
            float startTurn = part.Pose.turnDeg, startProgress = part.ThreadProgressDegrees, sent = 0;
            while (Mathf.Abs(remaining) > .001f)
            {
                Require(Owns(part), "Selection ownership lost during rotation.");
                float delta = Mathf.Clamp(remaining, -12, 12);
                Send(Orbit(ResearchFlowVisualInput.ControllerWorldPose(side), part.seat.position, part.seat.forward, delta));
                remaining -= delta; sent += delta;
                float expectedTurn = startTurn + sent;
                float expectedProgress = Mathf.Clamp(startProgress - sent * (part.rightHanded ? 1 : -1), 0, part.fullTurnDegrees);
                yield return AwaitObservation(() => SdkSettled() && (part.IsThread
                    ? Mathf.Abs(part.ThreadProgressDegrees - expectedProgress) < .05f &&
                    (expectedProgress > .01f || Mathf.Abs(remaining) > .001f || part.ThreadState == S1ThreadState.Absent) &&
                    (expectedProgress < part.fullTurnDegrees - .01f || Mathf.Abs(remaining) > .001f || part.ThreadState == S1ThreadState.Tight)
                    : Mathf.Abs(Mathf.DeltaAngle(part.Pose.turnDeg, expectedTurn)) < .25f), "observed part rotation endpoint", part);
            }
            Require(Owns(part), "Selection ownership lost at rotation endpoint.");
            Record("after");
        }

        static IEnumerator WithdrawAndPark(ResearchS1FanPart part, float outwardMetres, float lateralMetres)
        {
            step = "withdraw:" + part.stableId; Record("before");
            yield return Translate(part, -part.seat.forward * outwardMetres);
            Require(part.Placement == S1Placement.NotInstalled, "Actual part did not withdraw. Collision or kinematic stop remains.");
            step = "park:" + part.stableId;
            yield return Translate(part, part.seat.right * lateralMetres);
            yield return Release();
            Require(part.Placement == S1Placement.NotInstalled && !part.IsHeld, "Removed part did not retain a released uninstalled pose.");
            Record("after");
        }

        static IEnumerator ReturnToSeat(ResearchS1FanPart part, float depthMm)
        {
            step = "reinsert:" + part.stableId; Record("before");
            Vector3 lateral = Vector3.ProjectOnPlane(part.seat.position - part.mate.position, part.seat.forward);
            if (lateral.sqrMagnitude > .00000001f) yield return Translate(part, lateral);
            Vector3 axial = part.seat.forward * ((part.Pose.depthMm - depthMm) * .001f);
            if (axial.sqrMagnitude > .00000001f) yield return Translate(part, axial);
            yield return AwaitObservation(() => part.Pose.lateralMm <= part.positionToleranceMm &&
                part.Pose.tiltDeg <= part.angleToleranceDegrees && Mathf.Abs(part.Pose.depthMm - depthMm) <= .3f,
                "observed part insertion endpoint; no state assignment", part);
            Record("after");
        }

        static IEnumerator Translate(ResearchS1FanPart part, Vector3 displacement)
        {
            int steps = Mathf.CeilToInt(displacement.magnitude / .01f);
            Vector3 increment = displacement / steps;
            Vector3 before = part.transform.position;
            for (int i = 0; i < steps; i++)
            {
                Require(Owns(part), "Selection ownership lost during translation.");
                var pose = ResearchFlowVisualInput.ControllerWorldPose(side);
                pose.position += increment; Send(pose);
                yield return AwaitObservation(SdkSettled, "SDK translation movement", part);
            }
            yield return AwaitObservation(() => SdkSettled() && Vector3.Distance(part.transform.position, before + displacement) < .025f,
                "observed part translation endpoint; no collision bypass", part);
        }

        static IEnumerator Release()
        {
            var prior = selected;
            ResearchFlowVisualInput.SetControllerWorldPose(side, ResearchFlowVisualInput.ControllerWorldPose(side));
            yield return AwaitObservation(() => hand.State != InteractorState.Select && (!prior || !prior.IsHeld), "actual SDK release");
            selected = null; Record("released");
        }

        static bool SdkSettled()
        {
            if (hand.Movement == null || !hand.Movement.Stopped) return false;
            var target = hand.GetHandGrabPose();
            return Vector3.Distance(hand.Movement.Pose.position, target.position) <= .001f &&
                Quaternion.Angle(hand.Movement.Pose.rotation, target.rotation) <= .1f;
        }

        static IEnumerator AwaitObservation(Func<bool> observed, string action, ResearchS1FanPart heldPart = null)
        {
            double started = EditorApplication.timeSinceStartup, stableSince = -1;
            int issuedFrame = Time.frameCount;
            while (true)
            {
                if (heldPart) Require(Owns(heldPart), "Selection ownership lost while waiting for " + action);
                double now = EditorApplication.timeSinceStartup;
                bool satisfied = observed();
                if (!satisfied) stableSince = -1;
                else if (stableSince < 0) stableSince = now;
                if (ObservationReady(Time.frameCount, issuedFrame, now - started, stableSince < 0 ? 0 : now - stableSince, satisfied)) yield break;
                if (now - started >= 3) throw new TimeoutException("Observed SDK/part endpoint did not settle within 3 seconds: " + action);
                yield return null;
            }
        }

        internal static bool ObservationReady(int frame, int issuedFrame, double elapsed, double stableElapsed, bool satisfied)
            => frame > issuedFrame && elapsed >= .05 && stableElapsed >= .03 && satisfied;

        internal static bool ReleaseObserved(int frame, int notBeforeFrame, bool playing, bool paused, bool neutral)
            => frame >= notBeforeFrame && playing && !paused && neutral;

        static void Send(Pose pose) => ResearchFlowVisualInput.SetControllerWorldPose(side, pose, pinch ? 0 : 1, pinch ? 1 : 0);
        static bool Owns(ResearchS1FanPart part) => part && hand && OwnsSelection(part.interactable.GetInstanceID(),
            hand.Interactable ? hand.Interactable.GetInstanceID() : 0, part.grabbable.SelectingPointsCount,
            part.IsHeld, hand.State == InteractorState.Select);

        internal static bool OwnsSelection(int expected, int actual, int count, bool held, bool selecting)
            => expected != 0 && expected == actual && count == 1 && held && selecting;

        internal static Pose Orbit(Pose controllerPose, Vector3 center, Vector3 axis, float degrees)
        {
            if (float.IsNaN(axis.sqrMagnitude) || axis.sqrMagnitude < .99f || axis.sqrMagnitude > 1.01f ||
                float.IsNaN(degrees) || float.IsInfinity(degrees) || Mathf.Abs(degrees) > 12.001f)
                throw new ArgumentOutOfRangeException(nameof(degrees));
            var delta = Quaternion.AngleAxis(degrees, axis);
            return new Pose(center + delta * (controllerPose.position - center),
                delta * controllerPose.rotation);
        }

        static IEnumerator WaitFrames(int count)
        {
            int until = Time.frameCount + count;
            while (Time.frameCount < until) yield return null;
        }

        static IEnumerator Capture(string label)
        {
            step = label;
            yield return AwaitObservation(() => !selected && hand.State != InteractorState.Select, "stable released capture state");
            if (!captureFrames) { Record("capture-omitted-data-run"); yield break; }
            Record("capture-request");
            string path = Path.Combine(output, label + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return WaitFrames(3);
            double until = EditorApplication.timeSinceStartup + 5;
            while (!File.Exists(path))
            {
                if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("Capture file was not produced.");
                yield return null;
            }
            Record("capture-written");
        }

        static object Part(ResearchS1FanPart part) => new
        {
            part.stableId, held = part.IsHeld, placement = part.Placement.ToString(), thread = part.ThreadState.ToString(),
            progress = part.ThreadProgressDegrees, clip_closed = part.ClipClosed,
            position = V(part.transform.position), rotation = Q(part.transform.rotation),
            pose = part.seat && part.mate ? new { part.Pose.depthMm, part.Pose.lateralMm, part.Pose.tiltDeg, part.Pose.turnDeg } : null
        };
        static object V(Vector3 v) => new { v.x, v.y, v.z };
        static object Q(Quaternion q) => new { q.x, q.y, q.z, q.w };
        public static object Audit() => new
        {
            status = Status, running = Running, step, error, output, session, run, cleanup_observed = cleanupObserved,
            evidence_scope = "Editor SimulatedController component interaction only. Initial DEV and TaskB-active state arranged by root. Repair and reassembly are observed only on successful completion. No full workflow, inspection-pass or hardware claim.",
            task_state = task ? task.Capture() : null,
            hand_state = hand ? hand.State.ToString() : null,
            selected_target = hand && hand.Interactable ? hand.Interactable.transform.parent.name : null
        };
        static void Record(string observation)
        {
            File.AppendAllText(Path.Combine(output, "observations.jsonl"), JsonConvert.SerializeObject(new
            {
                observation, step, frame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble, session, run,
                input_source = "SimulatedController", test_data = true, task_state = task.Capture(),
                parts = new[] { task.frontClip, task.frontGuard, task.spinner, task.blade, task.rearLock }.Select(Part).ToArray(),
                controller = new { position = V(ResearchFlowVisualInput.ControllerWorldPose(side).position), rotation = Q(ResearchFlowVisualInput.ControllerWorldPose(side).rotation) },
                hand_state = hand.State.ToString(), selected_id = hand.Interactable ? hand.Interactable.GetInstanceID() : 0,
                sdk_movement = hand.Movement == null ? null : new { type = hand.Movement.GetType().Name, stopped = hand.Movement.Stopped,
                    position = V(hand.Movement.Pose.position), rotation = Q(hand.Movement.Pose.rotation),
                    target_position = V(hand.GetHandGrabPose().position), target_rotation = Q(hand.GetHandGrabPose().rotation) }
            }) + Environment.NewLine);
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void PlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) BeforeReload(); }
        static void BeforeReload() { if (Running) { Stop(); if (Running) Complete(); } }
        public static void Stop() { if (Running) Finish(new OperationCanceledException("Stopped before bounded proof completed.")); }
        static void Finish(Exception failure)
        {
            if (!Running) return;
            EditorApplication.update -= Tick;
            error = failure == null ? null : failure.ToString();
            Status = "ReleasingInput";
            try
            {
                if (ResearchFlowVisualInput.Running)
                    ResearchFlowVisualInput.SetControllerWorldPose(side, ResearchFlowVisualInput.ControllerWorldPose(side));
            }
            catch (Exception cleanup) { error = (error ?? "") + "\nINPUT_RELEASE_FAILED: " + cleanup; }
            routines.Clear();
            cleanupFrame = Time.frameCount + 3;
            cleanupDeadline = EditorApplication.timeSinceStartup + 3;
            EditorApplication.update -= CleanupTick;
            if (EditorApplication.isPlaying && !EditorApplication.isCompiling) EditorApplication.update += CleanupTick;
            else Complete();
        }
        static void CleanupTick()
        {
            if (Time.frameCount < cleanupFrame && EditorApplication.timeSinceStartup < cleanupDeadline) return;
            cleanupObserved = ReleaseObserved(Time.frameCount, cleanupFrame, EditorApplication.isPlaying, EditorApplication.isPaused,
                hand && hand.State != InteractorState.Select && (!selected || !selected.IsHeld));
            if (!cleanupObserved && EditorApplication.timeSinceStartup < cleanupDeadline) return;
            Complete();
        }
        static void Complete()
        {
            Running = false;
            EditorApplication.update -= CleanupTick;
            EditorApplication.playModeStateChanged -= PlayState;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            if (!cleanupObserved) error = (error ?? "") + "\nInput release was not observed before cleanup ended.";
            Status = error == null ? "BOUNDED_B_REPAIR_REASSEMBLY_OBSERVED" : "FAILED";
            File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(Audit(), Formatting.Indented));
        }
    }
}
#endif
