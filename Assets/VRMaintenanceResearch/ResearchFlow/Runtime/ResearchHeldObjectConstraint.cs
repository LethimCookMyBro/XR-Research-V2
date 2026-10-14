using System;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Unity.Profiling;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>
    /// Grabbable one-grab transformer: the SDK single-point rigid follow of the grab point, constrained against the
    /// audited static environment before the pose is written. Grab points, raw tracking and the SDK kinematic lock are untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResearchHeldObjectConstraint : MonoBehaviour, ITransformer
    {
        public Collider[] environment = Array.Empty<Collider>();
        public Transform trackingOrigin;
        [Min(.002f)] public float maxStepMetres = .015f;
        [Min(.0001f)] public float skin = .001f;
        [Min(.0001f)] public float tolerance = .002f;
        [Range(1, 64)] public int maxSubsteps = 24;
        public bool Holding { get; private set; }
        public float DesiredSeparationMetres { get; private set; }
        public float MaxDesiredSeparationMetres { get; private set; }
        public int Updates { get; private set; }
        public int ConstrainedUpdates { get; private set; }
        public int RejectedSubsteps { get; private set; }
        public int UnresolvedPoses { get; private set; }
        public int SaturatedQueries { get; private set; }
        public int ReleaseCorrections { get; private set; }
        public double LastUpdateMilliseconds { get; private set; }
        public double MaxUpdateMilliseconds { get; private set; }

        static readonly ProfilerMarker Marker = new ProfilerMarker("Research.HeldObject.Constraint");
        readonly HashSet<Collider> allowed = new HashSet<Collider>();
        readonly Collider[] overlaps = new Collider[64];
        IGrabbable grabbable;
        Collider[] own = Array.Empty<Collider>();
        Pose[] ownLocal = Array.Empty<Pose>();
        Pose grabToObject, validInOrigin;
        float reach, step;
        int mask;

        public static void Install(ResearchSessionFlowController controller)
        {
            var solids = ResearchHandContactProxy.StaticEnvironment(controller);
            foreach (var item in controller.trainingObjects)
            {
                if (!item || !item.grabbable) continue;
                if (!item.TryGetComponent(out ResearchHeldObjectConstraint constraint)) constraint = item.gameObject.AddComponent<ResearchHeldObjectConstraint>();
                constraint.trackingOrigin = controller.origin ? controller.origin.transform : null;
                constraint.ConfigureEnvironment(solids);
                // Supported SDK seam; Grabbable.Start then skips generating its GrabFreeTransformer (or it is simply no longer used).
                item.grabbable.InjectOptionalOneGrabTransformer(constraint);
                item.grabbable.InjectOptionalTwoGrabTransformer(constraint);
                // Grabbable assigns Transform in Start and initializes injected transformers there; inactive props start later.
                if (item.grabbable.Transform) constraint.Initialize(item.grabbable);
            }
        }

        public void ConfigureEnvironment(Collider[] colliders)
        {
            environment = colliders ?? Array.Empty<Collider>();
            allowed.Clear(); mask = 0;
            foreach (var c in environment)
            {
                if (!c || c.attachedRigidbody || c.GetComponentInParent<Grabbable>() || c.GetComponentInParent<Canvas>()) continue;
                allowed.Add(c); mask |= 1 << c.gameObject.layer;
            }
        }

        bool Solid(Collider c) => c && allowed.Contains(c) && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && !c.attachedRigidbody;

        public void Initialize(IGrabbable target)
        {
            grabbable = target;
            var body = target.Transform.GetComponent<Rigidbody>();
            own = target.Transform.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && c.GetComponentInParent<Rigidbody>() == body).ToArray();
            // ComputePenetration needs a primitive or convex shape on one side; the environment side may be a non-convex mesh.
            if (own.Length == 0 || own.Any(c => c is MeshCollider m && !m.convex))
                throw new InvalidOperationException("Held-object constraint needs the object's own convex or primitive colliders: " + target.Transform.name);
            ownLocal = new Pose[own.Length];
        }

        public void BeginTransform()
        {
            var target = grabbable.Transform; var grab = grabbable.GrabPoints[0];
            var inverse = Quaternion.Inverse(grab.rotation);
            grabToObject = new Pose(inverse * (target.position - grab.position), inverse * target.rotation);
            var local = Quaternion.Inverse(target.rotation);
            reach = 0; float thinnest = float.MaxValue;
            for (int i = 0; i < own.Length; i++)
            {
                ownLocal[i] = new Pose(local * (own[i].transform.position - target.position), local * own[i].transform.rotation);
                var b = own[i].bounds;
                reach = Mathf.Max(reach, Vector3.Distance(target.position, b.center) + b.extents.magnitude);
                thinnest = Mathf.Min(thinnest, Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z)));
            }
            // ponytail: world AABB approximates the thinnest extent; fine for chunky props, use collider-local extents for thin plates.
            // A sub-step below a third of the object's thickness cannot skip a zero-thickness mesh surface.
            step = Mathf.Clamp(thinnest / 3, .003f, maxStepMetres);
            Vector3 position = target.position;
            if (!Resolve(ref position, target.rotation, out _)) { UnresolvedPoses++; position = target.position; }
            target.position = position;
            Remember(position, target.rotation);
            DesiredSeparationMetres = 0; Holding = true;
        }

        public void UpdateTransform()
        {
            long begin = System.Diagnostics.Stopwatch.GetTimestamp();
            using (Marker.Auto())
            {
                var target = grabbable.Transform; var grab = grabbable.GrabPoints[0];
                var desired = new Pose(grab.position + grab.rotation * grabToObject.position, grab.rotation * grabToObject.rotation);
                // Last valid pose rides with the rig, so teleport/snap turn carry the object instead of sweeping it across the room.
                var start = trackingOrigin ? new Pose(trackingOrigin.position + trackingOrigin.rotation * validInOrigin.position,
                    trackingOrigin.rotation * validInOrigin.rotation) : validInOrigin;
                Vector3 position = start.position; Quaternion rotation = start.rotation;
                if (!Resolve(ref position, rotation, out _)) { UnresolvedPoses++; position = start.position; }
                Vector3 goal = desired.position;
                for (int i = 0; i < maxSubsteps; i++)
                {
                    Vector3 delta = goal - position;
                    float remaining = delta.magnitude + Quaternion.Angle(rotation, desired.rotation) * Mathf.Deg2Rad * reach;
                    if (remaining < .0001f) break;
                    float fraction = Mathf.Min(1, step / remaining);
                    Vector3 candidate = position + delta * fraction;
                    Quaternion turn = Quaternion.Slerp(rotation, desired.rotation, fraction);
                    if (!Resolve(ref candidate, turn, out Vector3 push)) { RejectedSubsteps++; break; }
                    position = candidate; rotation = turn;
                    // Slide: drop only the requested motion into the contact normal, keep the tangential part.
                    if (push.sqrMagnitude > 0) goal = position + Vector3.ProjectOnPlane(goal - position, push.normalized);
                }
                target.SetPositionAndRotation(position, rotation);
                Remember(position, rotation);
                Updates++;
                DesiredSeparationMetres = Vector3.Distance(position, desired.position);
                MaxDesiredSeparationMetres = Mathf.Max(MaxDesiredSeparationMetres, DesiredSeparationMetres);
                if (DesiredSeparationMetres > tolerance || Quaternion.Angle(rotation, desired.rotation) > .5f) ConstrainedUpdates++;
            }
            LastUpdateMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - begin) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            MaxUpdateMilliseconds = Math.Max(MaxUpdateMilliseconds, LastUpdateMilliseconds);
        }

        // Runs inside Grabbable before the SDK kinematic unlock and throw velocity: never release an embedded pose.
        public void EndTransform()
        {
            if (!Holding) return;
            Holding = false;
            var target = grabbable.Transform; Vector3 position = target.position;
            if (!Resolve(ref position, target.rotation, out Vector3 push)) { UnresolvedPoses++; return; } // lost-object recovery owns the residual
            if (push.magnitude > skin) { target.position = position; ReleaseCorrections++; }
        }

        void Remember(Vector3 position, Quaternion rotation)
        {
            if (!trackingOrigin) { validInOrigin = new Pose(position, rotation); return; }
            var inverse = Quaternion.Inverse(trackingOrigin.rotation);
            validInOrigin = new Pose(inverse * (position - trackingOrigin.position), inverse * rotation);
        }

        /// <summary>Pushes the object's real colliders out of static solids; false leaves the pose untouched.</summary>
        public bool Resolve(ref Vector3 position, Quaternion rotation, out Vector3 push)
        {
            Vector3 start = position; push = Vector3.zero;
            for (int pass = 0; pass < 5; pass++)
            {
                int count = Physics.OverlapSphereNonAlloc(position, reach + skin, overlaps, mask, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length) { SaturatedQueries++; break; }
                float deepest = 0;
                for (int i = 0; i < count; i++)
                {
                    var solid = overlaps[i];
                    if (!Solid(solid)) continue;
                    for (int k = 0; k < own.Length; k++)
                        if (own[k].enabled && Physics.ComputePenetration(own[k], position + rotation * ownLocal[k].position, rotation * ownLocal[k].rotation,
                            solid, solid.transform.position, solid.transform.rotation, out Vector3 direction, out float depth))
                        { deepest = Mathf.Max(deepest, depth); position += direction * (depth + skin); }
                }
                if (deepest <= tolerance) { push = position - start; return true; }
            }
            position = start; return false;
        }
    }
}
