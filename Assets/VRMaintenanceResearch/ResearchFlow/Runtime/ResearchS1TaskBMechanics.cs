using System;
using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    [DefaultExecutionOrder(-200)]
    public sealed class ResearchS1TaskBMechanics : MonoBehaviour
    {
        // S1_TASKB_SHORT_THREADS_20261029: 720/540 deg made removal and refit the slowest part of Task B (owner target 5-6 min).
        public const string ThreadVersion = "S1_TASKB_SHORT_THREADS_20261029";
        public const float SpinnerTurnDegrees = 270, RearLockTurnDegrees = 180, RearLockFaultGapDegrees = 20;
        public ResearchTaskPort port;
        public ResearchSessionFlowController controller;
        public Transform servicePivot;
        public Transform[] fixedAssembly;
        public Vector3[] fixedPositions;
        public Quaternion[] fixedRotations;
        public ResearchS1FanPart frontGuard, blade, spinner, rearGuard, rearLock, frontClip;
        public Transform motorShaft;
        public Quaternion isolatedShaftRotation;
        private ResearchS1FanPart[] parts;
        private Collider rearGuardThreadContact;
        public bool InteractionAllowed => port && port.InteractionAllowed;
        public bool FrontGuardLocked => frontGuard && frontGuard.Placement == S1Placement.Seated && Mathf.Abs(frontGuard.Pose.turnDeg) <= 1;

        private void Start() => Bind();

        public void Bind()
        {
            KeepAuthoredGuardDetail();
            if (!AuthoredBindingsValid())
                throw new InvalidOperationException("Task B has missing or inconsistent authored bindings.");
            rearGuardThreadContact = rearGuard.GetComponentsInChildren<Collider>(true).Single(c => c.name == "COL_FAN_PED_GUARD_REAR_Shell"
                && !c.isTrigger && c.attachedRigidbody == rearGuard.body);
            parts = new[] { frontGuard, blade, spinner, rearGuard, rearLock, frontClip };
            port.BindSnapshot(() => Capture(), value =>
            {
                var result = ResearchS1TaskEvaluator.Evaluate((S1TaskBState)value);
                if (result.Category == S1InspectionCategory.SystemError) throw new InvalidOperationException("Task B snapshot is invalid.");
                return result.Passed;
            }, Gate);
            port.BindFeedback(value => "inspect.s1.B." + ResearchS1TaskEvaluator.Evaluate((S1TaskBState)value).Category);
            port.BindExamineInfo(Observation);
        }

        public void KeepAuthoredGuardDetail()
        {
            // The authored guard exposes only LOD0; automatic LOD selected disabled meshes and made it disappear.
            foreach (var guard in new[] { frontGuard, rearGuard })
                if (guard) foreach (var group in guard.GetComponentsInChildren<LODGroup>(true)) group.ForceLOD(0);
        }

        public S1TaskBState Capture()
        {
            bool valid = AuthoredBindingsValid();
            if (!valid) return new S1TaskBState { systemIntegrityValid = false, capturedAtSeconds = Time.realtimeSinceStartupAsDouble };
            bool stable = true;
            if (stable)
                for (int i = 0; i < fixedAssembly.Length; i++)
                    stable &= fixedAssembly[i] && fixedAssembly[i].parent == servicePivot && fixedAssembly[i].gameObject.activeInHierarchy && Vector3.Distance(fixedAssembly[i].localPosition, fixedPositions[i]) < .0005f &&
                        Quaternion.Angle(fixedAssembly[i].localRotation, fixedRotations[i]) < .1f;
            return new S1TaskBState
            {
                systemIntegrityValid = valid,
                capturedAtSeconds = Time.realtimeSinceStartupAsDouble,
                workpieceYawDegrees = servicePivot ? servicePivot.localEulerAngles.y : 0,
                baseId = "B.base", frontGuardId = frontGuard ? frontGuard.stableId : null, bladeId = blade ? blade.stableId : null,
                spinnerId = spinner ? spinner.stableId : null, rearGuardId = rearGuard ? rearGuard.stableId : null, rearLockId = rearLock ? rearLock.stableId : null,
                // This scoped fixture has no mains connection or motor driver. A moved shaft invalidates isolation.
                powerIsolated = motorShaft && Quaternion.Angle(motorShaft.localRotation, isolatedShaftRotation) < .01f,
                motorRunning = motorShaft && Quaternion.Angle(motorShaft.localRotation, isolatedShaftRotation) >= .01f,
                baseStable = stable,
                assemblyObstructed = valid && (frontGuard.IsHeld || blade.IsHeld || spinner.IsHeld || rearGuard.IsHeld || rearLock.IsHeld),
                frontGuardPlacement = frontGuard ? frontGuard.Placement : S1Placement.NotInstalled,
                frontGuardAligned = FrontGuardLocked,
                frontGuardClipped = frontClip && frontClip.ClipClosed,
                bladePlacement = blade ? blade.Placement : S1Placement.NotInstalled,
                bladeCompatible = blade && blade.mate && blade.mate.name == "MATE_FAN_PED_BLADE_Bore",
                bladeKeyAligned = blade && blade.Aligned,
                spinner = spinner ? spinner.ThreadState : S1ThreadState.Absent,
                rearGuardPlacement = rearGuard ? rearGuard.Placement : S1Placement.NotInstalled,
                rearGuardAligned = rearGuard && rearGuard.Aligned,
                rearLock = rearLock ? rearLock.ThreadState : S1ThreadState.Absent
            }.WithB04Context();
        }

        private bool AuthoredBindingsValid()
        {
            if (!port || port.taskId != "B" || !controller || servicePivot != transform || !motorShaft ||
                fixedAssembly == null || fixedPositions == null || fixedRotations == null ||
                fixedAssembly.Length != 3 || fixedPositions.Length != 3 || fixedRotations.Length != 3) return false;
            string[] fixedNames = { "FAN_PED_BASE", "FAN_PED_STAND", "FAN_PED_HEAD" };
            for (int i = 0; i < fixedAssembly.Length; i++)
                if (!fixedAssembly[i] || fixedAssembly[i].name != fixedNames[i] || fixedAssembly[i].parent != servicePivot ||
                    !Finite(fixedPositions[i]) || !ValidRotation(fixedRotations[i])) return false;
            if (!Anchor(motorShaft, fixedAssembly[2], "MOV_FAN_PED_HEAD_Shaft") || !ValidRotation(isolatedShaftRotation)) return false;
            if (!PartBinding(frontGuard, S1FanPartKind.FrontGuard, "B.front_guard") ||
                !PartBinding(blade, S1FanPartKind.Blade, "B.blade") ||
                !PartBinding(spinner, S1FanPartKind.Spinner, "B.spinner") ||
                !PartBinding(rearGuard, S1FanPartKind.RearGuard, "B.rear_guard") ||
                !PartBinding(rearLock, S1FanPartKind.RearLock, "B.rear_guard_lock") ||
                !PartBinding(frontClip, S1FanPartKind.FrontClip, "B.front_guard_clip")) return false;
            return MountBinding(frontGuard, rearGuard.transform, "FAN_PED_GUARD_FRONT", "Rim", "SEAT_FAN_PED_GUARD_REAR_FrontLocked") &&
                MountBinding(blade, fixedAssembly[2], "FAN_PED_BLADE", "Bore", "SEAT_FAN_PED_HEAD_Blade") &&
                MountBinding(spinner, fixedAssembly[2], "FAN_PED_SPINNER", "Thread", "SEAT_FAN_PED_HEAD_Spinner") &&
                MountBinding(rearGuard, fixedAssembly[2], "FAN_PED_GUARD_REAR", "Plate", "SEAT_FAN_PED_HEAD_GuardRear") &&
                MountBinding(rearLock, fixedAssembly[2], "FAN_PED_GUARD_LOCK", "Thread", "SEAT_FAN_PED_HEAD_GuardLock") &&
                Anchor(frontClip.transform, rearGuard.transform, "MOV_FAN_PED_GUARD_REAR_GuardClip") &&
                frontClip.clipAnimationRoot == rearGuard.gameObject && frontClip.ClipPosesAuthored && frontClip.clipOpen && frontClip.clipClose &&
                frontClip.clipOpen.name == "FAN_PED_GUARD_REAR__GuardClip_open" && frontClip.clipClose.name == "FAN_PED_GUARD_REAR__GuardClip_close" &&
                ThreadBinding(spinner, SpinnerTurnDegrees, 5, false) && ThreadBinding(rearLock, RearLockTurnDegrees, 16.9f, true) &&
                Finite(frontGuard.insertTurnDegrees) && Mathf.Abs(Mathf.Abs(frontGuard.insertTurnDegrees) - 12) <= .1f;
        }

        private bool PartBinding(ResearchS1FanPart part, S1FanPartKind kind, string id)
        {
            return part && part.owner == this && part.kind == kind && part.stableId == id &&
                part.body && part.body.gameObject == part.gameObject && part.grabbable && part.grabbable.gameObject == part.gameObject &&
                part.interactable && part.interactable.transform.parent == part.transform;
        }

        private bool MountBinding(ResearchS1FanPart part, Transform host, string asset, string mate, string seat)
        {
            return part.transform.parent == servicePivot && Anchor(part.mate, part.transform, "MATE_" + asset + "_" + mate) &&
                Anchor(part.seat, host, seat) && Finite(part.approachMm) && part.approachMm > 0 &&
                Finite(part.positionToleranceMm) && part.positionToleranceMm > 0 &&
                Finite(part.angleToleranceDegrees) && part.angleToleranceDegrees > 0;
        }

        private static bool ThreadBinding(ResearchS1FanPart part, float turns, float travel, bool rightHanded)
        {
            return part.fullTurnDegrees == turns && part.threadTravelMm == travel && part.rightHanded == rightHanded &&
                Finite(part.ThreadProgressDegrees) && part.ThreadProgressDegrees >= 0 && part.ThreadProgressDegrees <= turns;
        }

        private static bool Anchor(Transform anchor, Transform root, string name) => anchor && root && anchor.name == name && anchor.IsChildOf(root);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool ValidRotation(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w) &&
            Mathf.Abs(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w - 1) < .01f;

        public string InputSource(int pointer)
        {
            if (!controller || pointer < 0) return "None";
            string value = controller.InputSourceForPointer(pointer);
            if (value == "SimulatedController" && !port.demoMode) return "None";
            return value;
        }

        public bool CanManipulate(ResearchS1FanPart part) => ManipulationBlockReason(part) == PartBlockReason.None;

        public PartBlockReason ManipulationBlockReason(ResearchS1FanPart part)
        {
            if (!InteractionAllowed || !part || !motorShaft || Quaternion.Angle(motorShaft.localRotation, isolatedShaftRotation) >= .01f) return PartBlockReason.Unspecified;
            bool frontOff = frontGuard.Placement == S1Placement.NotInstalled;
            bool capOff = spinner.ThreadState == S1ThreadState.Absent && spinner.Placement == S1Placement.NotInstalled;
            switch (part.kind)
            {
                case S1FanPartKind.FrontClip: return PartBlockReason.None;
                case S1FanPartKind.FrontGuard: return frontClip.ClipClosed ? PartBlockReason.ClipClosed : PartBlockReason.None;
                case S1FanPartKind.Spinner: return frontOff ? PartBlockReason.None : PartBlockReason.FrontGuardPresent;
                case S1FanPartKind.Blade: return !frontOff ? PartBlockReason.FrontGuardPresent : !capOff ? PartBlockReason.BladeCapPresent : PartBlockReason.None;
                case S1FanPartKind.RearLock:
                case S1FanPartKind.RearGuard:
                    if (!frontOff) return PartBlockReason.FrontGuardPresent;
                    if (!capOff) return PartBlockReason.BladeCapPresent;
                    if (blade.Placement != S1Placement.NotInstalled) return PartBlockReason.BladePresent;
                    if (part.kind == S1FanPartKind.RearGuard && (rearLock.ThreadState != S1ThreadState.Absent || rearLock.Placement != S1Placement.NotInstalled)) return PartBlockReason.RearRingPresent;
                    return PartBlockReason.None;
                default: return PartBlockReason.Unspecified;
            }
        }

        public bool CanMount(ResearchS1FanPart part)
        {
            switch (part.kind)
            {
                case S1FanPartKind.FrontGuard: return rearGuard.Placement == S1Placement.Seated;
                case S1FanPartKind.Blade: return rearGuard.Placement == S1Placement.Seated && rearLock.ThreadState != S1ThreadState.Absent;
                case S1FanPartKind.Spinner: return blade.Placement == S1Placement.Seated;
                case S1FanPartKind.RearLock: return rearGuard.Placement == S1Placement.Seated;
                case S1FanPartKind.RearGuard: return true;
                default: return false;
            }
        }

        private void Gate(bool allowed)
        {
            foreach (var part in parts) if (part) part.Gate(allowed);
        }

        Collider ThreadContact(ResearchS1FanPart part, Matrix4x4 desired, bool previouslyCaptured = false)
        {
            if (part.kind != S1FanPartKind.RearLock || !rearGuard || rearGuard.Placement != S1Placement.Seated
                || !rearGuard.Aligned || rearGuard.IsHeld || !ResearchS1TaskAMechanics.Finite(desired)) return null;
            if (previouslyCaptured) return rearGuardThreadContact;
            if (part.ThreadProgressDegrees > 0 || !CanMount(part) || !part.Aligned
                || part.Pose.depthMm < part.threadTravelMm - .3f) return null;
            var pose = VRMaintenanceResearch.AssetLibraryQA.SocketRules.Measure(part.seat.localToWorldMatrix,
                desired * part.transform.worldToLocalMatrix * part.mate.localToWorldMatrix);
            // The solid shell fills the bore outside the 20 mm snap range too; retain the existing capture band.
            return pose.lateralMm <= part.positionToleranceMm && pose.tiltDeg <= part.angleToleranceDegrees
                && pose.depthMm >= part.threadTravelMm - .3f ? rearGuardThreadContact : null;
        }

        internal bool AllowsThreadPose(ResearchS1FanPart part, Matrix4x4 desired, bool previouslyCaptured)
            => ResearchS1HeldCollision.Allows(part.transform, desired, ThreadContact(part, desired, previouslyCaptured));

        // The shared swept test permits escape from existing proxy overlap without tunneling.
        public Matrix4x4 FreePose(ResearchS1FanPart part, Matrix4x4 desired)
            => ResearchS1HeldCollision.Allows(part.transform, desired, ThreadContact(part, desired)) ? desired : part.transform.localToWorldMatrix;

        private string Observation(string target, string language) => ResearchS1FanObservations.Get(Capture(), target, language);

        // Owner 29 Oct: the ticket names a vibration/knock the participant never heard. The 3 s Inspect spin now sounds like the
        // reported symptom while the fault remains, and like a smooth fan once fixed. No sound while the guards are not ready.
        AudioSource testAudio;
        static AudioClip humClip, knockClip;
        public void PlayInspectionSound(string feedbackKey)
        {
            bool symptom = feedbackKey == "inspect.s1.B.SymptomPresent";
            if (!symptom && feedbackKey != "inspect.s1.B.Pass") return;
            if (!testAudio)
            {
                testAudio = (servicePivot ? servicePivot : transform).gameObject.AddComponent<AudioSource>();
                testAudio.playOnAwake = false; testAudio.spatialBlend = 1; testAudio.minDistance = .6f; testAudio.maxDistance = 6;
            }
            testAudio.clip = symptom ? knockClip ??= SpinSound(true) : humClip ??= SpinSound(false);
            testAudio.Play();
        }

        public static AudioClip SpinSound(bool knock)
        {
            const int rate = 24000;
            var data = new float[(int)(rate * ResearchTaskTiming.InspectionSeconds)];
            var noise = new System.Random(4);
            float seconds = data.Length / (float)rate;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate, envelope = Mathf.Clamp01(t / .5f) * Mathf.Clamp01((seconds - t) / .3f);
                float hum = .22f * Mathf.Sin(2 * Mathf.PI * 100 * t) + .1f * Mathf.Sin(2 * Mathf.PI * 200 * t) + .05f * ((float)noise.NextDouble() * 2 - 1);
                // A loose guard knocks once per blade pass (3 blades at ~5 rev/s) as the spin comes up to speed.
                float phase = t * 15 * Mathf.Clamp01(t / .8f) % 1;
                float rattle = knock && phase < .05f ? .7f * (1 - phase / .05f) * ((float)noise.NextDouble() * 2 - 1) : 0;
                data[i] = envelope * (hum + rattle);
            }
            var clip = AudioClip.Create(knock ? "S1_FanSpin_Knock" : "S1_FanSpin_Smooth", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
