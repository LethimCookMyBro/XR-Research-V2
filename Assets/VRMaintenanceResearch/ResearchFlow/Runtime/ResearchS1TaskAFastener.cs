using System;
using Oculus.Interaction;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
    [DefaultExecutionOrder(11000)]
    public sealed class ResearchS1TaskAFastener : MonoBehaviour, ITransformer
    {
        public ResearchS1TaskAMechanics owner;
        public ResearchS1TaskAInput input;
        public ResearchS1Sled sled;
        public Transform mate, captiveSeat;
        public Rigidbody body;
        public Grabbable grabbable;
        public float travelMm;
        [SerializeField] private float progress;
        [SerializeField] private int socketIndex = -1;
        private IGrabbable grab;
        private Matrix4x4 grabToRoot, mateInRoot, accepted;
        private float previousTurn;
        private double stopBackoff;
        private bool held, rebase;
        public float ProgressDegrees => progress;
        public int SocketIndex => socketIndex;
        public float FullDegrees => travelMm / .79375f * 360;
        public bool Captive => sled;
        public bool IsHeld => Application.isPlaying && grabbable.SelectingPointsCount > 0;
        private Matrix4x4 Seat => SeatAt(socketIndex);
        private Matrix4x4 SeatAt(int index) => Captive ? captiveSeat.localToWorldMatrix : owner.gpuScrewSeats[index].localToWorldMatrix * Matrix4x4.Translate(new Vector3(0, 0, -.0009f));
        public S1ThreadState State
        {
            get
            {
                if (progress <= 0 || (!Captive && socketIndex < 0) || (Captive && sled.Placement != S1Placement.Seated)) return S1ThreadState.Absent;
                var pose = SocketRules.Measure(Seat, mate.localToWorldMatrix);
                float expected = (float)ResearchS1FanKinematics.ThreadDepthMm(progress, FullDegrees, travelMm);
                if (pose.lateralMm > .3f || pose.tiltDeg > 1 || Mathf.Abs(pose.depthMm - expected) > .3f) return S1ThreadState.Absent;
                return progress >= FullDegrees ? S1ThreadState.Tight : S1ThreadState.EngagedLoose;
            }
        }
        public void AuthorOpen()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Author fasteners only outside Play Mode.");
            progress = 0; socketIndex = -1;
            mateInRoot = transform.worldToLocalMatrix * mate.localToWorldMatrix;
            if (Captive) Place(ThreadPose());
        }
        public void Initialize(IGrabbable value) => grab = value;
        private void Start() { mateInRoot = transform.worldToLocalMatrix * mate.localToWorldMatrix; accepted = transform.localToWorldMatrix; }
        public void BeginTransform() { held = true; Rebase(); }
        private void Rebase()
        {
            if (grab == null || grab.GrabPoints.Count == 0) return;
            accepted = transform.localToWorldMatrix;
            var p = grab.GrabPoints[0];
            grabToRoot = Matrix4x4.TRS(p.position, p.rotation, Vector3.one).inverse * transform.localToWorldMatrix;
            previousTurn = Captive || socketIndex >= 0 ? SocketRules.Measure(Seat, mate.localToWorldMatrix).turnDeg : 0;
            stopBackoff = 0;
            rebase = false;
        }
        public void UpdateTransform()
        {
            if (!held || grab.GrabPoints.Count == 0) return;
            if (!input.Allowed) { rebase = true; return; }
            if (rebase) { Rebase(); return; }
            var p = grab.GrabPoints[0];
            var free = Matrix4x4.TRS(p.position, p.rotation, Vector3.one) * grabToRoot;
            if (!ResearchS1TaskAMechanics.Finite(free)) { rebase = true; return; }
            var before = owner.Capture();
            if (!Captive && socketIndex < 0)
            {
                for (int i = 0; i < owner.gpuScrewSeats.Length; i++)
                {
                    var seat = owner.gpuScrewSeats[i].localToWorldMatrix * Matrix4x4.Translate(new Vector3(0, 0, -.0009f));
                    var offer = SocketRules.Measure(seat, free * mateInRoot);
                    if (offer.lateralMm > 3 || offer.tiltDeg > 6 || offer.depthMm > travelMm + 3 || offer.depthMm < -1) continue;
                    if (!owner.GpuSeated(i) || owner.ScrewOccupied(i, this))
                    { input.Motion("FASTEN", before, true); return; }
                    var offeredPose = ThreadPose(i, progress);
                    if (!owner.AllowsMotion(transform, offeredPose)) { input.Motion("ALIGN", before, true); return; }
                    socketIndex = i; previousTurn = offer.turnDeg;
                    Place(offeredPose);
                    input.Motion("ALIGN", before, false);
                    return;
                }
                if (owner.AllowsMotion(transform, free)) Place(free);
                else input.Motion("MOVE", before, true);
                return;
            }
            var hand = SocketRules.Measure(Seat, free * mateInRoot);
            // The imported head is on seat-local -Z, so clockwise is a negative +Z turn.
            float clockwiseDelta = -Mathf.DeltaAngle(previousTurn, hand.turnDeg);
            previousTurn = hand.turnDeg;
            if (!Captive && progress <= 0 && hand.depthMm > travelMm + 2)
            {
                if (!owner.AllowsMotion(transform, free)) { input.Motion("REMOVE", before, true); return; }
                socketIndex = -1; Place(free); input.Motion("REMOVE", before, false); return;
            }
            bool blocked = hand.lateralMm > 4 || hand.tiltDeg > 8 || (Captive ? sled.Placement != S1Placement.Seated || sled.part.IsHeld : !owner.GpuSeated(socketIndex));
            double backoff = stopBackoff;
            float next = blocked ? progress : (float)ResearchS1FanKinematics.TurnWithDetent(progress, clockwiseDelta, FullDegrees, true, ref backoff);
            // Turning into a stop is blocked; a small turn held by the end-stop detent is not.
            blocked |= Mathf.Abs(clockwiseDelta) > .2f && Mathf.Approximately((float)ResearchS1FanKinematics.Turn(progress, clockwiseDelta, FullDegrees, true), progress);
            bool changed = !Mathf.Approximately(next, progress);
            var nextPose = ThreadPose(socketIndex, next);
            if (!owner.AllowsMotion(transform, nextPose)) { input.Motion(clockwiseDelta >= 0 ? "FASTEN" : "UNFASTEN", before, true); return; }
            stopBackoff = backoff;
            progress = next;
            Place(nextPose);
            if (changed || blocked && Mathf.Abs(clockwiseDelta) > .2f) input.Motion(clockwiseDelta >= 0 ? "FASTEN" : "UNFASTEN", before, blocked);
        }
        private Matrix4x4 ThreadPose() => ThreadPose(socketIndex, progress);
        private Matrix4x4 ThreadPose(int index, float degrees) => SocketRules.RootPose(SeatAt(index),
            (float)ResearchS1FanKinematics.ThreadDepthMm(degrees, FullDegrees, travelMm), -degrees, mateInRoot);
        public void EndTransform() { held = false; Place(accepted); }
        private void LateUpdate()
        {
            if (Captive || socketIndex >= 0) Place(ThreadPose());
            else if (held) Place(accepted);
        }
        private void Place(Matrix4x4 pose)
        {
            accepted = pose;
            if (!Captive)
            {
                var parent = socketIndex >= 0 ? owner.servicePivot : owner.transform;
                if (transform.parent != parent) transform.SetParent(parent, true);
            }
            transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            body.position = transform.position; body.rotation = transform.rotation;
        }
    }
}
