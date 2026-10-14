using Oculus.Interaction;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
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
                    input.Motion("REMOVE", owner.Capture(), true);
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
            QASocket proposedSocket;
            plug.UpdateTransform(pose => !(blocked = !owner.AllowsMotion(transform, pose)), out proposedSocket);
            accepted = transform.localToWorldMatrix;
            if (blocked)
            {
                bool disconnecting = removing || (priorSocket && !proposedSocket);
                string action = proposedSocket || priorSocket
                    ? (cable ? (disconnecting ? "DISCONNECT" : "CONNECT") : (disconnecting ? "REMOVE" : "INSTALL"))
                    : "MOVE";
                input.Motion(action, before, true);
                return;
            }
            if (!attempted) return;
            var socket = plug.Socket;
            if (socket && socket.Last.captured)
                input.Motion(cable ? (removing ? "DISCONNECT" : "CONNECT") : (removing ? "REMOVE" : "INSTALL"), before,
                    removing ? socket.Retained : socket.Last.stop != Stop.Seat);
            else if (priorSocket) input.Motion(cable ? "DISCONNECT" : "REMOVE", before, false);
        }
        public void EndTransform()
        {
            transform.SetPositionAndRotation(accepted.GetColumn(3), accepted.rotation);
            plug.body.position = transform.position; plug.body.rotation = transform.rotation;
            plug.EndTransform();
        }
    }
}
