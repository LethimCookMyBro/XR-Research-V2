using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Cast-only policy for four verified imported ENV sign meshes. Only runtime caster flags change; no saved scene, material or UI assets.</summary>
    public static class ResearchImportedEnvironmentSignShadows
    {
        public const string CanonicalScene = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        static readonly string[] Paths = {
            "Environment_V2/Training/Room_Title_0", "Environment_V2/Computer/Room_Title_1",
            "Environment_V2/Fan/Room_Title_2", "Environment_V2/Training/Room_Steps_0" };
        static readonly string[] Materials = { "V2_Ink", "V2_Ink", "V2_Ink", "SP_Paint" };

        // Immutable-by-convention local metadata; public validation is read-only and supports bounded fixtures.
        public sealed class TargetInfo
        {
            public string Path, MaterialName;
            public int RendererId, MeshId, MaterialId, MeshVertices, RendererCount, MeshFilterCount, MaterialCount;
            public bool HasCanvas, HasScript, Active, Enabled;
            public ShadowCastingMode Casting;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplyAtSceneLoad()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != CanonicalScene) return;
            try
            {
                var roots = scene.GetRootGameObjects().Where(g => g.name == "Environment_V2").ToArray();
                if (roots.Length != 1) throw new InvalidOperationException("Expected exactly one canonical Environment_V2 root.");
                var all = roots[0].GetComponentsInChildren<MeshRenderer>(true);
                var targets = new MeshRenderer[Paths.Length];
                for (int i = 0; i < Paths.Length; i++)
                {
                    var found = all.Where(r => HierarchyPath(r.transform) == Paths[i]).ToArray();
                    if (found.Length != 1) throw new InvalidOperationException("Expected exactly one imported ENV sign: " + Paths[i]);
                    targets[i] = found[0];
                }
                var info = targets.Select(Describe).ToArray();
                var snapshots = targets.Select(r => new RetainedState(r)).ToArray();
                // Validate every selected object before the first setter. Main-thread synchronous startup, no yields.
                if (!ApplyValidated(scene.path, info, i => targets[i].shadowCastingMode = ShadowCastingMode.Off, out string reason))
                    throw new InvalidOperationException(reason);
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i].shadowCastingMode != ShadowCastingMode.Off || !snapshots[i].Matches(targets[i]))
                        throw new InvalidOperationException("Unexpected non-casting delta: " + Paths[i]);
                Debug.Log("ENV_SIGN_SHADOW_POLICY cast-only Off verified for exact four imported ENV meshes.");
            }
            catch (Exception error)
            {
                // Retain failure evidence; never save, alter another target, or guess a restore after an unexpected native error.
                Debug.LogError("ENV_SIGN_SHADOW_POLICY_FAILED " + error);
            }
        }

        public static bool ValidateTargets(string scenePath, IReadOnlyList<TargetInfo> targets, out string reason)
        {
            reason = null;
            if (scenePath != CanonicalScene || targets == null || targets.Count != Paths.Length)
            { reason = "Canonical scene/exact four target inventory required."; return false; }
            var ids = new HashSet<int>();
            for (int i = 0; i < Paths.Length; i++)
            {
                var t = targets[i];
                if (t == null || t.Path != Paths[i] || t.MaterialName != Materials[i] || t.RendererId == 0 ||
                    !ids.Add(t.RendererId) || t.MeshId == 0 || t.MaterialId == 0 || t.MeshVertices <= 0 ||
                    t.RendererCount != 1 || t.MeshFilterCount != 1 || t.MaterialCount != 1 || t.HasCanvas || t.HasScript ||
                    !t.Active || !t.Enabled || (t.Casting != ShadowCastingMode.On && t.Casting != ShadowCastingMode.Off))
                { reason = "Unexpected imported ENV glyph metadata at ordinal " + i + "."; return false; }
            }
            return true;
        }

        // All-or-none preflight, not a promise of transactional Unity setters if the native engine throws mid-batch.
        static bool ApplyValidated(string scenePath, IReadOnlyList<TargetInfo> targets, Action<int> setOff, out string reason)
        {
            if (!ValidateTargets(scenePath, targets, out reason)) return false;
            if (setOff == null) { reason = "Missing owned setter."; return false; }
            for (int i = 0; i < Paths.Length; i++) setOff(i);
            return true;
        }

        static TargetInfo Describe(MeshRenderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>(); var mesh = filter ? filter.sharedMesh : null;
            var materials = renderer.sharedMaterials; var material = materials.Length == 1 ? materials[0] : null;
            return new TargetInfo { Path = HierarchyPath(renderer.transform), MaterialName = material ? material.name : null,
                RendererId = renderer.GetInstanceID(), MeshId = mesh ? mesh.GetInstanceID() : 0,
                MaterialId = material ? material.GetInstanceID() : 0, MeshVertices = mesh ? mesh.vertexCount : 0,
                RendererCount = renderer.GetComponents<Renderer>().Length, MeshFilterCount = renderer.GetComponents<MeshFilter>().Length,
                MaterialCount = materials.Length, HasCanvas = renderer.GetComponentInParent<Canvas>(true) || renderer.GetComponentInChildren<Canvas>(true),
                HasScript = renderer.GetComponents<MonoBehaviour>().Length != 0 || renderer.GetComponentsInChildren<MonoBehaviour>(true).Length != 0,
                Active = renderer.gameObject.activeInHierarchy, Enabled = renderer.enabled, Casting = renderer.shadowCastingMode };
        }

        static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            for (var t = transform; t; t = t.parent) parts.Add(t.name);
            parts.Reverse(); return string.Join("/", parts);
        }

        sealed class RetainedState
        {
            readonly Mesh mesh; readonly Material material; readonly Matrix4x4 matrix; readonly Bounds bounds;
            readonly bool receive, enabled, batching; readonly int layer, start; readonly uint renderingLayer;
            public RetainedState(MeshRenderer r)
            {
                mesh = r.GetComponent<MeshFilter>().sharedMesh; material = r.sharedMaterials.Single();
                matrix = r.transform.localToWorldMatrix; bounds = r.bounds; receive = r.receiveShadows; enabled = r.enabled;
                layer = r.gameObject.layer; renderingLayer = r.renderingLayerMask; batching = r.isPartOfStaticBatch; start = r.subMeshStartIndex;
            }
            public bool Matches(MeshRenderer r) => r && r.GetComponent<MeshFilter>().sharedMesh == mesh &&
                r.sharedMaterials.Length == 1 && r.sharedMaterials[0] == material && r.transform.localToWorldMatrix.Equals(matrix) &&
                r.bounds.Equals(bounds) && r.receiveShadows == receive && r.enabled == enabled && r.gameObject.layer == layer &&
                r.renderingLayerMask == renderingLayer && r.isPartOfStaticBatch == batching && r.subMeshStartIndex == start;
        }
    }
}
