using System;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
    public enum S1FanPartKind { FrontGuard, Blade, Spinner, RearGuard, RearLock, FrontClip }

    [DefaultExecutionOrder(10000)]
    public sealed class ResearchS1FanPart : MonoBehaviour, ITransformer
    {
        public ResearchS1TaskBMechanics owner;
        public S1FanPartKind kind;
        public string stableId;
        public Transform seat, mate;
        public Rigidbody body;
        public Grabbable grabbable;
        public HandGrabInteractable interactable;
        public float approachMm, positionToleranceMm = 3, angleToleranceDegrees = 6;
        public float fullTurnDegrees, threadTravelMm, insertTurnDegrees;
        public bool rightHanded;
        public AnimationClip clipOpen, clipClose;
        public GameObject clipAnimationRoot;
        [SerializeField] private bool captured;
        [SerializeField] private float progressDegrees;
        [SerializeField] private Quaternion clipClosedRotation;
        private IGrabbable grab;
        private Matrix4x4 grabToRoot, mateInRoot, acceptedPose;
        private float previousHandTurn;
        private double stopBackoff;
        private int pointer = -1;
        private bool gesture, rebase, wasBlocked, cancelled, endPending;
        private ResearchTaskPort.Attempt attempt;
        private string source = "None";

        public bool IsThread => kind == S1FanPartKind.Spinner || kind == S1FanPartKind.RearLock;
        public bool IsHeld => Application.isPlaying && grabbable && grabbable.SelectingPointsCount > 0;
        public float ThreadProgressDegrees => progressDegrees;
        public bool ClipClosed => kind == S1FanPartKind.FrontClip && Quaternion.Angle(transform.localRotation, clipClosedRotation) <= 1;
        public MatePose Pose => SocketRules.Measure(seat.localToWorldMatrix, mate.localToWorldMatrix);
        public bool Aligned
        {
            get
            {
                if (!seat || !mate) return false;
                var pose = Pose;
                double key = kind == S1FanPartKind.Blade ? 180 : kind == S1FanPartKind.RearGuard ? 120 : 360;
                return pose.lateralMm <= positionToleranceMm && pose.tiltDeg <= angleToleranceDegrees &&
                    (IsThread || ResearchS1FanKinematics.KeyErrorDegrees(pose.turnDeg, key) <= angleToleranceDegrees);
            }
        }
        public S1Placement Placement
        {
            get
            {
                if (!captured || !seat || !mate) return S1Placement.NotInstalled;
                var pose = Pose;
                if (pose.lateralMm > positionToleranceMm + 1 || pose.tiltDeg > angleToleranceDegrees + 2 || pose.depthMm > approachMm + 1 || pose.depthMm < -.3f)
                    return S1Placement.NotInstalled;
                return pose.depthMm <= .3f && Aligned ? S1Placement.Seated : S1Placement.Partial;
            }
        }
        public S1ThreadState ThreadState
        {
            get
            {
                if (!IsThread || !captured || progressDegrees <= 0 || !Aligned) return S1ThreadState.Absent;
                float expected = (float)ResearchS1FanKinematics.ThreadDepthMm(progressDegrees, fullTurnDegrees, threadTravelMm);
                if (Mathf.Abs(Pose.depthMm - expected) > .3f) return S1ThreadState.Absent;
                return progressDegrees >= fullTurnDegrees ? S1ThreadState.Tight : S1ThreadState.EngagedLoose;
            }
        }

        public void AuthorInitialPose(float threadProgressDegrees = 0)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Initial fan state is authored only outside Play Mode.");
            captured = true;
            progressDegrees = threadProgressDegrees;
            if (kind == S1FanPartKind.FrontClip) SampleClip(true);
            clipClosedRotation = transform.localRotation;
            mateInRoot = mate ? transform.worldToLocalMatrix * mate.localToWorldMatrix : Matrix4x4.identity;
            if (IsThread) Place(SocketRules.RootPose(seat.localToWorldMatrix,
                (float)ResearchS1FanKinematics.ThreadDepthMm(progressDegrees, fullTurnDegrees, threadTravelMm),
                SignedThreadTurn(), mateInRoot));
        }

        public void Initialize(IGrabbable value) => grab = value;

        private void Start()
        {
            if (!owner || !body || !grabbable || !interactable) throw new InvalidOperationException("Fan part is not fully authored.");
            grabbable.TransferOnSecondSelection = true;
            mateInRoot = mate ? transform.worldToLocalMatrix * mate.localToWorldMatrix : Matrix4x4.identity;
            acceptedPose = transform.localToWorldMatrix;
            grabbable.WhenPointerEventRaised += Pointer;
        }

        private void OnDestroy()
        {
            if (owner && owner.port) owner.port.EndAttempt(attempt, true);
            if (grabbable) grabbable.WhenPointerEventRaised -= Pointer;
        }

        private void Pointer(PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Select)
            {
                if (grabbable.SelectingPointsCount != 1) { CancelGesture(); return; }
                pointer = evt.Identifier;
            }
            if (evt.Type == PointerEventType.Cancel && evt.Identifier == pointer) cancelled = true;
            if ((evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel) && grabbable.SelectingPointsCount == 0)
            {
                FlushEnd();
                pointer = -1;
            }
        }

        public void BeginTransform()
        {
            if (grab == null || grab.GrabPoints.Count != 1 || pointer < 0) { CancelGesture(); return; }
            if (owner.port.IsAttemptActive(attempt) && endPending && !cancelled && owner.InputSource(pointer) == attempt.InputSource)
            {
                endPending = false;
                gesture = true;
                Rebase();
                return;
            }
            FlushEnd();
            Rebase();
            source = owner.InputSource(pointer);
            if (!owner.InteractionAllowed || source == "None") return;
            attempt = owner.port.BeginAttempt(stableId, source);
            gesture = true;
            wasBlocked = cancelled = false;
            if (kind == S1FanPartKind.FrontClip && owner.CanManipulate(this) && source != "None")
            {
                var before = owner.Capture();
                bool closing = !ClipClosed;
                if (closing && !owner.FrontGuardLocked) { RecordMotion(before, "REPAIR", true); return; }
                SampleClip(closing);
                acceptedPose = transform.localToWorldMatrix;
                RecordMotion(before, closing ? "REPAIR" : "ACCESS", false);
            }
        }

        private void SampleClip(bool closed)
        {
            var root = clipAnimationRoot.transform;
            var position = root.localPosition;
            var rotation = root.localRotation;
            var scale = root.localScale;
            try
            {
                var clip = closed ? clipClose : clipOpen;
                clip.SampleAnimation(clipAnimationRoot, clip.length);
            }
            finally
            {
                // Imported clips also key the assembly root; only the clip may move.
                root.SetLocalPositionAndRotation(position, rotation);
                root.localScale = scale;
            }
        }

        private void Rebase()
        {
            var point = grab.GrabPoints[0];
            grabToRoot = Matrix4x4.TRS(point.position, point.rotation, Vector3.one).inverse * transform.localToWorldMatrix;
            previousHandTurn = seat && mate ? Pose.turnDeg : 0;
            stopBackoff = 0;
            acceptedPose = transform.localToWorldMatrix;
            rebase = false;
        }

        public void UpdateTransform()
        {
            if (!gesture) return;
            if (cancelled || grab.GrabPoints.Count != 1 || pointer < 0 || !owner.port.IsAttemptActive(attempt)) { CancelGesture(); return; }
            string observed = owner.InputSource(pointer);
            if (observed == "None" || observed != attempt.InputSource) { CancelGesture(); return; }
            if (kind == S1FanPartKind.FrontClip) return;
            var point = grab.GrabPoints[0];
            var free = Matrix4x4.TRS(point.position, point.rotation, Vector3.one) * grabToRoot;
            var hand = SocketRules.Measure(seat.localToWorldMatrix, free * mateInRoot);
            float delta = Mathf.DeltaAngle(previousHandTurn, hand.turnDeg);
            previousHandTurn = hand.turnDeg;
            if (!Finite(hand) || !Finite(free)) { wasBlocked = true; rebase = true; return; }
            bool requested = Vector3.Distance(transform.position, free.GetColumn(3)) > .00001f || Quaternion.Angle(transform.rotation, free.rotation) > .01f;
            if (!requested) return;
            var before = owner.Capture();
            var prior = Pose;
            bool priorCaptured = captured;
            float priorProgress = progressDegrees;
            double priorBackoff = stopBackoff;
            wasBlocked = false;
            if (!owner.CanManipulate(this))
            {
                string rejected = ClassifyMotion(kind, priorCaptured, priorCaptured, priorProgress,
                    priorProgress - delta * (rightHanded ? 1 : -1), prior.depthMm, hand.depthMm, prior.turnDeg, hand.turnDeg);
                if (rejected != null) RecordMotion(before, rejected, true);
                rebase = true;
                return;
            }
            if (rebase) { Rebase(); return; }
            Matrix4x4 next = IsThread ? ThreadPose(hand, delta, free) : kind == S1FanPartKind.FrontGuard ? FrontPose(hand, free) : KeyedPose(hand, free);
            if (IsThread && !owner.AllowsThreadPose(this, next, priorCaptured))
            {
                string rejected = ClassifyMotion(kind, priorCaptured, captured, priorProgress, progressDegrees,
                    prior.depthMm, hand.depthMm, prior.turnDeg, hand.turnDeg);
                captured = priorCaptured; progressDegrees = priorProgress; stopBackoff = priorBackoff; wasBlocked = true;
                if (rejected != null) RecordMotion(before, rejected, true);
                return;
            }
            // Keep accepted thread angle/depth synchronized even below the display-motion epsilon.
            bool moved = Vector3.Distance(transform.position, next.GetColumn(3)) > .00001f || Quaternion.Angle(transform.rotation, next.rotation) > .01f || (IsThread && priorProgress != progressDegrees);
            if (!moved)
            {
                string rejected = ClassifyMotion(kind, priorCaptured, priorCaptured, priorProgress,
                    priorProgress - delta * (rightHanded ? 1 : -1), prior.depthMm, hand.depthMm, prior.turnDeg, hand.turnDeg);
                if (wasBlocked && rejected != null) RecordMotion(before, rejected, true);
                return;
            }
            Place(next);
            // Thread travel changes the held axial offset; zero progress must not leave old-depth slack before withdrawal.
            if (IsThread && priorProgress > 0 && progressDegrees <= 0 && captured) Rebase();
            var current = Pose;
            string operation = ClassifyMotion(kind, priorCaptured, captured, priorProgress, progressDegrees,
                prior.depthMm, current.depthMm, prior.turnDeg, current.turnDeg);
            if (operation != null) RecordMotion(before, operation, false);
        }

        private Matrix4x4 ThreadPose(MatePose hand, float delta, Matrix4x4 free)
        {
            bool aligned = hand.lateralMm <= positionToleranceMm + 1 && hand.tiltDeg <= angleToleranceDegrees + 2;
            if (captured)
            {
                if (progressDegrees <= 0 && hand.depthMm > threadTravelMm + 2)
                {
                    return Leave(free);
                }
                if (aligned)
                {
                    // Turning into a stop is blocked; a small turn held by the end-stop detent is not.
                    wasBlocked |= Mathf.Abs(delta) > .2f &&
                        Mathf.Approximately((float)ResearchS1FanKinematics.Turn(progressDegrees, -delta, fullTurnDegrees, rightHanded), progressDegrees);
                    progressDegrees = (float)ResearchS1FanKinematics.TurnWithDetent(progressDegrees, -delta, fullTurnDegrees, rightHanded, ref stopBackoff);
                }
                else wasBlocked = true;
                return SocketRules.RootPose(seat.localToWorldMatrix,
                    (float)ResearchS1FanKinematics.ThreadDepthMm(progressDegrees, fullTurnDegrees, threadTravelMm), SignedThreadTurn(), mateInRoot);
            }
            if (!aligned || hand.depthMm > approachMm || hand.depthMm < -threadTravelMm) return owner.FreePose(this, free);
            if (!owner.CanMount(this)) { wasBlocked = true; return owner.FreePose(this, free); }
            if (hand.depthMm > threadTravelMm + .3f)
                return SocketRules.RootPose(seat.localToWorldMatrix, hand.depthMm, hand.turnDeg, mateInRoot);
            captured = true;
            progressDegrees = 0;
            return SocketRules.RootPose(seat.localToWorldMatrix, threadTravelMm, 0, mateInRoot);
        }

        private float SignedThreadTurn() => (rightHanded ? -1 : 1) * progressDegrees;

        private Matrix4x4 KeyedPose(MatePose hand, Matrix4x4 free)
        {
            if (hand.lateralMm > positionToleranceMm + 1 || hand.tiltDeg > angleToleranceDegrees + 2 || hand.depthMm > approachMm + 1)
            {
                return Leave(free);
            }
            captured = true;
            float key = kind == S1FanPartKind.Blade ? 180 : 120;
            bool aligned = owner.CanMount(this) && ResearchS1FanKinematics.KeyErrorDegrees(hand.turnDeg, key) <= angleToleranceDegrees;
            float stop = aligned ? 0 : kind == S1FanPartKind.Blade ? 3.24f : 2.49f;
            wasBlocked |= !aligned;
            return SocketRules.RootPose(seat.localToWorldMatrix, Mathf.Clamp(hand.depthMm, stop, approachMm),
                aligned ? Mathf.Round(hand.turnDeg / key) * key : hand.turnDeg, mateInRoot);
        }

        private Matrix4x4 FrontPose(MatePose hand, Matrix4x4 free)
        {
            bool near = hand.lateralMm <= positionToleranceMm + 1 && hand.tiltDeg <= angleToleranceDegrees + 2 && hand.depthMm <= approachMm + 1;
            bool atEntry = owner.CanMount(this) && Mathf.Abs(Mathf.DeltaAngle(hand.turnDeg, insertTurnDegrees)) <= angleToleranceDegrees;
            if (captured && Pose.depthMm <= .3f)
            {
                float turn = Mathf.Clamp(hand.turnDeg, Mathf.Min(0, insertTurnDegrees), Mathf.Max(0, insertTurnDegrees));
                if (!atEntry || hand.depthMm <= 2)
                    return SocketRules.RootPose(seat.localToWorldMatrix, 0, turn, mateInRoot);
            }
            if (!near)
            {
                return Leave(free);
            }
            captured = true;
            wasBlocked |= !atEntry;
            return SocketRules.RootPose(seat.localToWorldMatrix, Mathf.Clamp(hand.depthMm, atEntry ? 0 : 5.49f, approachMm),
                atEntry ? insertTurnDegrees : hand.turnDeg, mateInRoot);
        }

        private Matrix4x4 Leave(Matrix4x4 free)
        {
            var next = owner.FreePose(this, free);
            bool moved = Vector3.Distance(transform.position, next.GetColumn(3)) > .00001f || Quaternion.Angle(transform.rotation, next.rotation) > .01f;
            if (moved) captured = false;
            else wasBlocked = true;
            return next;
        }

        internal static string ClassifyMotion(S1FanPartKind part, bool wasCaptured, bool isCaptured, float priorProgress, float currentProgress,
            float priorDepth, float currentDepth, float priorTurn, float currentTurn)
        {
            if (!wasCaptured && !isCaptured) return null;
            switch (part)
            {
                case S1FanPartKind.Spinner:
                    if (Math.Abs(currentProgress - priorProgress) > .01f) return currentProgress < priorProgress ? "ACCESS" : "REPAIR";
                    goto case S1FanPartKind.Blade;
                case S1FanPartKind.FrontGuard:
                    if (Math.Abs(currentDepth - priorDepth) <= .01f && Math.Abs(currentTurn - priorTurn) > .01f)
                        return Math.Abs(currentTurn) < Math.Abs(priorTurn) ? "REPAIR" : "ACCESS";
                    goto case S1FanPartKind.Blade;
                case S1FanPartKind.Blade:
                    if (!isCaptured) return "ACCESS";
                    if (!wasCaptured) return "REPAIR";
                    if (Math.Abs(currentDepth - priorDepth) > .01f) return currentDepth < priorDepth ? "REPAIR" : "ACCESS";
                    return null;
                default: return "REPAIR";
            }
        }

        private void RecordMotion(S1TaskBState before, string operation, bool blocked)
        {
            owner.port.UpdateAttempt(attempt, operation, before, owner.Capture(), blocked);
        }

        public void EndTransform()
        {
            if (!gesture) return;
            gesture = false;
            Place(acceptedPose);
            endPending = true;
        }

        private void FlushEnd()
        {
            if (!endPending) return;
            endPending = false;
            owner.port.EndAttempt(attempt, cancelled || (grab != null && grab.GrabPoints.Count > 0));
            attempt = null;
        }

        private void CancelGesture()
        {
            gesture = endPending = false;
            cancelled = rebase = true;
            pointer = -1;
            if (owner && owner.port) owner.port.EndAttempt(attempt, true);
            attempt = null;
        }

        private void LateUpdate()
        {
            if (IsHeld || gesture) Place(acceptedPose);
            FlushEnd();
        }

        private void Place(Matrix4x4 pose)
        {
            acceptedPose = pose;
            transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            if (body) { body.position = transform.position; body.rotation = transform.rotation; }
        }

        public void Gate(bool allowed)
        {
            if (!allowed) cancelled = true;
            if (interactable) interactable.enabled = allowed;
            if (!allowed) CancelGesture();
            rebase = true;
        }

        private static bool Finite(MatePose pose) => !float.IsNaN(pose.depthMm + pose.lateralMm + pose.tiltDeg + pose.turnDeg) && !float.IsInfinity(pose.depthMm + pose.lateralMm + pose.tiltDeg + pose.turnDeg);
        private static bool Finite(Matrix4x4 pose)
        {
            for (int i = 0; i < 16; i++) if (float.IsNaN(pose[i]) || float.IsInfinity(pose[i])) return false;
            return true;
        }
    }
}
