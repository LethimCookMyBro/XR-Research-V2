using System.Linq;
using Oculus.Interaction;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    public sealed class ResearchS1ServiceYaw : MonoBehaviour, ITransformer
    {
        public ResearchS1TaskAMechanics owner;
        public ResearchS1TaskAInput input;
        public Grabbable grabbable;
        private IGrabbable grab;
        private Vector3 previousDirection;
        private bool rebase;
        private Matrix4x4 acceptedHandle;

        public void Initialize(IGrabbable value) => grab = value;
        public void BeginTransform()
        {
            rebase = true;
            acceptedHandle = transform.localToWorldMatrix;
        }
        public static bool HandDirection(Vector3 pivot, Vector3 hand, out Vector3 direction)
        {
            direction = Vector3.ProjectOnPlane(hand - pivot, Vector3.up);
            return direction.sqrMagnitude >= .0025f && !float.IsNaN(direction.sqrMagnitude) && !float.IsInfinity(direction.sqrMagnitude);
        }
        public void UpdateTransform()
        {
            if (grab == null || grab.GrabPoints.Count != 1 || !input.Allowed) { rebase = true; return; }
            var pivot = owner.servicePivot;
            Vector3 direction;
            if (!HandDirection(pivot.position, grab.GrabPoints[0].position, out direction)) { rebase = true; return; }
            if (rebase) { previousDirection = direction; rebase = false; return; }
            float delta = Vector3.SignedAngle(previousDirection, direction, Vector3.up);
            previousDirection = direction;
            if (Mathf.Abs(delta) < .01f) return;
            var before = owner.Capture();
            var next = Matrix4x4.TRS(pivot.position, Quaternion.AngleAxis(delta, Vector3.up) * pivot.rotation, pivot.lossyScale);
            var reason = owner.inputs.Any(i => i != input && i.pointable.SelectingPointsCount > 0)
                ? PartBlockReason.OtherHeld : owner.MotionBlockReason(pivot, next);
            bool blocked = reason != PartBlockReason.None;
            if (!blocked)
            {
                pivot.SetPositionAndRotation(next.GetColumn(3), next.rotation);
                Physics.SyncTransforms();
                acceptedHandle = transform.localToWorldMatrix;
            }
            input.Motion("ROTATE_WORKPIECE", before, blocked, reason);
        }
        public void EndTransform()
        {
            transform.SetPositionAndRotation(acceptedHandle.GetColumn(3), acceptedHandle.rotation);
            rebase = true;
        }
    }
}
