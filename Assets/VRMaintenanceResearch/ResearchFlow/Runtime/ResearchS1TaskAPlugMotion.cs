using Oculus.Interaction;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
    [DefaultExecutionOrder(11000)]
    public sealed class ResearchS1TaskAPlugMotion : MonoBehaviour, ITransformer
    {
        public ResearchS1TaskAMechanics owner;
        public QAPlug plug;
        public ResearchS1Cable cable;
        public ResearchS1TaskAInput input;
        private IGrabbable grab;
        private Matrix4x4 grabToRoot, accepted;
        private bool rebase;
        public void Initialize(IGrabbable value)
        {
            grab = value; plug.Initialize(value);
            plug.PoseAllowed = pose => owner.AllowsMotion(transform, pose);
            plug.SlideFree = pose => owner.FreeMove(transform, pose, out _);
        }
        // S1_LAYOUT_CABLE_TRAY_20261110: both ends of a task cable are loose plugs. A head let go away from a port goes to the
        // tray beside the case. As a free body it hung inside the case, dropped behind it or stopped in mid-air at the cable limit.
        const float ReturnMetresPerSecond = 1.5f, ReturnDegreesPerSecond = 720;
        bool Parks => cable && cable.ParkOf(transform) != null;

        // Where this loose head belongs now. While the hand holds the other end of a cable that is loose at both ends, the
        // whole folded cable comes along: this end keeps its folded place beside the held one. With the other end in a port it lies in the tray corner on that port's side of the motherboard tray,
        // lead toward the case. Otherwise it lies folded in its start slot. A slot the lead cannot reach is skipped.
        bool TryParkPose(out Pose pose, out bool carried)
        {
            var park = cable.ParkOf(transform); var slots = park.slots;
            var end = System.Array.Find(owner.wiring.ends, e => e.plug == plug || e.sourcePlug == plug);
            var other = end.plug == plug ? end.sourcePlug : end.plug;
            carried = other.IsHeld && !other.Socket;
            if (carried)
            {
                pose = cable.FoldedPose(transform);
                return true;
            }
            if (other.IsHeld) { pose = default; return false; } // the other end is being pulled out of a port; wait where it is
            bool rear = cable.transform.InverseTransformPoint(other.transform.position).z > cable.passPlane;
            var order = !other.Socket ? new[] { 0 } : (park.waitSlot > 0 ? park.waitSlot == 2 : rear) ? new[] { 2, 1, 0 } : new[] { 1, 2, 0 };
            foreach (int i in order)
            {
                pose = cable.SlotPose(slots[i]);
                if ((transform.position == pose.position && transform.rotation == pose.rotation) ||
                    owner.wiring.Allows(transform, Matrix4x4.TRS(pose.position, pose.rotation, transform.lossyScale))) return true;
            }
            pose = default;
            return false;
        }

        private void Start()
        {
            gameObject.AddComponent<ResearchS1FreeBody>().Configure(plug.body, plug.grabbable,
                () => plug.Socket != null || Parks, () => owner.port && owner.port.InteractionAllowed,
                pose => owner.wiring.Allows(transform, pose));
        }

        private void LateUpdate()
        {
            // Stateless: the socket lets go a frame or two after the hand does, so the head converges whenever it is loose and free.
            if (!Parks || plug.IsHeld || plug.Socket || !TryParkPose(out var park, out bool carried)) return;
            if (transform.position == park.position && transform.rotation == park.rotation) return;
            var position = carried ? park.position : Vector3.MoveTowards(transform.position, park.position, ReturnMetresPerSecond * Time.deltaTime);
            var rotation = carried ? park.rotation : Quaternion.RotateTowards(transform.rotation, park.rotation, ReturnDegreesPerSecond * Time.deltaTime);
            if (position == park.position && Quaternion.Angle(rotation, park.rotation) < .01f) rotation = park.rotation;
            transform.SetPositionAndRotation(position, rotation);
            plug.body.position = position; plug.body.rotation = rotation;
        }

        public void BeginTransform()
        {
            if (grab == null || grab.GrabPoints.Count == 0) return;
            plug.BeginTransform();
            var p = grab.GrabPoints[0];
            grabToRoot = Matrix4x4.TRS(p.position, p.rotation, Vector3.one).inverse * transform.localToWorldMatrix;
            accepted = transform.localToWorldMatrix;
            rebase = false;
        }
        public void UpdateTransform()
        {
            if (grab == null || grab.GrabPoints.Count == 0) return;
            if (!input.Allowed) { rebase = true; return; }
            if (rebase) { BeginTransform(); return; }
            var p = grab.GrabPoints[0];
            var free = Matrix4x4.TRS(p.position, p.rotation, Vector3.one) * grabToRoot;
            if (!ResearchS1TaskAMechanics.Finite(free)) { rebase = true; return; }
            if (owner.GpuThreadRetained(plug))
            {
                if (Vector3.Distance(transform.position, free.GetColumn(3)) > .0005f || Quaternion.Angle(transform.rotation, free.rotation) > .2f)
                    input.Motion("REMOVE", owner.Capture(), true, PartBlockReason.Retained);
                return;
            }
            var before = owner.Capture();
            var priorSocket = plug.Socket;
            bool attempted = Vector3.Distance(transform.position, free.GetColumn(3)) > .0001f || Quaternion.Angle(transform.rotation, free.rotation) > .1f;
            bool removing = false;
            if (priorSocket && priorSocket.Mate)
            {
                var desired = SocketRules.Measure(priorSocket.seat.localToWorldMatrix, free * plug.MateInRoot(priorSocket.Mate));
                removing = desired.depthMm > priorSocket.Last.depthMm + .01f || desired.lateralMm > priorSocket.spec.posTolMm || desired.tiltDeg > priorSocket.spec.angTolDeg;
            }
            bool blocked = false;
            PartBlockReason reason = PartBlockReason.None;
            QASocket proposedSocket;
            plug.UpdateTransform(pose => !(blocked = (reason = owner.MotionBlockReason(transform, pose)) != PartBlockReason.None), out proposedSocket);
            accepted = transform.localToWorldMatrix;
            if (blocked)
            {
                bool disconnecting = removing || (priorSocket && !proposedSocket);
                string action = proposedSocket || priorSocket
                    ? (cable ? (disconnecting ? "DISCONNECT" : "CONNECT") : (disconnecting ? "REMOVE" : "INSTALL"))
                    : "MOVE";
                input.Motion(action, before, true, reason);
                return;
            }
            if (!attempted) return;
            var socket = plug.Socket;
            if (socket && socket.Last.captured)
                input.Motion(cable ? (removing ? "DISCONNECT" : "CONNECT") : (removing ? "REMOVE" : "INSTALL"), before,
                    removing ? socket.Retained : socket.Last.stop != Stop.Seat,
                    removing ? PartBlockReason.Retained : socket.Last.stop == Stop.Entrance && socket.latches.Exists(l => l.Closed)
                        ? PartBlockReason.LatchClosed : !socket.PrerequisitesMet ? PartBlockReason.MountNotReady : PartBlockReason.Alignment);
            else if (priorSocket) input.Motion(cable ? "DISCONNECT" : "REMOVE", before, false);
            else input.Observe(PartOperation.Move);
        }
        public void EndTransform()
        {
            transform.SetPositionAndRotation(accepted.GetColumn(3), accepted.rotation);
            plug.body.position = transform.position; plug.body.rotation = transform.rotation;
            plug.EndTransform();
        }
    }
}
