using System;
using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    [DefaultExecutionOrder(-200)]
    public sealed class ResearchS1TaskBMechanics : MonoBehaviour
    {
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
                frontClip.clipAnimationRoot == rearGuard.gameObject && frontClip.clipOpen && frontClip.clipClose &&
                frontClip.clipOpen.name == "FAN_PED_GUARD_REAR__GuardClip_open" && frontClip.clipClose.name == "FAN_PED_GUARD_REAR__GuardClip_close" &&
                ThreadBinding(spinner, 720, 5, false) && ThreadBinding(rearLock, 540, 16.9f, true) &&
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

        public bool CanManipulate(ResearchS1FanPart part)
        {
            if (!InteractionAllowed || !part || !motorShaft || Quaternion.Angle(motorShaft.localRotation, isolatedShaftRotation) >= .01f) return false;
            bool frontOff = frontGuard.Placement == S1Placement.NotInstalled;
            switch (part.kind)
            {
                case S1FanPartKind.FrontClip: return true;
                case S1FanPartKind.FrontGuard: return !frontClip.ClipClosed;
                case S1FanPartKind.Spinner: return frontOff;
                case S1FanPartKind.Blade: return frontOff && spinner.ThreadState == S1ThreadState.Absent && spinner.Placement == S1Placement.NotInstalled;
                case S1FanPartKind.RearLock: return frontOff && blade.Placement == S1Placement.NotInstalled && spinner.ThreadState == S1ThreadState.Absent && spinner.Placement == S1Placement.NotInstalled;
                case S1FanPartKind.RearGuard: return frontOff && blade.Placement == S1Placement.NotInstalled && spinner.ThreadState == S1ThreadState.Absent && spinner.Placement == S1Placement.NotInstalled && rearLock.ThreadState == S1ThreadState.Absent && rearLock.Placement == S1Placement.NotInstalled;
                default: return false;
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
    }
}
