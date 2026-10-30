using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    public static class ResearchS1HeldCollision
    {
        static readonly List<Collider> colliders = new List<Collider>(32);
        static Collider[] overlaps = new Collider[64];
        static RaycastHit[] castHits = new RaycastHit[64];
        static readonly Matrix4x4[] rootPoses = new Matrix4x4[513];
        static readonly Matrix4x4[] colliderPoses = new Matrix4x4[513];

        public static int Samples(float travel, float angle, float radius)
        {
            if (float.IsNaN(travel) || float.IsInfinity(travel) || float.IsNaN(angle) || float.IsInfinity(angle) || float.IsNaN(radius) || float.IsInfinity(radius) || travel < 0 || angle < 0 || radius < 0) return -1;
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
            root.GetComponentsInChildren(colliders);
            for (int i = colliders.Count - 1; i >= 0; i--)
                if (!colliders[i].enabled || colliders[i].isTrigger) colliders.RemoveAt(i);
            if (colliders.Count == 0) return false;
            foreach (var c in colliders) if (!(c is BoxCollider)) return false;
            Physics.SyncTransforms();
            float radius = 0;
            foreach (var c in colliders) radius = Mathf.Max(radius, Vector3.Distance(root.position, c.bounds.center) + c.bounds.extents.magnitude);
            float travel = Vector3.Distance(root.position, desired.GetColumn(3));
            float angle = Quaternion.Angle(root.rotation, desired.rotation);
            int samples = Samples(travel, angle, radius);
            if (samples < 0) return false;
            rootPoses[0] = start;
            for (int i = 1; i <= samples; i++)
            {
                float fraction = (float)i / samples;
                rootPoses[i] = Matrix4x4.TRS(Vector3.Lerp(start.GetColumn(3), desired.GetColumn(3), fraction),
                    Quaternion.Slerp(start.rotation, desired.rotation, fraction), start.lossyScale);
            }
            foreach (var collider in colliders)
            {
                float reach = collider.bounds.extents.magnitude + travel + Quaternion.Angle(start.rotation, desired.rotation) * Mathf.Deg2Rad * radius + .001f;
                int count = Overlap(collider.bounds.center, reach), foreign = 0;
                for (int i = 0; i < count; i++)
                {
                    var other = overlaps[i];
                    if (other.enabled && other != constrainedThreadContact && !other.transform.IsChildOf(root) && !Physics.GetIgnoreCollision(collider, other))
                        overlaps[foreign++] = other;
                }
                if (foreign == 0) continue;
                var local = root.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                for (int i = 0; i <= samples; i++) colliderPoses[i] = rootPoses[i] * local;
                var box = (BoxCollider)collider;
                Vector3 half = Vector3.Scale(box.size * .5f, Abs(box.transform.lossyScale));
                var prior = colliderPoses[0];
                for (int k = 0; k < foreign; k++)
                {
                    var other = overlaps[k];
                    Vector3 direction;
                    float baseline;
                    if (!Physics.ComputePenetration(collider, prior.GetColumn(3), prior.rotation, other, other.transform.position, other.transform.rotation, out direction, out baseline)) baseline = 0;
                    float previous = baseline;
                    for (int i = 1; i <= samples; i++)
                    {
                        var next = colliderPoses[i];
                        float depth;
                        bool touching = Physics.ComputePenetration(collider, next.GetColumn(3), next.rotation,
                            other, other.transform.position, other.transform.rotation, out direction, out depth);
                        if (!touching) depth = 0;
                        if (depth > .0001f && (baseline <= .0001f || depth >= previous - .0000001f)) return false;
                        previous = depth;
                    }
                }
                for (int i = 1; i <= samples; i++)
                {
                    var next = colliderPoses[i];
                    Vector3 a = prior.MultiplyPoint3x4(box.center), b = next.MultiplyPoint3x4(box.center), delta = b - a;
                    if (delta.sqrMagnitude > .0000000001f)
                    {
                        int hits = Cast(a, half, delta.normalized, prior.rotation, delta.magnitude);
                        for (int k = 0; k < hits; k++)
                        {
                            var hit = castHits[k];
                            if (hit.collider && hit.collider != constrainedThreadContact && !hit.collider.transform.IsChildOf(root)
                                && !Physics.GetIgnoreCollision(collider, hit.collider) && hit.distance > .00001f) return false;
                        }
                    }
                    prior = next;
                }
            }
            return true;
        }

        static int Overlap(Vector3 center, float radius)
        {
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(center, radius, overlaps, ~0, QueryTriggerInteraction.Ignore)) == overlaps.Length)
                Array.Resize(ref overlaps, checked(overlaps.Length * 2));
            return count;
        }

        static int Cast(Vector3 center, Vector3 half, Vector3 direction, Quaternion rotation, float distance)
        {
            int count;
            while ((count = Physics.BoxCastNonAlloc(center, half, direction, castHits, rotation, distance, ~0, QueryTriggerInteraction.Ignore)) == castHits.Length)
                Array.Resize(ref castHits, checked(castHits.Length * 2));
            return count;
        }

        static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
