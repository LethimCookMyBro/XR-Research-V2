using System;
using System.Linq;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
    [DefaultExecutionOrder(12000)]
    public sealed class ResearchS1TaskAMechanics : MonoBehaviour
    {
        public ResearchTaskPort port;
        public ResearchSessionFlowController controller;
        public Transform servicePivot;
        public string layout;
        public Transform[] fixedAssembly;
        public Vector3[] fixedPositions;
        public Quaternion[] fixedRotations;
        public QAPlug[] ram, ssds, gpus;
        public QASocket[] ramSlots, gpuSlots;
        public ResearchS1TaskAInput[] inputs;
        public Transform[] traySeats, gpuScrewSeats;
        public ResearchS1Sled[] sleds;
        public ResearchS1TaskAFastener[] gpuScrews;
        public ResearchS1TaskAWiring wiring;

        public ResearchS1ServiceYaw serviceYaw;
        // shortcut: collision geometry and service rotation remain unverified until actual SDK/Unity acceptance.

        public bool AllowsMotion(Transform moving, Matrix4x4 pose) => MotionBlockReason(moving, pose) == PartBlockReason.None;

        public PartBlockReason MotionBlockReason(Transform moving, Matrix4x4 pose)
        {
            if (inputs.Any(i => i != (serviceYaw ? serviceYaw.input : null) && i.transform != moving && i.transform.IsChildOf(moving) && i.pointable.SelectingPointsCount > 0))
                return PartBlockReason.OtherHeld;
            if (!wiring || !wiring.Allows(moving, pose)) return PartBlockReason.CableLimit;
            return ResearchS1HeldCollision.Allows(moving, pose) ? PartBlockReason.None : PartBlockReason.Contact;
        }

        private void Start() => Bind();

        public void Bind()
        {
            if (!AuthoredBindingsValid()) throw new InvalidOperationException("Task A authored bindings are incomplete or inconsistent.");
            port.BindSnapshot(() => Capture(), EvaluateSnapshot, Gate);
            port.BindFeedback(value => "inspect.s1.A." + ResearchS1TaskEvaluator.Evaluate((S1TaskAState)value).Category);
            port.BindExamineInfo((target, language) => ResearchS1TaskACatalog.Ids.Contains(target) ? ResearchS1TaskACatalog.Label(target)
                : target == "A.module" ? ResearchStrings.Get("examine.info.A.module", language) : null);
        }

        // Mirrors each option category in place (ResearchS1TaskACatalog.LayoutIndex) before any part has been touched.
        public void ApplyLayout(string target)
        {
            ResearchS1TaskACatalog.LayoutIndex(0, 1, target);
            if (target == layout) return;
            if (ram.Concat(gpus).Any(p => p.Socket || p.IsHeld) || sleds.Any(s => s.Tray >= 0 || s.part.IsHeld))
                throw new InvalidOperationException("Task A layout can only change before any option is handled.");
            foreach (var options in new[] { ram.Select(p => p.transform).ToArray(), gpus.Select(p => p.transform).ToArray(), sleds.Select(s => s.transform).ToArray() })
            {
                var poses = options.Select(t => (t.localPosition, t.localRotation)).ToArray();
                for (int i = 0; i < options.Length; i++) options[i].SetLocalPositionAndRotation(poses[options.Length - 1 - i].localPosition, poses[options.Length - 1 - i].localRotation);
            }
            layout = target;
            Physics.SyncTransforms();
        }

        public bool AuthoredBindingsValid()
        {
            if (!port || port.taskId != "A" || !controller || !servicePivot || (layout != "P" && layout != "Q") ||
                fixedAssembly == null || fixedPositions == null || fixedRotations == null || fixedAssembly.Length != 5 ||
                fixedPositions.Length != 5 || fixedRotations.Length != 5 || ramSlots == null || ramSlots.Length != 4 ||
                gpuSlots == null || gpuSlots.Length != 2 || inputs == null || inputs.Length != 25 ||
                traySeats == null || traySeats.Length != 2 || gpuScrewSeats == null || gpuScrewSeats.Length != 2 ||
                sleds == null || sleds.Length != 3 || gpuScrews == null || gpuScrews.Length != 2) return false;
            string[] names = { "CASE_ATX_MID", "MB_B760_DDR4", "CPU_LGA1700", "COOLER_LGA1700_STOCK", "PSU_ATX_MX650" };
            for (int i = 0; i < names.Length; i++)
                if (!fixedAssembly[i] || fixedAssembly[i].name != names[i] || fixedAssembly[i].parent != servicePivot) return false;
            if (!Parts(ram, new[] { "M01", "M02", "M03", "M04", "M05" }) || !Parts(ssds, new[] { "S01", "S02", "S03" }) ||
                !Parts(gpus, new[] { "G01", "G02" })) return false;
            string[] slots = { "DIMM_A1", "DIMM_A2", "DIMM_B1", "DIMM_B2", "PCIEX16_1", "PCIEX16_2" };
            var sockets = ramSlots.Concat(gpuSlots).ToArray();
            if (sockets.Distinct().Count() != sockets.Length || inputs.Distinct().Count() != inputs.Length) return false;
            for (int i = 0; i < sockets.Length; i++)
            {
                if (!sockets[i] || sockets[i].seatName != slots[i] || sockets[i].fixtureId != "A.motherboard" ||
                    !sockets[i].seat || sockets[i].seat.name != "SEAT_MB_B760_DDR4_" + slots[i] ||
                    !sockets[i].seat.IsChildOf(fixedAssembly[1]) || sockets[i].latches == null || sockets[i].latches.Count != 1) return false;
                var latch = sockets[i].latches[0];
                string nodeName = "MOV_MB_B760_DDR4_" + (i < 4 ? "DimmLatch" + (i + 1) : slots[i] + "_Latch");
                if (!latch || latch.socket != sockets[i] || latch.name != nodeName || !latch.transform.IsChildOf(fixedAssembly[1]) ||
                    latch.animationRoot != latch.gameObject || !latch.openClip || !latch.closeClip ||
                    inputs.Count(p => p && p.latch == latch && !p.plug && p.stableId == "A." + slots[i] + ".latch" && p.gameObject == latch.gameObject) != 1) return false;
            }
            foreach (var part in ram.Concat(ssds).Concat(gpus))
                if (inputs.Count(p => p && p.plug == part && !p.latch && p.stableId == part.stableId && p.pointable == part.grabbable && p.gameObject == part.gameObject) != 1) return false;
            for (int i = 0; i < 2; i++)
            {
                if (!traySeats[i] || traySeats[i].name != "SEAT_CASE_ATX_MID_Sled25_" + (i + 1) || !traySeats[i].IsChildOf(fixedAssembly[0]) ||
                    !gpuScrewSeats[i] || gpuScrewSeats[i].name != "SEAT_CASE_ATX_MID_SlotScrew" + (i == 0 ? 1 : 4) || !gpuScrewSeats[i].IsChildOf(fixedAssembly[0]) ||
                    !FastenerValid(gpuScrews[i], "A.gpu_fastener" + (i + 1), 5.1f) || gpuScrews[i].sled ||
                    !gpus[i].GetComponent<ResearchS1TaskAPlugMotion>() || gpus[i].GetComponent<ResearchS1TaskAPlugMotion>().plug != gpus[i]) return false;
            }
            for (int i = 0; i < 3; i++)
                if (!sleds[i] || sleds[i].owner != this || sleds[i].part != ssds[i] || sleds[i].input != ssds[i].GetComponent<ResearchS1TaskAInput>() ||
                    !sleds[i].input.customMotion || !sleds[i].mate || sleds[i].mate.name != "MATE_CASE_SLED_25_Tray" || !sleds[i].mate.IsChildOf(ssds[i].transform) ||
                    !FastenerValid(sleds[i].thumb, ssds[i].stableId + ".tray_fastener", 2.5f) || sleds[i].thumb.sled != sleds[i] ||
                    !sleds[i].thumb.captiveSeat || sleds[i].thumb.captiveSeat.parent != sleds[i].thumb.transform.parent) return false;
            return serviceYaw && serviceYaw.owner == this && serviceYaw.transform.parent == servicePivot &&
                serviceYaw.input && serviceYaw.input.stableId == "A.service_handle" && serviceYaw.input.pointable == serviceYaw.grabbable &&
                inputs.Count(p => p == serviceYaw.input) == 1 && wiring && wiring.AuthoredBindingsValid() && inputs.All(p => p && p.owner == this && p.interactable && p.interactable.transform.parent == p.transform && p.pointable && p.pointable.gameObject == p.gameObject);
        }

        private bool FastenerValid(ResearchS1TaskAFastener part, string id, float travel)
        {
            return part && part.owner == this && part.body && part.grabbable && part.mate && part.mate.IsChildOf(part.transform) &&
                part.travelMm == travel && part.input && part.input.pointable == part.grabbable && part.input.customMotion &&
                part.input.gameObject == part.gameObject && inputs.Count(p => p == part.input && p.stableId == id && !p.plug && !p.latch) == 1;
        }

        private static bool Parts(QAPlug[] parts, string[] ids)
        {
            if (parts == null || parts.Length != ids.Length) return false;
            for (int i = 0; i < ids.Length; i++)
                if (!parts[i] || parts[i].stableId != ids[i] || parts[i].assetId !=
                    (ids[i].StartsWith("S", StringComparison.Ordinal) ? "CASE_SLED_25" : ResearchS1TaskACatalog.Asset(ids[i])) ||
                    !parts[i].body || !parts[i].grabbable ||
                    (!ids[i].StartsWith("S", StringComparison.Ordinal) && !parts[i].GetComponent<ResearchS1TaskAPlugMotion>())) return false;
            return true;
        }

        public S1TaskAState Capture()
        {
            var state = new S1TaskAState { systemIntegrityValid = false, capturedAtSeconds = Time.realtimeSinceStartupAsDouble,
                workpieceYawDegrees = servicePivot ? servicePivot.localEulerAngles.y : 0 };
            if (!AuthoredBindingsValid()) return state;
            state.systemIntegrityValid = true;
            bool[] stable = fixedAssembly.Select((t, i) => t.gameObject.activeInHierarchy &&
                Vector3.Distance(t.localPosition, fixedPositions[i]) < .0005f && Quaternion.Angle(t.localRotation, fixedRotations[i]) < .1f).ToArray();
            state.baseState = new S1TaskABaseState { caseId = "A.case", motherboardId = "A.motherboard", cpuId = "A.cpu", coolerId = "A.cooler", psuId = "A.psu",
                caseIntact = stable[0], motherboardSecured = stable[1], cpuSeated = stable[2], coolerSecured = stable[3], psuSecured = stable[4] };
            state.ram = ram.Select((part, i) =>
            {
                var value = ResearchS1TaskACatalog.Ram(part.stableId);
                int slot = Array.IndexOf(ramSlots, part.Socket);
                if (slot >= 0) { value.placement = Placement(part); value.slot = (S1RamSlot)(slot + 1); value.retained = value.placement == S1Placement.Seated && part.Socket.Retained; }
                return value;
            }).ToArray();
            state.ssds = sleds.Select(sled =>
            {
                var value = ResearchS1TaskACatalog.Ssd(sled.part.stableId);
                value.placement = sled.Placement;
                value.mount = sled.Tray < 0 ? S1SsdMount.None : (S1SsdMount)(sled.Tray + 1);
                value.trayFastener = sled.thumb.State;
                return value;
            }).ToArray();
            state.gpus = gpus.Select(part =>
            {
                var value = ResearchS1TaskACatalog.Gpu(part.stableId);
                int slot = Array.IndexOf(gpuSlots, part.Socket);
                if (slot >= 0) { value.placement = Placement(part); value.slot = (S1GpuSlot)(slot + 1); value.retained = value.placement == S1Placement.Seated && part.Socket.Retained;
                    var screw = gpuScrews.FirstOrDefault(p => p.SocketIndex == slot && p.State != S1ThreadState.Absent);
                    value.bracketFastener = screw ? screw.State : S1ThreadState.Absent; }
                return value;
            }).ToArray();
            state.cables = Array.Empty<S1CableState>();
            state.assemblyObstructed = Application.isPlaying && (ram.Concat(ssds).Concat(gpus).Any(p => p.IsHeld) || gpuScrews.Any(p => p.IsHeld) || sleds.Any(p => p.thumb.IsHeld));
            var plugs = ram.Concat(gpus).ToArray();
            state.slots = ramSlots.Concat(gpuSlots).Select(s => new S1SlotState { id = s.seatName,
                occupantId = plugs.FirstOrDefault(p => p.Socket == s)?.stableId, latchClosed = s.latches.All(l => l.Closed) }).ToArray();
            bool play = Application.isPlaying;
            state.pending_attempts = plugs.Where(p => play && p.IsHeld || Placement(p) == S1Placement.Partial)
                .Select(p => new S1PendingAttempt { partId = p.stableId, targetId = (p.Socket ? p.Socket : p.HeldSocket)?.seatName, held = play && p.IsHeld, placement = Placement(p) })
                .Concat(sleds.Where(s => play && s.part.IsHeld || s.Placement == S1Placement.Partial).Select(s => new S1PendingAttempt { partId = s.part.stableId,
                    targetId = s.Tray < 0 ? null : "tray" + (s.Tray + 1), held = play && s.part.IsHeld, placement = s.Placement }))
                .Concat(gpuScrews.Where(f => play && f.IsHeld || f.State == S1ThreadState.EngagedLoose).Select(f => new S1PendingAttempt { partId = f.input.stableId,
                    targetId = f.SocketIndex < 0 ? null : gpuSlots[f.SocketIndex].seatName, held = play && f.IsHeld,
                    placement = f.State == S1ThreadState.Tight ? S1Placement.Seated : f.State == S1ThreadState.EngagedLoose ? S1Placement.Partial : S1Placement.NotInstalled }))
                .Concat(wiring.ends.Where(e => play && e.plug.IsHeld || e.plug.Socket && Placement(e.plug) == S1Placement.Partial).Select(e => new S1PendingAttempt { partId = e.plug.stableId,
                    targetId = (e.plug.Socket ? e.plug.Socket : e.plug.HeldSocket)?.seatName, held = play && e.plug.IsHeld, placement = Placement(e.plug) }))
                .ToArray();
            wiring.Capture(state);
            return state;
        }

        private static bool EvaluateSnapshot(object value)
        {
            var result = ResearchS1TaskEvaluator.Evaluate(value as S1TaskAState);
            if (result.Category == S1InspectionCategory.SystemError) throw new InvalidOperationException("Task A physical mechanics are incomplete; do not score this inspection.");
            return result.Passed;
        }

        private static S1Placement Placement(QAPlug part)
        {
            var socket = part.Socket;
            if (!socket || !socket.Mate) return S1Placement.NotInstalled;
            var pose = SocketRules.Measure(socket.seat.localToWorldMatrix, socket.Mate.localToWorldMatrix);
            bool seated = (socket.State == SocketState.Seated || socket.State == SocketState.Latched) &&
                Mathf.Abs(pose.depthMm) <= socket.spec.seatedDepthTolMm && pose.lateralMm <= socket.spec.posTolMm && pose.tiltDeg <= socket.spec.angTolDeg;
            return seated ? S1Placement.Seated : S1Placement.Partial;
        }

        private void Gate(bool allowed)
        {
            foreach (var input in inputs) if (input) input.Gate(allowed);
        }

        public string InputSource(int pointer)
        {
            if (!controller || !port || !port.InteractionAllowed) return "None";
            string source = controller.InputSourceForPointer(pointer);
            return source == "SimulatedController" && !port.demoMode ? "None" : source;
        }

        public bool TrayOccupied(int index, ResearchS1Sled except) => sleds.Any(p => p != except && p.Tray == index);
        public bool ScrewOccupied(int index, ResearchS1TaskAFastener except) => gpuScrews.Any(p => p != except && p.SocketIndex == index);
        public bool GpuSeated(int index) => index >= 0 && index < gpuSlots.Length && gpus.Any(p => p.Socket == gpuSlots[index] &&
            Placement(p) == S1Placement.Seated && (!Application.isPlaying || !p.IsHeld));
        public bool GpuThreadRetained(QAPlug part)
        {
            int index = Array.IndexOf(gpuSlots, part.Socket);
            return index >= 0 && gpuScrews.Any(p => p.SocketIndex == index && p.ProgressDegrees > 0);
        }
        public static bool Finite(Matrix4x4 pose)
        {
            for (int i = 0; i < 16; i++) if (float.IsNaN(pose[i]) || float.IsInfinity(pose[i])) return false;
            return true;
        }
    }
}
