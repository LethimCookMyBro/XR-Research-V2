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
        [SerializeField] private Pose clipOpenPose, clipClosedPose;
        [SerializeField] private bool clipPosesAuthored;
        private IGrabbable grab;
        private Matrix4x4 grabToRoot, mateInRoot, acceptedPose;
        private float previousHandTurn;
        private double stopBackoff;
        private int pointer = -1;
        private bool gesture, rebase, wasBlocked, cancelled, endPending;
        private PartBlockReason blockReason;
        private ResearchTaskPort.Attempt attempt;
        private string source = "None";

        public bool IsThread => kind == S1FanPartKind.Spinner || kind == S1FanPartKind.RearLock;
        public bool IsHeld => Application.isPlaying && grabbable && grabbable.SelectingPointsCount > 0;
        public float ThreadProgressDegrees => progressDegrees;
        public bool ClipClosed => kind == S1FanPartKind.FrontClip && Quaternion.Angle(transform.localRotation, clipClosedRotation) <= 1;
        public bool ClipPosesAuthored => clipPosesAuthored;
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
                return ResearchS1FanKinematics.IsTight(progressDegrees, fullTurnDegrees) ? S1ThreadState.Tight : S1ThreadState.EngagedLoose;
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
            gameObject.AddComponent<ResearchS1FreeBody>().Configure(body, grabbable,
                () => kind == S1FanPartKind.FrontClip || captured, () => owner.InteractionAllowed);
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
            blockReason = PartBlockReason.None;
            if (kind == S1FanPartKind.FrontClip && owner.CanManipulate(this) && source != "None")
            {
                var before = owner.Capture();
                bool closing = !ClipClosed;
                if (closing && !owner.FrontGuardLocked) { blockReason = PartBlockReason.GuardNotSeated; RecordMotion(before, "REPAIR", true); return; }
                SampleClip(closing);
                acceptedPose = transform.localToWorldMatrix;
                RecordMotion(before, closing ? "REPAIR" : "ACCESS", false);
            }
        }

        private void SampleClip(bool closed)
        {
            if (!clipPosesAuthored) throw new InvalidOperationException("Fan clip open/closed poses have not been authored.");
            // A two-state latch must not depend on imported animation sampling in the player.
            var pose = closed ? clipClosedPose : clipOpenPose;
            transform.SetLocalPositionAndRotation(pose.position, pose.rotation);
            acceptedPose = transform.localToWorldMatrix;
            if (body)
            {
                body.position = transform.position;
                body.rotation = transform.rotation;
            }
        }

#if UNITY_EDITOR
        public void AuthorClipPoses(Pose open, Pose closed)
        {
            if (Application.isPlaying || kind != S1FanPartKind.FrontClip)
                throw new InvalidOperationException("Author fan clip poses only in the stopped Editor.");
            float separation = Quaternion.Angle(open.rotation, closed.rotation);
            if (float.IsNaN(separation) || separation <= 1 ||
                !float.IsFinite(open.position.sqrMagnitude + closed.position.sqrMagnitude))
                throw new InvalidOperationException("Authored clip poses must be finite and visibly distinct.");
            clipOpenPose = open;
            clipClosedPose = closed;
            clipClosedRotation = closed.rotation;
            clipPosesAuthored = true;
        }
#endif

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
            blockReason = owner.ManipulationBlockReason(this);
            if (blockReason != PartBlockReason.None)
            {
                owner.port.UpdateInteraction(attempt, InteractionOperation(), blockReason);
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
                blockReason = PartBlockReason.Contact;
                owner.port.UpdateInteraction(attempt, InteractionOperation(), blockReason);
                if (rejected != null) RecordMotion(before, rejected, true);
                return;
            }
            owner.port.UpdateInteraction(attempt, InteractionOperation(), wasBlocked ? blockReason : PartBlockReason.None);
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
                // The shaft constrains an engaged part even when the wrist moves off-axis.
                if (progressDegrees > 0 || aligned)
                {
                    // Turning into a stop is blocked; a small turn held by the end-stop detent is not.
                    bool atStop = Mathf.Abs(delta) > .2f &&
                        Mathf.Approximately((float)ResearchS1FanKinematics.Turn(progressDegrees, -delta, fullTurnDegrees, rightHanded), progressDegrees);
                    wasBlocked |= atStop;
                    if (atStop) blockReason = PartBlockReason.EndStop;
                    progressDegrees = (float)ResearchS1FanKinematics.TurnWithDetent(progressDegrees, -delta, fullTurnDegrees, rightHanded, ref stopBackoff);
                }
                else { wasBlocked = true; blockReason = PartBlockReason.Alignment; }
                return SocketRules.RootPose(seat.localToWorldMatrix,
                    (float)ResearchS1FanKinematics.ThreadDepthMm(progressDegrees, fullTurnDegrees, threadTravelMm), SignedThreadTurn(), mateInRoot);
            }
            if (!aligned || hand.depthMm > approachMm || hand.depthMm < -threadTravelMm) return FreePose(free);
            if (!owner.CanMount(this)) { wasBlocked = true; blockReason = PartBlockReason.MountNotReady; return FreePose(free); }
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
            if (!aligned) blockReason = owner.CanMount(this) ? PartBlockReason.Alignment : PartBlockReason.MountNotReady;
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
            if (!atEntry) blockReason = owner.CanMount(this) ? PartBlockReason.Alignment : PartBlockReason.MountNotReady;
            return SocketRules.RootPose(seat.localToWorldMatrix, Mathf.Clamp(hand.depthMm, atEntry ? 0 : 5.49f, approachMm),
                atEntry ? insertTurnDegrees : hand.turnDeg, mateInRoot);
        }

        private Matrix4x4 FreePose(Matrix4x4 free)
        {
            var accepted = owner.FreePose(this, free);
            bool requested = Vector3.Distance(transform.position, free.GetColumn(3)) > .00001f || Quaternion.Angle(transform.rotation, free.rotation) > .01f;
            bool moved = Vector3.Distance(transform.position, accepted.GetColumn(3)) > .00001f || Quaternion.Angle(transform.rotation, accepted.rotation) > .01f;
            if (requested && !moved) { wasBlocked = true; blockReason = PartBlockReason.Contact; }
            return accepted;
        }

        private Matrix4x4 Leave(Matrix4x4 free)
        {
            var next = FreePose(free);
            bool moved = Vector3.Distance(transform.position, next.GetColumn(3)) > .00001f || Quaternion.Angle(transform.rotation, next.rotation) > .01f;
            if (moved) captured = false;
            else { wasBlocked = true; blockReason = PartBlockReason.Contact; }
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
            owner.port.UpdateAttempt(attempt, operation, before, owner.Capture(), blocked,
                InteractionOperation(), blocked ? blockReason : PartBlockReason.None);
        }

        private PartOperation InteractionOperation() => kind == S1FanPartKind.FrontClip
            ? (ClipClosed || blockReason == PartBlockReason.GuardNotSeated ? PartOperation.Close : PartOperation.Open)
            : IsThread ? PartOperation.Turn : PartOperation.Move;

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
            GetComponent<ResearchS1FreeBody>()?.Refresh();
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
