using Oculus.Interaction;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Grab transformer for the Training turntable handle: only yaw of the pivot follows the hand around the vertical axis.
    [DefaultExecutionOrder(11000)]
    public sealed class ResearchTrainingTurntable : MonoBehaviour, ITransformer
    {
        public Transform pivot;
        public bool Allowed = true;
        IGrabbable grab;
        float startHandAngle, startYaw;
        public float Yaw => pivot ? pivot.localEulerAngles.y : 0;

        public void Initialize(IGrabbable value) => grab = value;
        float HandAngle()
        {
            var offset = grab.GrabPoints[0].position - pivot.position;
            return Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        }
        public void BeginTransform() { startHandAngle = HandAngle(); startYaw = Yaw; }
        public void UpdateTransform()
        {
            if (!Allowed || grab.GrabPoints.Count == 0) return;
            var euler = pivot.localEulerAngles;
            euler.y = startYaw + Mathf.DeltaAngle(startHandAngle, HandAngle());
            pivot.localEulerAngles = euler;
            Physics.SyncTransforms();
        }
        public void EndTransform() { }
    }
}
