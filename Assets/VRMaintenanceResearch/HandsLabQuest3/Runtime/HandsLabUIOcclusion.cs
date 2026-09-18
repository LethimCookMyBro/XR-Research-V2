using System;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace VRMaintenanceResearch.HandsLabQuest3.Runtime
{
    /// <summary>Adds solid-physics occlusion to an SDK UI surface. ISDK RayInteractor
    /// normally compares only RayInteractable surfaces, not arbitrary wall colliders.</summary>
    public sealed class HandsLabUIOcclusion : MonoBehaviour, ISurface
    {
        [SerializeField] RayInteractable rayInteractable;
        [SerializeField] UnityEngine.Object originalSurface;
        [SerializeField] UnityEngine.Object originalSelectSurface;
        [SerializeField] bool selectChannel;
        [SerializeField] HandsLabUIOcclusion selectWrapper;
        [SerializeField] HandsLabUIOcclusion owner;
        public Transform ownPanelRoot;
        public LayerMask occlusionMask = ~0;
        [Min(.0001f)] public float surfaceToleranceMetres = .002f;
        public string LastOccluder { get; private set; } = "";
        ISurface Original => originalSurface as ISurface;
        public Transform Transform => Original != null ? Original.Transform : transform;

        public void Configure(RayInteractable ray, Transform panelRoot = null)
        {
            if (selectChannel) throw new InvalidOperationException("Configure the parent hover wrapper, not its select channel.");
            if (!ray) throw new ArgumentNullException(nameof(ray));
            if (rayInteractable && rayInteractable != ray)
                throw new InvalidOperationException("One UI occlusion component per RayInteractable.");
            rayInteractable = ray;
            if (!ReferenceEquals(ray.Surface, this) && ray.Surface != null)
                originalSurface = ray.Surface as UnityEngine.Object;
#if UNITY_EDITOR
            // Inspector-built SDK prefabs have not run Awake outside Play Mode.
            // Read the verified SDK205 serialized fields without invoking lifecycle methods.
            var serialized = new UnityEditor.SerializedObject(ray);
            if (!originalSurface) originalSurface = serialized.FindProperty("_surface").objectReferenceValue;
            if (!originalSelectSurface) originalSelectSurface = serialized.FindProperty("_selectSurface").objectReferenceValue;
#endif
            if (Original == null || ReferenceEquals(Original, this))
                throw new InvalidOperationException("Configure requires an existing SDK UI ISurface.");
            if (originalSelectSurface == this) originalSelectSurface = originalSurface;
            if (!originalSelectSurface) originalSelectSurface = originalSurface;
            if (panelRoot) ownPanelRoot = panelRoot;
            else if (!ownPanelRoot)
            {
                var canvas = ray.GetComponentInParent<Canvas>();
                if (!canvas && Original.Transform) canvas = Original.Transform.GetComponentInParent<Canvas>();
                ownPanelRoot = canvas ? canvas.transform : ray.transform;
            }
            if (originalSelectSurface != originalSurface)
            {
                if (!selectWrapper)
                {
                    var child = new GameObject("HandsLab_SelectOcclusion");
                    child.transform.SetParent(transform, false);
                    selectWrapper = child.AddComponent<HandsLabUIOcclusion>();
                }
                selectWrapper.selectChannel = true;
                selectWrapper.owner = this;
                selectWrapper.originalSurface = originalSelectSurface;
                selectWrapper.ownPanelRoot = ownPanelRoot;
            }
            Bind();
        }

        void OnEnable() { Bind(); }
        void Bind()
        {
            if (selectChannel || !rayInteractable || Original == null) return;
            rayInteractable.InjectSurface(this);
            rayInteractable.InjectOptionalSelectSurface(selectWrapper ? selectWrapper : this);
        }
        void OnDisable()
        {
            if (selectChannel || !rayInteractable || Original == null) return;
            rayInteractable.InjectSurface(Original);
            rayInteractable.InjectOptionalSelectSurface(originalSelectSurface as ISurface ?? Original);
        }

        public bool Raycast(in Ray ray, out SurfaceHit hit, float maxDistance = 0)
        {
            LastOccluder = "";
            if (Original == null || !Original.Raycast(ray, out hit, maxDistance)) { hit = default; return false; }
            // Use all hits because the first may be this panel's own collider. Ignore
            // trigger-only interaction volumes, but include ordinary walls and objects.
            var settings = owner ? owner : this;
            foreach (var obstruction in Physics.RaycastAll(ray, hit.Distance, settings.occlusionMask, QueryTriggerInteraction.Ignore))
            {
                var collider = obstruction.collider;
                if (!collider || (settings.ownPanelRoot && collider.transform.IsChildOf(settings.ownPanelRoot))) continue;
                if (BlocksBeforeSurface(obstruction.distance, hit.Distance, settings.surfaceToleranceMetres))
                {
                    LastOccluder = collider.name;
                    hit = default;
                    return false;
                }
            }
            return true;
        }
        public bool ClosestSurfacePoint(in Vector3 point, out SurfaceHit hit, float maxDistance = 0)
        {
            if (Original != null) return Original.ClosestSurfacePoint(point, out hit, maxDistance);
            hit = default; return false;
        }
        public static bool BlocksBeforeSurface(float blockerDistance, float surfaceDistance, float tolerance)
            => blockerDistance >= 0 && blockerDistance < surfaceDistance - tolerance;
        public static string RunLogicChecks()
        {
            if (!BlocksBeforeSurface(.4f, 1f, .002f)
                || BlocksBeforeSurface(1.2f, 1f, .002f)
                || BlocksBeforeSurface(1f, 1f, .002f)
                || BlocksBeforeSurface(.999f, 1f, .002f)
                || !BlocksBeforeSurface(0f, 1f, .002f))
                throw new InvalidOperationException("UI occlusion distance logic failed.");
            return "UI_OCCLUSION_LOGIC_PASS checks=5; collider/input tests separate";
        }
    }
}
