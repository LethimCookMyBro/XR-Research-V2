using System;
using Oculus.Interaction;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
    [DefaultExecutionOrder(11000)]
    public sealed class ResearchS1Sled : MonoBehaviour, ITransformer
    {
        public ResearchS1TaskAMechanics owner;
        public QAPlug part;
        public ResearchS1TaskAInput input;
        public Transform mate;
        public ResearchS1TaskAFastener thumb;
        private IGrabbable grab;
        private Matrix4x4 grabToRoot, mateInRoot, accepted;
        private S1SledMotion motion;
        private int tray = -1;
        private bool held, rebase;
        public int Tray => tray;
        public bool Hooked => motion.Phase == S1SledPhase.Hooked;
        public S1Placement Placement
        {
            get
            {
                if (tray < 0 || motion.Phase == S1SledPhase.Free) return S1Placement.NotInstalled;
                var pose = owner.traySeats[tray].worldToLocalMatrix * mate.localToWorldMatrix;
                Vector3 position = pose.GetColumn(3);
                return motion.Seated && position.magnitude <= .0005f && Quaternion.Angle(pose.rotation, Quaternion.identity) <= 1
                    ? S1Placement.Seated : S1Placement.Partial;
            }
        }
        public void Initialize(IGrabbable value) => grab = value;
        private void Start()
        {
            mateInRoot = transform.worldToLocalMatrix * mate.localToWorldMatrix;
            accepted = transform.localToWorldMatrix;
            gameObject.AddComponent<ResearchS1FreeBody>().Configure(part.body, part.grabbable,
                () => tray >= 0, () => owner.port && owner.port.InteractionAllowed,
                pose => owner.wiring.Allows(transform, pose));
        }
        public void BeginTransform() { held = true; if (tray < 0) accepted = transform.localToWorldMatrix; Rebase(); }
        private void Rebase()
        {
            if (grab == null || grab.GrabPoints.Count == 0) return;
            var p = grab.GrabPoints[0];
            grabToRoot = Matrix4x4.TRS(p.position, p.rotation, Vector3.one).inverse * transform.localToWorldMatrix;
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
            if (thumb.IsHeld)
            {
                if (Vector3.Distance(transform.position, free.GetColumn(3)) > .0005f || Quaternion.Angle(transform.rotation, free.rotation) > .2f)
                    input.Motion(Hooked ? "REMOVE" : "INSTALL", before, true, PartBlockReason.OtherHeld);
                return;
            }
            int selected = tray;
            if (selected < 0)
            {
                float nearest = .04f;
                for (int i = 0; i < owner.traySeats.Length; i++)
                {
                    float distance = Vector3.Distance((free * mateInRoot).GetColumn(3), owner.traySeats[i].position);
                    if (distance < nearest) { nearest = distance; selected = i; }
                }
            }
            if (selected < 0)
            {
                var freeReason = owner.MotionBlockReason(transform, free);
                if (freeReason == PartBlockReason.None) { Place(free); input.Observe(PartOperation.Move); }
                else input.Motion("MOVE", before, true, freeReason);
                return;
            }
            var relative = owner.traySeats[selected].worldToLocalMatrix * free * mateInRoot;
            Vector3 pos = relative.GetColumn(3);
            bool aligned = Quaternion.Angle(relative.rotation, Quaternion.identity) <= 6;
            bool occupied = owner.TrayOccupied(selected, this), threaded = thumb.ProgressDegrees > 0;
            double transverseMm = pos.y * 1000;
            var next = ResearchS1SledKinematics.Step(motion, pos.x * 1000, -pos.z * 1000, transverseMm, aligned, occupied, threaded);
            var blockedReason = !next.Blocked ? PartBlockReason.None
                : threaded ? PartBlockReason.Retained
                : motion.Phase != S1SledPhase.Hooked && occupied ? PartBlockReason.MountNotReady
                : motion.Phase == S1SledPhase.Hooked && aligned && Math.Abs(transverseMm) <= 4
                    ? PartBlockReason.Retained : PartBlockReason.Alignment;
            bool removing = motion.Phase == S1SledPhase.Hooked && (next.SlideMm > motion.SlideMm + .01 || next.Phase == S1SledPhase.Free || -pos.z * 1000 > .3);
            bool meaningful = next.Blocked || next.Phase != motion.Phase || Math.Abs(next.SlideMm - motion.SlideMm) > .01 || Math.Abs(next.DepthMm - motion.DepthMm) > .01;
            var nextPose = next.Phase == S1SledPhase.Free ? free : owner.traySeats[selected].localToWorldMatrix *
                Matrix4x4.Translate(new Vector3((float)next.SlideMm / 1000, 0, -(float)next.DepthMm / 1000)) * mateInRoot.inverse;
            var motionReason = owner.MotionBlockReason(transform, nextPose);
            if (motionReason != PartBlockReason.None) { input.Motion(removing ? "REMOVE" : "INSTALL", before, true, motionReason); return; }
            motion = next;
            tray = next.Phase == S1SledPhase.Free ? -1 : selected;
            Place(nextPose);
            input.Observe(removing ? PartOperation.Remove : PartOperation.Insert, blockedReason);
            if (meaningful) input.Motion(removing ? "REMOVE" : "INSTALL", before, next.Blocked, blockedReason);
        }
        private Matrix4x4 GuidedPose() => owner.traySeats[tray].localToWorldMatrix *
            Matrix4x4.Translate(new Vector3((float)motion.SlideMm / 1000, 0, -(float)motion.DepthMm / 1000)) * mateInRoot.inverse;
        public void EndTransform() { held = false; Place(accepted); }
        private void LateUpdate()
        {
            if (tray >= 0) Place(GuidedPose());
            else if (held) Place(accepted);
        }
        private void Place(Matrix4x4 pose)
        {
            accepted = pose;
            var parent = tray >= 0 ? owner.servicePivot : owner.transform;
            if (transform.parent != parent) transform.SetParent(parent, true);
            transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            part.body.position = transform.position; part.body.rotation = transform.rotation;
        }
    }
}
