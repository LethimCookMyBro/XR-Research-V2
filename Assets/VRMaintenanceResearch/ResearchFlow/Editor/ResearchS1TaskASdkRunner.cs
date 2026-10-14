#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1TaskASdkRunner
    {
        static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        static readonly List<string> completed = new List<string>();
        static ResearchS1TaskAMechanics task;
        static HandGrabInteractor hand;
        static ResearchS1TaskAInput selected;
        static StreamWriter observations;
        static string output, participant, session, run, scene, layout, step, error, storage = "S01";
        static int side, lastFrame, cleanupFrame;
        static bool pinch, cleanupObserved;
        static double deadline, cleanupDeadline;
        static float yawPositive, yawNegative, requestedYaw, observedYaw;
        public static bool Running { get; private set; }
        public static string Status { get; private set; } = "NotStarted";

        public static string Begin(string evidenceDirectory, string expectedSessionId, string expectedRunId, int controllerSide = 1, string storageId = "S01")
        {
            if (storageId != "S01" && storageId != "S02" && storageId != "S03") throw new ArgumentOutOfRangeException(nameof(storageId));
            storage = storageId;
            if (Running || ResearchS1TaskBSdkRunner.Running) throw new InvalidOperationException("An SDK runner already owns input.");
            if (controllerSide < 0 || controllerSide > 1) throw new ArgumentOutOfRangeException(nameof(controllerSide));
            task = UnityEngine.Object.FindObjectsByType<ResearchS1TaskAMechanics>(FindObjectsSortMode.None).Single();
            side = controllerSide; session = expectedSessionId; run = expectedRunId; participant = null;
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path; layout = task.layout;
            CheckContext();
            if (UnityEngine.Object.FindObjectsByType<HandGrabInteractor>(FindObjectsSortMode.None).Any(h => h.isActiveAndEnabled && h.State == InteractorState.Select))
                throw new InvalidOperationException("Both SDK hands must be neutral before starting.");
            hand = UnityEngine.Object.FindObjectsByType<HandGrabInteractor>(FindObjectsSortMode.None)
                .Single(h => h.isActiveAndEnabled && h.Hand != null && h.Hand.Handedness == (side == 0 ? Handedness.Left : Handedness.Right));
            if (task.inputs.Any(i => i.pointable.SelectingPointsCount != 0) || task.ram.Concat(task.gpus).Any(p => p.Socket) ||
                task.ramSlots.Concat(task.gpuSlots).Any(s => s.Occupant || s.State != SocketState.Empty || s.latches.Any(l => !l.Closed)) ||
                task.sleds.Any(s => s.Tray >= 0))
                throw new InvalidOperationException("Expected a fresh actual Task A assembly with empty sockets, closed latches and loose SSD sleds.");
            output = FreshPath(evidenceDirectory, Path.GetDirectoryName(Application.dataPath));
            participant = task.controller.Record.participant_id;
            Directory.CreateDirectory(output);
            observations = new StreamWriter(new FileStream(Path.Combine(output, "observations.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            error = null; selected = null; step = "initial"; lastFrame = -1; cleanupObserved = false;
            yawPositive = yawNegative = requestedYaw = observedYaw = 0;
            completed.Clear(); routines.Clear(); routines.Push(Run());
            deadline = EditorApplication.timeSinceStartup + 400; // Inside the 420 s S1 active task limit.
            Running = true; Status = "Running";
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayState;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            try { Record("begin"); }
            catch (Exception failure) { Finish(failure); throw; }
            return Audit();
        }

        static void CheckContext()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || !Application.isFocused || !ResearchFlowVisualInput.Running || !task ||
                !task.AuthoredBindingsValid() || !task.controller || task.controller.Record == null || !task.controller.Record.test_data ||
                string.IsNullOrEmpty(task.controller.Record.participant_id) || !task.controller.Record.participant_id.StartsWith("DEV", StringComparison.Ordinal) ||
                task.controller.Flow == null || !task.controller.Flow.IsTaskActive || task.controller.Flow.Paused || task.controller.Flow.CurrentTask != "A" ||
                task.controller.Logger == null || task.controller.Logger.State != HandsLabLogState.Active || !task.port || !task.port.demoMode || !task.port.InteractionAllowed ||
                !IdentityMatches(session, run, task.controller.Record.session_id, task.controller.Record.run_id, task.controller.Logger.RunId) ||
                (participant != null && participant != task.controller.Record.participant_id) || layout != task.layout ||
                scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene().path)
                throw new InvalidOperationException("Requires focused unpaused active A, exact DEV session/run/logger identity, a running simulator and unchanged production interaction gates. No automatic resume.");
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Task A SDK driver exceeded 400 seconds.");
                CheckContext();
                if (ResearchS1TaskBSdkRunner.Running) throw new InvalidOperationException("Another SDK runner took input ownership.");
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
            var pivot = task.servicePivot;
            var initialPivot = new Pose(pivot.position, pivot.rotation);
            yield return Acquire(task.serviceYaw.input, false);
            yield return RotateService(360);
            Require(Quaternion.Angle(pivot.rotation, initialPivot.rotation) < .25f && Vector3.Distance(pivot.position, initialPivot.position) < .0001f,
                "The positive service revolution did not return the accepted workpiece pose.");
            Phase("service_yaw_positive_360_observed");
            yield return RotateService(-360);
            yield return Release();
            Require(Quaternion.Angle(pivot.rotation, initialPivot.rotation) < .25f && Vector3.Distance(pivot.position, initialPivot.position) < .0001f,
                "The released service handle did not retain the original accepted workpiece pose.");
            Phase("service_yaw_negative_360_and_release_observed");
            var sled = task.sleds.Single(s => s.part.stableId == storage);
            var originalSsd = new Pose(sled.part.transform.position, sled.part.transform.rotation);
            yield return Acquire(sled.input, false);
            yield return Translate(sled.input, Vector3.up * .02f);
            Require(sled.Tray < 0 && sled.Placement == S1Placement.NotInstalled, storage + " lift unexpectedly installed the sled.");
            Phase(storage + "_lift_20mm_observed");
            var acceptedPart = new Pose(sled.part.transform.position, sled.part.transform.rotation);
            var acceptedController = ResearchFlowVisualInput.ControllerWorldPose(side);
            step = "unsupported-jump:S01"; Record("before");
            Send(new Pose(acceptedController.position + Vector3.up, acceptedController.rotation));
            yield return AwaitObservation(() => SdkSettled() && SamePose(sled.input.transform, acceptedPart, .0005f, .1f), "unsupported one-metre S01 jump rejected", sled.input);
            Require(sled.Tray < 0 && sled.Placement == S1Placement.NotInstalled, "Rejected S01 jump changed installation state.");
            Record("jump-rejection-observed");
            Send(acceptedController);
            yield return AwaitObservation(() => SdkSettled() && SamePose(sled.input.transform, acceptedPart, .0005f, .1f), "controller returned to accepted held input", sled.input);
            yield return Translate(sled.input, Vector3.down * .02f);
            yield return Release();
            Require(SamePose(sled.input.transform, originalSsd, .00075f, .25f) && sled.Tray < 0 && sled.Placement == S1Placement.NotInstalled,
                storage + " did not retain its original loose pose after actual input return and release.");
            Phase(storage + "_jump_rejected_return_and_release_observed");
            var m01 = task.ram.Single(p => p.stableId == "M01");
            var a2 = task.ramSlots.Single(s => s.seatName == "DIMM_A2");
            yield return OpenLatch(a2);
            yield return ApproachRam(m01, a2, 5);
            yield return Release();
            Require(m01.Socket == a2 && a2.State == SocketState.Partial && !a2.Retained && !a2.latches.Single().Closed,
                "A released five-millimetre RAM partial seat was incorrectly retained or completed.");
            Phase("M01_DIMM_A2_partial_release_observed");
            yield return Acquire(Input(m01), false);
            yield return Translate(Input(m01), a2.seat.forward * .005f);
            yield return Release();
            Require(RamRetained(m01, a2), "M01 did not retain its actual full seat and automatic Q-DIMM latch.");
            Phase("M01_DIMM_A2_full_seat_auto_latch_observed");
            yield return Acquire(Input(m01), false);
            var seated = new Pose(m01.transform.position, m01.transform.rotation);
            var heldController = ResearchFlowVisualInput.ControllerWorldPose(side);
            step = "retained-pull:M01"; Record("before");
            Send(new Pose(heldController.position - a2.seat.forward * .01f, heldController.rotation));
            yield return AwaitObservation(() => SdkSettled() && SamePose(m01.transform, seated, .0005f, .1f) && RamRetained(m01, a2), "actual latched RAM pull rejected", Input(m01));
            Record("retained-pull-observed");
            Send(heldController);
            yield return AwaitObservation(() => SdkSettled() && SamePose(m01.transform, seated, .0005f, .1f), "retained RAM input returned", Input(m01));
            yield return Release();
            Phase("M01_DIMM_A2_retained_pull_and_release_observed");
            yield return OpenLatch(a2);
            Require(!a2.Retained && m01.Socket == a2, "Opening the actual latch did not remove RAM retention.");
            yield return Acquire(Input(m01), false);
            float withdrawal = (a2.spec.approachMm + a2.spec.hysteresisMm + 10) * .001f;
            yield return Translate(Input(m01), -a2.seat.forward * withdrawal);
            Require(!m01.Socket && !a2.Occupant && a2.State == SocketState.Empty, "Unlatched RAM did not physically leave the socket.");
            Phase("M01_DIMM_A2_manual_unlatch_and_removal_observed");
            yield return Translate(Input(m01), a2.seat.forward * withdrawal);
            yield return Release();
            Require(RamRetained(m01, a2), "M01 did not retain its actual reinstalled seat and latch.");
            Phase("M01_DIMM_A2_reinstallation_observed");
            var m02 = task.ram.Single(p => p.stableId == "M02");
            var b2 = task.ramSlots.Single(s => s.seatName == "DIMM_B2");
            yield return OpenLatch(b2);
            yield return ApproachRam(m02, b2, 0);
            yield return Release();
            Require(RamRetained(m01, a2) && RamRetained(m02, b2), "Released M01/M02 did not retain the actual A2/B2 seats.");
            Phase("M02_DIMM_B2_full_seat_auto_latch_observed");
            yield return InstallSled(sled);
            yield return BoundedGpuAndCableInputs(sled.part);
            Record("bounded-sequence-observed-inspection-not-invoked");
        }

        static IEnumerator InstallSled(ResearchS1Sled sled)
        {
            yield return Acquire(sled.input, false);
            var seat = task.traySeats[0];
            var mateInRoot = sled.part.transform.worldToLocalMatrix * sled.mate.localToWorldMatrix;
            var entry = seat.localToWorldMatrix * Matrix4x4.Translate(new Vector3(.006f, 0, -.020f)) * mateInRoot.inverse;
            var target = new Pose(entry.GetColumn(3), entry.rotation);
            float clearance = Mathf.Max(sled.part.transform.position.y, target.position.y,
                task.fixedAssembly[0].GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).Max(c => c.bounds.max.y)) + .15f;
            step = storage + "-clearance";
            yield return MoveRoot(sled.input, new Pose(new Vector3(sled.part.transform.position.x, clearance, sled.part.transform.position.z), sled.part.transform.rotation));
            step = storage + "-orient";
            yield return MoveRoot(sled.input, new Pose(sled.part.transform.position, target.rotation));
            var outward = -seat.forward;
            var absOutward = new Vector3(Mathf.Abs(outward.x), Mathf.Abs(outward.y), Mathf.Abs(outward.z));
            float caseExit = task.fixedAssembly[0].GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger)
                .Max(c => Vector3.Dot(c.bounds.center - target.position, outward) + Vector3.Dot(c.bounds.extents, absOutward));
            float sledNear = sled.part.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger)
                .Min(c => Vector3.Dot(c.bounds.center - sled.part.transform.position, outward) - Vector3.Dot(c.bounds.extents, absOutward));
            var outside = target.position + outward * (Mathf.Max(0, caseExit - sledNear) + .15f);
            step = storage + "-over-entry";
            yield return MoveRoot(sled.input, new Pose(new Vector3(outside.x, clearance, outside.z), target.rotation));
            step = storage + "-outside-entry";
            yield return MoveRoot(sled.input, new Pose(outside, target.rotation));
            step = storage + "-entry";
            yield return MoveRoot(sled.input, target);
            step = storage + "-hook";
            yield return Translate(sled.input, seat.forward * .020f);
            yield return AwaitObservation(() => sled.Tray == 0 && sled.Hooked, "actual S01 six-millimetre hook entry", sled.input);
            step = storage + "-slide";
            yield return Translate(sled.input, -seat.right * .006f);
            yield return Release();
            Require(sled.Tray == 0 && sled.Placement == S1Placement.Seated, "Released S01 did not retain its actual tray-1 seat.");
            Phase(storage + "_tray_1_seated_and_release_observed");
            var thumb = sled.thumb;
            yield return Acquire(thumb.input, true);
            var thumbMate = thumb.transform.worldToLocalMatrix * thumb.mate.localToWorldMatrix;
            step = storage + "-tighten-captive-thumb";
            while (thumb.ProgressDegrees < thumb.FullDegrees - .01f)
            {
                float next = Mathf.Min(thumb.FullDegrees, thumb.ProgressDegrees + 5);
                var root = SocketRules.RootPose(thumb.captiveSeat.localToWorldMatrix,
                    (float)ResearchS1FanKinematics.ThreadDepthMm(next, thumb.FullDegrees, thumb.travelMm), -next, thumbMate);
                yield return MoveRoot(thumb.input, new Pose(root.GetColumn(3), root.rotation));
                Require(Mathf.Abs(thumb.ProgressDegrees - next) <= .25f, "Actual S01 thumb thread did not advance by the requested clockwise step.");
            }
            yield return Release();
            Require(sled.Tray == 0 && sled.Placement == S1Placement.Seated && thumb.State == S1ThreadState.Tight,
                "Released S01 or its actual captive fastener did not retain the observed completed mechanical state.");
            Phase(storage + "_tray_fastener_tight_and_release_observed");
        }

        // shortcut: direct bracket/cable approaches are native-unverified, add measured waypoints after a guarded SDK trial rejects them.
        static IEnumerator BoundedGpuAndCableInputs(QAPlug storage)
        {
            var bound = task.sleds.Single(s => s.part == storage);
            Require(bound.Placement == S1Placement.Seated && bound.thumb.State == S1ThreadState.Tight && !storage.IsHeld,
                "The prior SSD input phase must have seated, fastened and released its bound target.");
            var gpu = task.gpus.Single(p => p.stableId == "G01");
            var socket = task.gpuSlots.Single(s => s.seatName == "PCIEX16_1");
            yield return OpenLatch(socket);
            yield return ApproachRam(gpu, socket, 0);
            yield return Release();
            Require(RamRetained(gpu, socket) && task.GpuSeated(0) && !gpu.IsHeld,
                "Released G01 did not retain its actual primary PCIe seat and latch.");
            Phase("G01_primary_seat_auto_latch_observed");
            var screw = task.gpuScrews.Single(s => s.input.stableId == "A.gpu_fastener1");
            Require(!screw.Captive && screw.SocketIndex < 0 && screw.ProgressDegrees == 0,
                "The GPU bracket screw must initially be loose and uncaptured.");
            var seat = task.gpuScrewSeats[0].localToWorldMatrix * Matrix4x4.Translate(new Vector3(0, 0, -.0009f));
            var mateInRoot = screw.transform.worldToLocalMatrix * screw.mate.localToWorldMatrix;
            yield return Acquire(screw.input, true);
            step = "G01-bracket-entry";
            var approach = SocketRules.RootPose(seat, screw.travelMm + 4, 0, mateInRoot);
            yield return MoveRoot(screw.input, new Pose(screw.transform.position, approach.rotation));
            yield return MoveRoot(screw.input, new Pose(approach.GetColumn(3), approach.rotation));
            var engaged = SocketRules.RootPose(seat, screw.travelMm, 0, mateInRoot);
            yield return MoveRoot(screw.input, new Pose(engaged.GetColumn(3), engaged.rotation));
            Require(screw.SocketIndex == 0 && screw.ProgressDegrees == 0 && screw.State == S1ThreadState.Absent,
                "Actual loose screw engagement at the primary bracket was not observed.");
            step = "G01-bracket-thread";
            yield return TightenThroughInput(screw);
            yield return Release();
            Require(screw.SocketIndex == 0 && screw.State == S1ThreadState.Tight && !screw.IsHeld && task.GpuSeated(0) &&
                task.Capture().gpus.Single(p => p.id == "G01").bracketFastener == S1ThreadState.Tight,
                "Released G01 and its actual primary bracket screw did not retain the tight state.");
            Phase("G01_primary_bracket_tight_observed");
            var connections = new[]
            {
                new { id = "A.sata_data", part = storage, connector = S1Connector.SsdData, sourcePartId = "A.motherboard", sourceConnector = S1Connector.MotherboardSata },
                new { id = "A.sata_power", part = storage, connector = S1Connector.SsdPower, sourcePartId = "A.psu", sourceConnector = S1Connector.PsuSata },
                new { id = "A.pcie_power", part = gpu, connector = S1Connector.GpuPcie8, sourcePartId = "A.psu", sourceConnector = S1Connector.PsuPcie8 }
            };
            foreach (var connection in connections)
            {
                var end = task.wiring.ends.Single(e => e.plug.stableId == connection.id);
                var target = task.wiring.targets.Single(t => t.part == connection.part && t.connector == connection.connector);
                var input = Input(end.plug);
                Require(!end.plug.Socket && end.sourcePartId == connection.sourcePartId && end.sourceConnector == connection.sourceConnector,
                    "The bound cable end must initially be loose with its exact source provenance.");
                yield return Acquire(input, true);
                var latch = end.plug.GetComponent<ResearchS1CableLatch>();
                if (connection.connector == S1Connector.GpuPcie8) Require(latch, "PCIe power requires its authored head latch.");
                if (latch) yield return AwaitObservation(() => !latch.latch.Closed, "actual cable head pinch opened its own latch", input);
                step = "cable-entry:" + connection.id;
                var mate = end.plug.mates.Single();
                var entry = SocketRules.RootPose(target.socket.seat.localToWorldMatrix, target.socket.spec.approachMm + 10, 0, end.plug.MateInRoot(mate));
                Require(task.wiring.Allows(input.transform, entry), "The proposed cable entry violates unchanged span constraints.");
                // Lift the plug clear of the case before turning it; rest poses sit beside the PSU and board.
                yield return MoveRoot(input, new Pose(OutsideCase(input.transform.position), input.transform.rotation));
                var through = TrayRoute(input, new Pose(entry.GetColumn(3), entry.rotation)).ToArray();
                var pass = through.Length > 0 ? through[0].rotation : entry.rotation;
                yield return MoveRoot(input, new Pose(input.transform.position, pass));
                var route = new[] { new Pose(OutsideCase(through.Length > 0 ? through[0].position : (Vector3)entry.GetColumn(3)), pass) }.Concat(through);
                foreach (var waypoint in route.ToArray())
                    yield return MoveRoot(input, waypoint);
                yield return MoveRoot(input, new Pose(entry.GetColumn(3), entry.rotation));
                step = "cable-insert:" + connection.id;
                yield return Translate(input, target.socket.seat.forward * (target.socket.spec.approachMm + 10) * .001f);
                yield return AwaitObservation(() => end.plug.Socket == target.socket && target.socket.Last.captured &&
                    target.socket.Last.stop == VRMaintenanceResearch.AssetLibraryQA.Stop.Seat && Mathf.Abs(target.socket.Last.depthMm) <= target.socket.spec.seatedDepthTolMm,
                    "actual bound cable target insertion", input);
                yield return Release();
                var observed = task.Capture().cables.Single(c => c.id == connection.id);
                Require(!end.plug.IsHeld && observed.sourceConnected && observed.targetConnected &&
                    observed.sourcePartId == connection.sourcePartId && observed.sourceConnector == connection.sourceConnector &&
                    observed.targetPartId == connection.part.stableId && observed.targetConnector == connection.connector,
                    "Released cable did not retain the exact source and target connection.");
                if (connection.connector == S1Connector.GpuPcie8) Require(target.socket.Retained && latch.latch.Closed,
                    "Released PCIe power did not retain its actual closed head latch.");
                Phase(connection.id + "_target_connection_observed");
            }
        }

        // Rear SSD connectors sit behind the motherboard tray, so the hand carries the plug through the nearest real routing slot,
        // turned about the tray normal when its entry orientation would not fit the slot width.
        static IEnumerable<Pose> TrayRoute(ResearchS1TaskAInput input, Pose entry)
        {
            var tray = task.fixedAssembly[0].GetComponentsInChildren<Transform>(true).Single(t => t.name == "COL_CASE_ATX_MID_Tray");
            Vector3 from = input.transform.position;
            float a = tray.InverseTransformPoint(from).y, b = tray.InverseTransformPoint(entry.position).y;
            if (Mathf.Sign(a) == Mathf.Sign(b)) yield break;
            var solid = (BoxCollider)input.GetComponentsInChildren<Collider>().Single(c => c.enabled && !c.isTrigger);
            var solidInRoot = Quaternion.Inverse(input.transform.rotation) * solid.transform.rotation;
            Func<Quaternion, float> width = rotation =>
            {
                var local = Quaternion.Inverse(tray.rotation) * rotation * solidInRoot;
                return Enumerable.Range(0, 3).Sum(k => { var axis = Vector3.zero; axis[k] = solid.size[k]; return Mathf.Abs((local * axis).x); });
            };
            var turned = Quaternion.AngleAxis(90, tray.up) * entry.rotation;
            var pass = width(entry.rotation) <= .016f ? entry.rotation : turned;
            Require(width(pass) <= .016f, "Plug does not fit a 20 mm routing slot in either entry orientation.");
            Vector3 rootFromSolid = pass * Quaternion.Inverse(input.transform.rotation) * (input.transform.position - solid.bounds.center);
            var slot = tray.Cast<Transform>().Where(t => t.name.StartsWith("S1_TrayRoutingSlot", StringComparison.Ordinal))
                .OrderBy(t => Vector3.Distance(from, t.position) + Vector3.Distance(t.position, entry.position)).First();
            step = "cable-route:" + input.stableId + ":" + slot.name;
            yield return new Pose(slot.position + tray.up * Mathf.Sign(a) * .04f + rootFromSolid, pass);
            yield return new Pose(slot.position + tray.up * Mathf.Sign(b) * .04f + rootFromSolid, pass);
        }

        static IEnumerator TightenThroughInput(ResearchS1TaskAFastener screw)
        {
            var mateInRoot = screw.transform.worldToLocalMatrix * screw.mate.localToWorldMatrix;
            while (screw.ProgressDegrees < screw.FullDegrees - .01f)
            {
                Require(Owns(screw.input), "Actual SDK thread ownership was lost.");
                float next = Mathf.Min(screw.FullDegrees, screw.ProgressDegrees + 5);
                var seat = task.gpuScrewSeats[screw.SocketIndex].localToWorldMatrix * Matrix4x4.Translate(new Vector3(0, 0, -.0009f));
                var expected = SocketRules.RootPose(seat, (float)ResearchS1FanKinematics.ThreadDepthMm(next, screw.FullDegrees, screw.travelMm), -next, mateInRoot);
                var goal = new Pose(expected.GetColumn(3), expected.rotation);
                Send(Follow(ResearchFlowVisualInput.ControllerWorldPose(side), new Pose(screw.transform.position, screw.transform.rotation), goal));
                yield return AwaitObservation(() => SdkSettled() && Mathf.Abs(screw.ProgressDegrees - next) <= .005f &&
                    SamePose(screw.transform, goal, .0003f, .1f), "observed bracket thread progress and axial pose", screw.input);
            }
        }


        static IEnumerator RotateService(float degrees)
        {
            step = degrees > 0 ? "service-yaw-positive" : "service-yaw-negative"; Record("before");
            float remaining = degrees;
            while (Mathf.Abs(remaining) > .001f)
            {
                var input = task.serviceYaw.input;
                Require(Owns(input), "Selection ownership lost during service rotation.");
                float before = task.servicePivot.eulerAngles.y;
                requestedYaw = Mathf.Clamp(remaining, -5, 5);
                Send(Orbit(ResearchFlowVisualInput.ControllerWorldPose(side), task.servicePivot.position, requestedYaw));
                yield return AwaitObservation(() => SdkSettled() && Mathf.Abs(Mathf.DeltaAngle(before, task.servicePivot.eulerAngles.y) - requestedYaw) <= .25f,
                    "accepted workpiece yaw step; collision and wiring guards unchanged", input);
                observedYaw = Mathf.DeltaAngle(before, task.servicePivot.eulerAngles.y);
                if (degrees > 0) yawPositive += observedYaw; else yawNegative += observedYaw;
                remaining -= requestedYaw;
                Record("yaw-step-observed");
            }
            Require(Mathf.Abs((degrees > 0 ? yawPositive : yawNegative) - degrees) <= 1, "Observed service yaw did not total the requested revolution.");
            Record("after");
        }

        static IEnumerator OpenLatch(QASocket socket)
        {
            var latch = socket.latches.Single();
            Require(latch.Closed, "Expected the actual Q-DIMM latch to be closed before manual opening.");
            var input = task.inputs.Single(i => i.latch == latch);
            yield return Acquire(input, true);
            yield return AwaitObservation(() => !latch.Closed, "actual Q-DIMM latch opened by SDK pinch", input);
            yield return Release();
            Require(!latch.Closed, "The released Q-DIMM latch did not remain open.");
        }

        static IEnumerator ApproachRam(QAPlug part, QASocket socket, float depthMm)
        {
            yield return Acquire(Input(part), false);
            var mate = part.mates.Single(m => part.MateName(m) == "Edge");
            var mateInRoot = part.MateInRoot(mate);
            var root = SocketRules.RootPose(socket.seat.localToWorldMatrix, socket.spec.approachMm + 10, 0, mateInRoot);
            var target = new Pose(root.GetColumn(3), root.rotation);
            float clearance = Mathf.Max(part.transform.position.y, target.position.y,
                task.fixedAssembly[0].GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).Max(c => c.bounds.max.y)) + .15f;
            step = "RAM-clearance:" + part.stableId;
            yield return MoveRoot(Input(part), new Pose(new Vector3(part.transform.position.x, clearance, part.transform.position.z), part.transform.rotation));
            step = "RAM-orient:" + part.stableId;
            yield return MoveRoot(Input(part), new Pose(part.transform.position, target.rotation));
            var outward = -socket.seat.forward;
            var absOutward = new Vector3(Mathf.Abs(outward.x), Mathf.Abs(outward.y), Mathf.Abs(outward.z));
            float caseExit = task.fixedAssembly[0].GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger)
                .Max(c => Vector3.Dot(c.bounds.center - target.position, outward) + Vector3.Dot(c.bounds.extents, absOutward));
            float ramNear = part.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger)
                .Min(c => Vector3.Dot(c.bounds.center - part.transform.position, outward) - Vector3.Dot(c.bounds.extents, absOutward));
            var outside = target.position + outward * (Mathf.Max(0, caseExit - ramNear) + .15f);
            step = "RAM-over-entry:" + part.stableId;
            yield return MoveRoot(Input(part), new Pose(new Vector3(outside.x, clearance, outside.z), target.rotation));
            step = "RAM-outside-entry:" + part.stableId;
            yield return MoveRoot(Input(part), new Pose(outside, target.rotation));
            step = "RAM-entry:" + part.stableId;
            yield return MoveRoot(Input(part), target);
            step = "RAM-insert:" + part.stableId;
            yield return Translate(Input(part), socket.seat.forward * (socket.spec.approachMm + 10 - depthMm) * .001f);
            yield return AwaitObservation(() => part.Socket == socket && socket.Last.captured && socket.Last.stop == VRMaintenanceResearch.AssetLibraryQA.Stop.Seat &&
                Mathf.Abs(socket.Last.depthMm - depthMm) <= socket.spec.seatedDepthTolMm, "actual RAM insertion depth", Input(part));
        }

        static bool RamRetained(QAPlug part, QASocket socket)
        {
            if (part.Socket != socket || socket.Occupant != part || socket.State != SocketState.Latched || !socket.Retained || !socket.Mate) return false;
            var pose = SocketRules.Measure(socket.seat.localToWorldMatrix, socket.Mate.localToWorldMatrix);
            return Mathf.Abs(pose.depthMm) <= socket.spec.seatedDepthTolMm && pose.lateralMm <= socket.spec.posTolMm && pose.tiltDeg <= socket.spec.angTolDeg;
        }

        static ResearchS1TaskAInput Input(QAPlug part) => task.inputs.Single(i => i.plug == part);

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

        static IEnumerator Acquire(ResearchS1TaskAInput input, bool usePinch)
        {
            step = "acquire:" + input.stableId; Record("before");
            Require(!selected && input.pointable.SelectingPointsCount == 0 && hand.State != InteractorState.Select, "Input ownership must be neutral before acquisition.");
            pinch = usePinch;
            var body = input.GetComponent<Rigidbody>();
            var collider = input.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy && c.attachedRigidbody == body)
                .OrderBy(c => Vector3.Distance(c.bounds.center, task.controller.origin.Camera.transform.position)).First();
            var pose = ResearchFlowVisualInput.ControllerWorldPose(side);
            Vector3 approach = collider.bounds.center;
            pose.position += approach - (pinch ? hand.PinchPoint.position : hand.PalmPoint.position);
            // The hand first backs off whatever it last touched, then leaves the case toward the participant before reaching in again.
            yield return Escape();
            yield return AwaitGuardRearmed();
            foreach (var waypoint in new[] { OutsideCase(ResearchFlowVisualInput.ControllerWorldPose(side).position), Approach(input, pose.position), pose.position })
            while (Vector3.Distance(ResearchFlowVisualInput.ControllerWorldPose(side).position, waypoint) > .0005f)
            {
                Require(hand.State != InteractorState.Select, "Neutral approach unexpectedly selected an object.");
                var current = ResearchFlowVisualInput.ControllerWorldPose(side);
                var next = new Pose(Vector3.MoveTowards(current.position, waypoint, .01f), current.rotation);
                Vector3 point = (pinch ? hand.PinchPoint.position : hand.PalmPoint.position) + next.position - current.position;
                ResearchFlowVisualInput.SetControllerWorldPose(side, next);
                yield return AwaitObservation(() => Vector3.Distance(point, pinch ? hand.PinchPoint.position : hand.PalmPoint.position) <= .003f, "neutral SDK hand approach step");
            }
            // Close the loop on the hand point itself: open-loop controller travel accumulates hand-to-controller drift on long approaches.
            for (int i = 0; i < 8 && Vector3.Distance(approach, pinch ? hand.PinchPoint.position : hand.PalmPoint.position) > .002f; i++)
            {
                Require(hand.State != InteractorState.Select, "Neutral approach unexpectedly selected an object.");
                var current = ResearchFlowVisualInput.ControllerWorldPose(side);
                var correction = Vector3.ClampMagnitude(approach - (pinch ? hand.PinchPoint.position : hand.PalmPoint.position), .01f);
                ResearchFlowVisualInput.SetControllerWorldPose(side, new Pose(current.position + correction, current.rotation));
                int frame = Time.frameCount;
                yield return AwaitObservation(() => Time.frameCount > frame + 2, "neutral SDK hand correction step");
            }
            yield return AwaitObservation(() => Vector3.Distance(approach, pinch ? hand.PinchPoint.position : hand.PalmPoint.position) <= .003f, "neutral SDK hand approach");
            Require(hand.State != InteractorState.Select, "Neutral approach unexpectedly selected an object.");
            // A trigger pinch belongs to UI while the controller ray rests on a canvas (ResearchControllerGrabIntent).
            // Like a person looking down at the part, tip the controller until its ray leaves the panels, pinch point fixed.
            if (pinch)
            {
                var ray = task.controller.guards.First(g => g && g.side == (side == 0 ? Handedness.Left : Handedness.Right))
                    .interactionsRoot.GetComponentsInChildren<RayInteractor>(true).Single(r => r.name == "ControllerRayInteractor");
                for (int i = 0; i < 9 && ray.Candidate && ray.Candidate.GetComponentInParent<Canvas>(); i++)
                {
                    if (i == 0) Record("ray_over_ui_before_pinch");
                    var current = ResearchFlowVisualInput.ControllerWorldPose(side);
                    var pivot = hand.PinchPoint.position;
                    var tip = Quaternion.AngleAxis(10, Vector3.Cross(current.rotation * Vector3.forward, Vector3.up).normalized);
                    ResearchFlowVisualInput.SetControllerWorldPose(side, new Pose(pivot + tip * (current.position - pivot), tip * current.rotation));
                    int frame = Time.frameCount;
                    yield return AwaitObservation(() => Time.frameCount > frame + 2, "tip ray off UI before pinch");
                }
                Require(!(ray.Candidate && ray.Candidate.GetComponentInParent<Canvas>()), "Controller ray still rests on UI before pinch.");
            }
            selected = input; Send(ResearchFlowVisualInput.ControllerWorldPose(side));
            double until = EditorApplication.timeSinceStartup + 3;
            while (!Owns(input))
            {
                if (hand.State == InteractorState.Select && hand.Interactable != input.interactable)
                    throw new InvalidOperationException("SDK selected a different interactable. No forced selection permitted.");
                if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("SDK did not select " + input.stableId);
                yield return null;
            }
            Record("sdk-selected-before-settle");
            yield return AwaitObservation(SdkSettled, "initial SDK grab movement", input);
            Record("selected-settled");
        }

        // A released hand can rest in contact (e.g. in the GPU slot pocket), where the contact guard refuses motions that keep touching.
        // Probe a few directions and keep the first the hand point actually follows for 3 cm.
        static IEnumerator Escape()
        {
            var lateral = task.fixedAssembly[0].up;
            var toward = Vector3.Dot(task.controller.origin.Camera.transform.position - task.fixedAssembly[0].position, lateral) >= 0 ? lateral : -lateral;
            foreach (var direction in new[] { toward, Vector3.up, -task.fixedAssembly[0].right, task.fixedAssembly[0].right })
            {
                int moved = 0;
                for (; moved < 3; moved++)
                {
                    var current = ResearchFlowVisualInput.ControllerWorldPose(side);
                    Vector3 point = (pinch ? hand.PinchPoint.position : hand.PalmPoint.position) + direction * .01f;
                    ResearchFlowVisualInput.SetControllerWorldPose(side, new Pose(current.position + direction * .01f, current.rotation));
                    int frame = Time.frameCount;
                    while (Time.frameCount < frame + 4) yield return null;
                    if (Vector3.Distance(point, pinch ? hand.PinchPoint.position : hand.PalmPoint.position) > .003f)
                    {
                        ResearchFlowVisualInput.SetControllerWorldPose(side, current);
                        frame = Time.frameCount;
                        while (Time.frameCount < frame + 4) yield return null;
                        break;
                    }
                }
                if (moved == 3) yield break;
            }
            throw new InvalidOperationException("Released hand could not back away from contact in any probed direction.");
        }

        // Cable heads carry the source GRAB anchor and its authored approach (approach_local "+Y"); other parts come in from outside the case.
        static Vector3 Approach(ResearchS1TaskAInput input, Vector3 target)
        {
            var grab = input.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("GRAB_", StringComparison.Ordinal) && t.parent == input.transform);
            return grab && input.plug && input.plug.assetId.StartsWith("CABLE_", StringComparison.Ordinal) ? target + grab.up * .1f : OutsideCase(target);
        }

        static Vector3 OutsideCase(Vector3 point)
        {
            var bounds = task.fixedAssembly[0].GetComponentsInChildren<Renderer>().Where(r => r.enabled).Select(r => r.bounds)
                .Aggregate((a, b) => { a.Encapsulate(b); return a; });
            bounds.Expand(.12f);
            // Side panels are off, so leave along the case's lateral axis on the participant's side (imported case local Y is shell.up).
            var lateral = task.fixedAssembly[0].up;
            var toward = Vector3.Dot(task.controller.origin.Camera.transform.position - bounds.center, lateral) >= 0 ? lateral : -lateral;
            while (bounds.Contains(point)) point += toward * .01f;
            return point;
        }

        static IEnumerator Translate(ResearchS1TaskAInput input, Vector3 displacement)
        {
            yield return MoveRoot(input, new Pose(input.transform.position + displacement, input.transform.rotation));
        }

        static IEnumerator MoveRoot(ResearchS1TaskAInput input, Pose target)
        {
            Require(FinitePose(target), "Requested input endpoint is non-finite or has an invalid quaternion.");
            Record("motion-before");
            while (!SamePose(input.transform, target, .00075f, .25f))
            {
                Require(Owns(input), "Selection ownership lost during part movement.");
                var currentRoot = new Pose(input.transform.position, input.transform.rotation);
                var current = ResearchFlowVisualInput.ControllerWorldPose(side);
                float angle = Quaternion.Angle(currentRoot.rotation, target.rotation);
                float distance = Vector3.Distance(currentRoot.position, target.position) + angle * Mathf.Deg2Rad * Vector3.Distance(current.position, currentRoot.position);
                float fraction = distance > .01f ? .01f / distance : 1;
                if (angle * fraction > 5) fraction = 5 / angle;
                var expected = new Pose(Vector3.Lerp(currentRoot.position, target.position, fraction), Quaternion.Slerp(currentRoot.rotation, target.rotation, fraction));
                Send(Follow(current, currentRoot, expected));
                yield return AwaitObservation(() => SdkSettled() && SamePose(input.transform, expected, .002f, .5f), "observed part movement; no collision bypass", input);
            }
            Record("motion-after");
        }

        static Pose Follow(Pose source, Pose reference, Pose target)
        {
            var delta = target.rotation * Quaternion.Inverse(reference.rotation);
            return new Pose(target.position + delta * (source.position - reference.position), delta * source.rotation);
        }

        static Pose Orbit(Pose pose, Vector3 center, float degrees)
        {
            if (!FinitePose(pose) || float.IsNaN(degrees) || float.IsInfinity(degrees) || Mathf.Abs(degrees) > 5.001f)
                throw new ArgumentOutOfRangeException(nameof(degrees));
            var delta = Quaternion.AngleAxis(degrees, Vector3.up);
            return new Pose(center + delta * (pose.position - center), delta * pose.rotation);
        }

        static bool FinitePose(Pose pose)
        {
            float norm = pose.rotation.x * pose.rotation.x + pose.rotation.y * pose.rotation.y + pose.rotation.z * pose.rotation.z + pose.rotation.w * pose.rotation.w;
            return ResearchS1TaskAMechanics.Finite(Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one)) && norm >= .99f && norm <= 1.01f;
        }

        static bool SamePose(Transform actual, Pose expected, float metres, float degrees) =>
            actual && Vector3.Distance(actual.position, expected.position) <= metres && Quaternion.Angle(actual.rotation, expected.rotation) <= degrees;

        static IEnumerator Release()
        {
            var prior = selected;
            ResearchFlowVisualInput.SetControllerWorldPose(side, ResearchFlowVisualInput.ControllerWorldPose(side));
            yield return AwaitObservation(() => hand.State != InteractorState.Select && (!prior || prior.pointable.SelectingPointsCount == 0), "actual SDK release");
            selected = null; Record("released");
        }

        static bool SdkSettled()
        {
            if (hand.Movement == null || !hand.Movement.Stopped) return false;
            var target = hand.GetHandGrabPose();
            return Vector3.Distance(hand.Movement.Pose.position, target.position) <= .001f && Quaternion.Angle(hand.Movement.Pose.rotation, target.rotation) <= .1f;
        }

        static IEnumerator AwaitObservation(Func<bool> observed, string action, ResearchS1TaskAInput held = null)
        {
            double started = EditorApplication.timeSinceStartup, stableSince = -1;
            int issuedFrame = Time.frameCount;
            while (true)
            {
                if (held) Require(Owns(held), "Selection ownership lost while waiting for " + action);
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
        internal static bool OwnsSelection(int expected, int actual, int count, bool allowed, bool selecting)
            => expected != 0 && expected == actual && count == 1 && allowed && selecting;
        internal static bool IdentityMatches(string expectedSession, string expectedRun, string actualSession, string actualRun, string loggerRun)
            => !string.IsNullOrEmpty(expectedSession) && !string.IsNullOrEmpty(expectedRun) && expectedSession == actualSession && expectedRun == actualRun && expectedRun == loggerRun;

        static bool Owns(ResearchS1TaskAInput input) => input && hand && OwnsSelection(input.interactable.GetInstanceID(),
            hand.Interactable ? hand.Interactable.GetInstanceID() : 0, input.pointable.SelectingPointsCount, input.Allowed, hand.State == InteractorState.Select);
        static void Send(Pose pose) => ResearchFlowVisualInput.SetControllerWorldPose(side, pose, pinch ? 0 : 1, pinch ? 1 : 0);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Phase(string phase) { completed.Add(phase); Record("phase-observed"); }
        static object V(Vector3 v) => new { v.x, v.y, v.z };
        static object Q(Quaternion q) => new { q.x, q.y, q.z, q.w };

        internal static string FreshPath(string directory, string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory) || directory.StartsWith(@"\\", StringComparison.Ordinal))
                throw new ArgumentException("Use a fresh absolute local evidence directory.");
            string full = Path.GetFullPath(directory);
            if (full.StartsWith(@"\\", StringComparison.Ordinal) || !string.Equals(Path.GetPathRoot(directory).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
                Path.GetPathRoot(full).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Evidence path must include its absolute local root.");
            string project = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(project, StringComparison.OrdinalIgnoreCase) || full.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                Directory.Exists(full) || File.Exists(full)) throw new IOException("Evidence must be fresh and outside the Unity project.");
            for (var parent = Directory.GetParent(full); parent != null; parent = parent.Parent)
                if ((Directory.Exists(parent.FullName) || File.Exists(parent.FullName)) && (File.GetAttributes(parent.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Evidence parents cannot be reparse points.");
            return full;
        }

        public static string Audit() => JsonConvert.SerializeObject(new
        {
            status = Status, running = Running, step, error, output, participant, session, run, scene, layout,
            cleanup_observed = cleanupObserved, completed_phases = completed.ToArray(), observed_positive_yaw = yawPositive, observed_negative_yaw = yawNegative,
            evidence_scope = "Editor SimulatedController bounded component interaction only. Root arranges a technical DEV active-A context. No full workflow, Inspect or hardware acceptance. S01 seating/fastening, G01 primary seating/bracket fastening and three exact-source/target cable connections are bounded mechanical observations only.",
            task_state = task ? task.Capture() : null,
            hand_state = hand ? hand.State.ToString() : null, selected_target = selected ? selected.stableId : null
        }, Formatting.Indented);

        static void Record(string observation)
        {
            observations.WriteLine(JsonConvert.SerializeObject(new
            {
                observation, step, frame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble, editor_realtime = EditorApplication.timeSinceStartup,
                execution_environment = "Editor", input_source = "SimulatedController", expected_participant = participant, expected_session = session, expected_run = run,
                observed_participant = task && task.controller && task.controller.Record != null ? task.controller.Record.participant_id : null,
                observed_session = task && task.controller && task.controller.Record != null ? task.controller.Record.session_id : null,
                observed_run = task && task.controller && task.controller.Record != null ? task.controller.Record.run_id : null,
                observed_test_data = task && task.controller && task.controller.Record != null ? (bool?)task.controller.Record.test_data : null,
                logger_timestamp = task && task.controller && task.controller.Logger != null ? (double?)task.controller.Logger.Timestamp : null,
                logger_sequence = task && task.controller && task.controller.Logger != null ? (long?)task.controller.Logger.LastSequence : null,
                requested_yaw_step = requestedYaw, observed_yaw_step = observedYaw, yaw_positive = yawPositive, yaw_negative = yawNegative,
                task_state = task ? task.Capture() : null, completed_phases = completed.ToArray(),
                selected_id = hand && hand.Interactable ? hand.Interactable.GetInstanceID() : 0,
                selected_target = selected ? selected.stableId : null, hand_state = hand ? hand.State.ToString() : null,
                selected_input_allowed = selected && selected.Allowed,
                selected_world = selected ? new { position = V(selected.transform.position), rotation = Q(selected.transform.rotation) } : null,
                controller = ResearchFlowVisualInput.Running ? new { position = V(ResearchFlowVisualInput.ControllerWorldPose(side).position), rotation = Q(ResearchFlowVisualInput.ControllerWorldPose(side).rotation) } : null,
                sdk_movement = hand && hand.Movement != null ? new { stopped = hand.Movement.Stopped,
                    position = V(hand.Movement.Pose.position), rotation = Q(hand.Movement.Pose.rotation),
                    target_position = V(hand.GetHandGrabPose().position), target_rotation = Q(hand.GetHandGrabPose().rotation) } : null
            }));
            observations.Flush();
        }

        static void PlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) BeforeReload(); }
        static void BeforeReload() { if (Running) { Finish(new OperationCanceledException("Play exit or assembly reload interrupted the bounded sequence.")); if (Running) Complete(); } }
        public static string Stop() { if (Running) Finish(new OperationCanceledException("Stopped before the bounded sequence completed.")); return Audit(); }

        static void Finish(Exception failure)
        {
            if (!Running || Status == "ReleasingInput") return;
            EditorApplication.update -= Tick;
            error = failure == null ? null : failure.ToString(); Status = "ReleasingInput";
            try
            {
                if (ResearchFlowVisualInput.Running && task && task.controller && task.controller.Record != null && task.controller.Logger != null &&
                    IdentityMatches(session, run, task.controller.Record.session_id, task.controller.Record.run_id, task.controller.Logger.RunId))
                    ResearchFlowVisualInput.SetControllerWorldPose(side, ResearchFlowVisualInput.ControllerWorldPose(side));
                else error = (error ?? "") + "\nOwned input identity is unavailable. Neutral input was not sent to another session.";
            }
            catch (Exception cleanup) { error = (error ?? "") + "\nINPUT_RELEASE_FAILED: " + cleanup; }
            routines.Clear(); cleanupFrame = Time.frameCount + 3; cleanupDeadline = EditorApplication.timeSinceStartup + 3;
            EditorApplication.update -= CleanupTick;
            if (EditorApplication.isPlaying && !EditorApplication.isCompiling) EditorApplication.update += CleanupTick;
            else Complete();
        }

        static void CleanupTick()
        {
            if (Time.frameCount < cleanupFrame && EditorApplication.timeSinceStartup < cleanupDeadline) return;
            cleanupObserved = ReleaseObserved(Time.frameCount, cleanupFrame, EditorApplication.isPlaying, EditorApplication.isPaused,
                hand && hand.State != InteractorState.Select && (!selected || selected.pointable.SelectingPointsCount == 0));
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
            Status = error == null ? "BOUNDED_A_YAW_RAM_S01_GPU_AND_CABLES_OBSERVED" : "FAILED";
            try { Record("cleanup-ended"); }
            catch (Exception failure) { error = (error ?? "") + "\nFINAL_OBSERVATION_FAILED: " + failure; Status = "FAILED"; }
            try { if (observations != null) observations.Dispose(); }
            catch (Exception failure) { error = (error ?? "") + "\nOBSERVATION_CLOSE_FAILED: " + failure; Status = "FAILED"; }
            finally { observations = null; }
            try { File.WriteAllText(Path.Combine(output, "result.json"), Audit(), new UTF8Encoding(false)); }
            catch (Exception failure) { error = (error ?? "") + "\nRESULT_WRITE_FAILED: " + failure; Status = "FAILED"; throw; }
        }
    }
}
#endif
