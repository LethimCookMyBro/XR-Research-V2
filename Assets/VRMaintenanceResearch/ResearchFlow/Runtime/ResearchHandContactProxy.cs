using System;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Unity.Profiling;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Visual-only contact after the SDK skeleton update; never writes to IHand or grab targets.</summary>
    [DisallowMultipleComponent]
    public sealed class ResearchHandContactProxy : MonoBehaviour
    {
        public HandVisual handVisual;
        public Transform trackingOrigin;
        public Transform recoveryAnchor;
        public Collider[] environment = Array.Empty<Collider>();
        [Min(.005f)] public float palmRadius = .035f;
        [Min(.003f)] public float fingertipRadius = .009f;
        [Min(.0001f)] public float skin = .001f;
        [Min(.05f)] public float divergenceNoticeMetres = .18f;
        public float SeparationMetres { get; private set; }
        public bool DeepPenetration => SeparationMetres > divergenceNoticeMetres;
        public int SaturatedQueries { get; private set; }
        public int ContactUpdates { get; private set; }
        public int UnresolvedContacts { get; private set; }
        public bool GhostAvailable => ghost;
        public bool GhostVisible => ghost && ghost.enabled;
        public double LastUpdateMilliseconds { get; private set; }
        public double MaxUpdateMilliseconds { get; private set; }

        static readonly ProfilerMarker Marker = new ProfilerMarker("Research.HandContact.VisualProxy");
        static readonly HandJointId[] Samples = { HandJointId.HandMiddle1, HandJointId.HandThumbTip,
            HandJointId.HandIndexTip, HandJointId.HandMiddleTip, HandJointId.HandRingTip, HandJointId.HandPinkyTip };
        readonly Vector3[] targets = new Vector3[6], previous = new Vector3[6];
        readonly HashSet<Collider> allowed = new HashSet<Collider>();
        readonly RaycastHit[] hits = new RaycastHit[64];
        readonly Collider[] overlaps = new Collider[64];
        SphereCollider queryShape;
        SkinnedMeshRenderer ghost;
        SkinnedMeshRenderer ghostSource;
        Transform ghostHolder;
        Transform[] visualTransforms, ghostTransforms;
        Vector3[] acceptedPositions;
        Quaternion[] acceptedRotations;
        Pose acceptedRoot;
        Material[] ghostMaterials;
        bool started, initialized;
        Vector3 lastOriginPosition, appliedOffset, renderedRootPosition;
        Quaternion lastOriginRotation;
        int mask;

        public static void Install(ResearchSessionFlowController controller)
        {
            if (!controller || !controller.origin) throw new ArgumentException("Contact installation needs the existing flow rig.");
            var solids = StaticEnvironment(controller);
            foreach (var visual in controller.origin.GetComponentsInChildren<HandVisual>(true))
            {
                var proxy = visual.GetComponent<ResearchHandContactProxy>() ?? visual.gameObject.AddComponent<ResearchHandContactProxy>();
                proxy.handVisual = visual; proxy.trackingOrigin = controller.origin.transform;
                proxy.recoveryAnchor = controller.origin.Camera.transform;
                proxy.ConfigureEnvironment(solids);
            }
        }

        /// <summary>Audited static solids shared by hand contact and held-object contact.</summary>
        public static Collider[] StaticEnvironment(Component flow) =>
            flow.gameObject.scene.GetRootGameObjects().Single(r => r.name == "Environment_V2").GetComponentsInChildren<Collider>(true)
                .Where(c => c.enabled && !c.isTrigger && !c.attachedRigidbody && !c.GetComponentInParent<Grabbable>()).ToArray();

        void Start()
        {
            if (!handVisual) handVisual = GetComponent<HandVisual>();
            if (!handVisual || !handVisual.Root)
                throw new InvalidOperationException("Hand contact needs the SDK HandVisual and its dedicated render skeleton root.");
            if (handVisual.Root == handVisual.transform || handVisual.Root.GetComponent<Rigidbody>()
                || handVisual.Root.GetComponentInChildren<Oculus.Interaction.HandGrab.HandGrabInteractor>())
                throw new InvalidOperationException("Contact must target only the render skeleton, never tracking or interaction transforms.");
            started = true;
            ConfigureEnvironment(environment);
            CreateQueryShape();
            CreateGhost();
            handVisual.WhenHandVisualUpdated += ApplyContact;
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
            ResetContact();
        }

        void CreateQueryShape()
        {
            if (queryShape) return;
            var go = new GameObject("ResearchHandContact_QueryShape") { hideFlags = HideFlags.HideAndDontSave };
            queryShape = go.AddComponent<SphereCollider>();
            queryShape.enabled = false; // ComputePenetration accepts an explicit pose; no physics pairs or impulses.
        }

        void OnEnable()
        {
            if (started && handVisual) { ResetContact(); handVisual.WhenHandVisualUpdated += ApplyContact; }
        }
        void OnDisable()
        {
            if (started && handVisual) handVisual.WhenHandVisualUpdated -= ApplyContact;
            if (handVisual && handVisual.Root && (handVisual.Root.position - renderedRootPosition).sqrMagnitude < .000001f)
                handVisual.Root.position -= appliedOffset;
            ResetContact();
        }
        void OnDestroy()
        {
            if (ghostHolder) Destroy(ghostHolder.gameObject);
            if (ghostMaterials != null) foreach (var material in ghostMaterials) Destroy(material);
            if (queryShape)
            {
                if (Application.isPlaying) Destroy(queryShape.gameObject);
                else DestroyImmediate(queryShape.gameObject);
            }
        }
        public void ResetContact() { initialized = false; appliedOffset = Vector3.zero; SeparationMetres = 0; if (ghost) ghost.enabled = false; }
        bool Solid(Collider c) => c && allowed.Contains(c) && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && !c.attachedRigidbody;

        void CreateGhost()
        {
            visualTransforms = handVisual.Root.GetComponentsInChildren<Transform>(true);
            acceptedPositions = new Vector3[visualTransforms.Length]; acceptedRotations = new Quaternion[visualTransforms.Length];
            var source = handVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(r => r.rootBone && r.rootBone.IsChildOf(handVisual.Root));
            // Reuse the installed Meta hand shader's documented opacity channel, never a second input/interaction stack.
            if (!source || source.sharedMaterials.Any(m => !m || !m.HasProperty("_Opacity"))) return;
            var holder = new GameObject("ResearchHandContact_TrackedGhost").transform;
            holder.SetParent(handVisual.transform, false);
            ghostHolder = holder; ghostSource = source;
            var mapping = new Dictionary<Transform, Transform>();
            ghostTransforms = new Transform[visualTransforms.Length];
            for (int i = 0; i < visualTransforms.Length; i++)
            {
                var original = visualTransforms[i]; var clone = new GameObject(original.name).transform;
                clone.SetParent(i == 0 ? holder : mapping[original.parent], false);
                mapping.Add(original, clone); ghostTransforms[i] = clone;
            }
            ghost = new GameObject("TrackedGhostMesh").AddComponent<SkinnedMeshRenderer>();
            ghost.transform.SetParent(holder, false);
            ghost.sharedMesh = source.sharedMesh;
            ghost.rootBone = mapping[source.rootBone];
            ghost.bones = source.bones.Select(b => mapping[b]).ToArray();
            ghost.localBounds = source.localBounds;
            ghost.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost.receiveShadows = false;
            ghostMaterials = source.sharedMaterials.Select(m => new Material(m)).ToArray();
            foreach (var material in ghostMaterials)
            {
                material.SetFloat("_Opacity", .16f);
                if (material.HasProperty("_OutlineOpacity")) material.SetFloat("_OutlineOpacity", .08f);
            }
            ghost.sharedMaterials = ghostMaterials;
            ghost.enabled = false;
        }

        bool SphereClear(Vector3 point, float radius)
        {
            queryShape.radius = radius;
            int count = Physics.OverlapSphereNonAlloc(point, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) { SaturatedQueries++; return false; }
            for (int i = 0; i < count; i++)
                if (Solid(overlaps[i]) && Physics.ComputePenetration(queryShape, point, Quaternion.identity,
                    overlaps[i], overlaps[i].transform.position, overlaps[i].transform.rotation, out _, out float depth) && depth > skin)
                    return false;
            return true;
        }

        /// <summary>Non-alloc swept sphere and overlap correction against the audited static set.</summary>
        public Vector3 ResolveSphere(Vector3 from, Vector3 target, float radius)
        {
            CreateQueryShape();
            var point = from;
            queryShape.radius = radius;
            // Resolve spawn/reacquire overlap first; sphere casts alone miss an overlapped start.
            for (int pass = 0; pass < 3; pass++)
            {
                int count = Physics.OverlapSphereNonAlloc(point, radius + skin, overlaps, mask, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length) { SaturatedQueries++; return from; }
                bool corrected = false;
                for (int i = 0; i < count; i++)
                    if (Solid(overlaps[i]) && Physics.ComputePenetration(queryShape, point, Quaternion.identity,
                        overlaps[i], overlaps[i].transform.position, overlaps[i].transform.rotation, out Vector3 normal, out float depth))
                    { point += normal * (depth + skin); corrected = true; }
                if (!corrected) break;
            }
            // Three contacts support sliding around an edge/corner without lerp latency.
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 delta = target - point;
                float length = delta.magnitude;
                if (length < .00001f) break;
                int count = Physics.SphereCastNonAlloc(point, radius, delta / length, hits, length + skin, mask, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) { SaturatedQueries++; return point; }
                int nearest = -1; float distance = length + skin;
                for (int i = 0; i < count; i++)
                    if (Solid(hits[i].collider) && hits[i].distance < distance) { nearest = i; distance = hits[i].distance; }
                if (nearest < 0) { point = target; break; }
                point += delta / length * Mathf.Clamp(distance - skin, 0, length);
                target = point + Vector3.ProjectOnPlane(target - point, hits[nearest].normal);
            }
            return point;
        }

        void ApplyContact()
        {
            long begin = System.Diagnostics.Stopwatch.GetTimestamp();
            using (Marker.Auto())
            {
                var hand = handVisual.Hand;
                if (hand == null || !hand.IsTrackedDataValid || !hand.GetRootPose(out Pose raw)) { ResetContact(); return; }
                if (trackingOrigin)
                {
                    if (initialized && (Vector3.Distance(lastOriginPosition, trackingOrigin.position) > .05f
                        || Quaternion.Angle(lastOriginRotation, trackingOrigin.rotation) > 5)) ResetContact();
                    lastOriginPosition = trackingOrigin.position; lastOriginRotation = trackingOrigin.rotation;
                }
                var root = handVisual.Root;
                if (initialized)
                    for (int i = 0; i < Samples.Length; i++)
                        if (!SphereClear(previous[i], i == 0 ? palmRadius : fingertipRadius)) { ResetContact(); break; }
                if (ghost)
                {
                    ghost.transform.SetPositionAndRotation(ghostSource.transform.position, ghostSource.transform.rotation);
                    var a = ghostSource.transform.lossyScale; var b = ghostHolder.lossyScale;
                    ghost.transform.localScale = new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
                    for (int i = 0; i < visualTransforms.Length; i++)
                    {
                        if (i == 0) ghostTransforms[i].SetPositionAndRotation(root.position, root.rotation);
                        else { ghostTransforms[i].localPosition = visualTransforms[i].localPosition; ghostTransforms[i].localRotation = visualTransforms[i].localRotation; }
                        ghostTransforms[i].localScale = visualTransforms[i].localScale;
                    }
                }
                for (int i = 0; i < Samples.Length; i++)
                {
                    int jointIndex = (int)Samples[i];
                    if (jointIndex >= handVisual.Joints.Count || !handVisual.Joints[jointIndex]) { ResetContact(); return; }
                    Vector3 joint = handVisual.Joints[jointIndex].position;
                    targets[i] = i == 0 ? (joint + root.position) * .5f : joint;
                    // Recover by crossing the real surface from the headset side, including a target already inside a closed mesh.
                    if (!initialized) previous[i] = recoveryAnchor ? recoveryAnchor.position + targets[i] - root.position : targets[i];
                }
                Vector3 offset = Vector3.zero;
                // ponytail: six spheres translate the whole visual skeleton; add finger articulation only if headset evidence requires it.
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < Samples.Length; i++)
                    {
                        Vector3 candidate = targets[i] + offset;
                        offset += ResolveSphere(previous[i], candidate, i == 0 ? palmRadius : fingertipRadius) - candidate;
                    }
                bool clear = true;
                for (int i = 0; i < Samples.Length; i++) clear &= SphereClear(targets[i] + offset, i == 0 ? palmRadius : fingertipRadius);
                if (!clear)
                {
                    UnresolvedContacts++;
                    if (!initialized) { ResetContact(); return; }
                    // A wedged multi-contact pose is rejected as a whole; later corrections cannot penetrate an earlier sphere.
                    offset = acceptedRoot.position - root.position;
                    root.SetPositionAndRotation(acceptedRoot.position, acceptedRoot.rotation);
                    for (int i = 1; i < visualTransforms.Length; i++)
                    { visualTransforms[i].localPosition = acceptedPositions[i]; visualTransforms[i].localRotation = acceptedRotations[i]; }
                }
                else
                {
                    root.position += offset;
                    acceptedRoot = new Pose(root.position, root.rotation);
                    for (int i = 0; i < visualTransforms.Length; i++)
                    { acceptedPositions[i] = visualTransforms[i].localPosition; acceptedRotations[i] = visualTransforms[i].localRotation; }
                    for (int i = 0; i < Samples.Length; i++) previous[i] = targets[i] + offset;
                }
                appliedOffset = offset; renderedRootPosition = root.position;
                SeparationMetres = offset.magnitude;
                if (ghost) ghost.enabled = DeepPenetration && handVisual.IsVisible;
                if (SeparationMetres > skin) ContactUpdates++;
                initialized = true;
            }
            LastUpdateMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - begin) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            MaxUpdateMilliseconds = Math.Max(MaxUpdateMilliseconds, LastUpdateMilliseconds);
        }
    }
}
