using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    public static class ResearchS1HeldCollision
    {
        public static int Samples(float travel, float angle, float radius)
        {
            if (float.IsNaN(travel) || float.IsInfinity(travel) || float.IsNaN(angle) || float.IsInfinity(angle) ||
                float.IsNaN(radius) || float.IsInfinity(radius) || travel < 0 || angle < 0 || radius < 0) return -1;
            float distance = travel + angle * Mathf.Deg2Rad * radius;
            if (distance > .128f) return -1;
            int count = Mathf.CeilToInt(distance / .00025f);
            return count > 512 ? -1 : Mathf.Max(1, count);
        }

        public static bool Allows(Transform root, Matrix4x4 desired, Collider constrainedThreadContact = null)
        {
            if (!root || !ResearchS1TaskAMechanics.Finite(desired)) return false;
            var start = root.localToWorldMatrix;
            if (Vector3.Distance(start.lossyScale, desired.lossyScale) > .00001f) return false;
            if (Vector3.Distance(root.position, desired.GetColumn(3)) < .0000001f && Quaternion.Angle(root.rotation, desired.rotation) < .00001f) return true;
            var colliders = root.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
            if (colliders.Length == 0 || colliders.Any(c => !(c is BoxCollider))) return false;
            Physics.SyncTransforms();
            float radius = colliders.Max(c => Vector3.Distance(root.position, c.bounds.center) + c.bounds.extents.magnitude);
            float travel = Vector3.Distance(root.position, desired.GetColumn(3));
            int samples = Samples(travel, Quaternion.Angle(root.rotation, desired.rotation), radius);
            if (samples < 0) return false;
            foreach (var collider in colliders)
            {
                float reach = collider.bounds.extents.magnitude + travel +
                    Quaternion.Angle(start.rotation, desired.rotation) * Mathf.Deg2Rad * radius + .001f;
                var others = Physics.OverlapSphere(collider.bounds.center, reach, ~0, QueryTriggerInteraction.Ignore)
                    .Where(c => c.enabled && c != constrainedThreadContact && !c.transform.IsChildOf(root) && !Physics.GetIgnoreCollision(collider, c)).ToArray();
                var local = root.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                var box = (BoxCollider)collider;
                Vector3 half = Vector3.Scale(box.size * .5f, Abs(box.transform.lossyScale));
                var prior = start * local;
                foreach (var other in others)
                {
                    Vector3 direction;
                    float baseline;
                    if (!Physics.ComputePenetration(collider, prior.GetColumn(3), prior.rotation, other,
                        other.transform.position, other.transform.rotation, out direction, out baseline)) baseline = 0;
                    float previous = baseline;
                    for (int i = 1; i <= samples; i++)
                    {
                        float fraction = (float)i / samples;
                        var next = Matrix4x4.TRS(Vector3.Lerp(start.GetColumn(3), desired.GetColumn(3), fraction),
                            Quaternion.Slerp(start.rotation, desired.rotation, fraction), start.lossyScale) * local;
                        float depth;
                        bool touching = Physics.ComputePenetration(collider, next.GetColumn(3), next.rotation, other,
                            other.transform.position, other.transform.rotation, out direction, out depth);
                        if (!touching) depth = 0;
                        if (depth > .0001f && (baseline <= .0001f || depth >= previous - .0000001f)) return false;
                        previous = depth;
                    }
                }
                // Translation casts retain thin barriers even when both sampled endpoints are clear.
                for (int i = 1; i <= samples; i++)
                {
                    float fraction = (float)i / samples;
                    var next = Matrix4x4.TRS(Vector3.Lerp(start.GetColumn(3), desired.GetColumn(3), fraction),
                        Quaternion.Slerp(start.rotation, desired.rotation, fraction), start.lossyScale) * local;
                    Vector3 a = prior.MultiplyPoint3x4(box.center), b = next.MultiplyPoint3x4(box.center), delta = b - a;
                    if (delta.sqrMagnitude > .0000000001f)
                        foreach (var hit in Physics.BoxCastAll(a, half, delta.normalized, prior.rotation, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                            if (hit.collider && hit.collider != constrainedThreadContact && !hit.collider.transform.IsChildOf(root) && !Physics.GetIgnoreCollision(collider, hit.collider) && hit.distance > .00001f) return false;
                    prior = next;
                }
            }
            return true;
        }

        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
