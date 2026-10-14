using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Conditional proposal with retry-safe owned cleanup: one owned finish on three imported ENV slots; no saved material, geometry, UI or scene changes.</summary>
    public static class ResearchImportedEnvironmentBlueFinish
    {
        public const string CanonicalScene = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        static readonly string[] Paths = { "Environment_V2/Training/Training_Storage", "Environment_V2/Computer/Computer_ToolPanel", "Environment_V2/Computer/Computer_ComponentTray" };
        static readonly int[] Slots = { 1, 0, 0 };
        static readonly string[][] Names = { new[] { "SP_Steel", "SP_Blue", "SP_Steel" }, new[] { "SP_Blue", "Ink", "SP_Steel", "Yellow.001", "SP_Steel" }, new[] { "SP_Blue" } };
        static OwnedAssignment owner;

        public sealed class TargetInfo
        {
            public string Path, Shader, BaseMap; public string[] Materials;
            public int RendererId, MeshId, SourceMaterialId, MeshSubmeshes, Start;
            public bool Active, Enabled, HasCanvasOrScript, HasPropertyBlock, Batched, RequiredProperties;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneUnloaded -= SceneUnloaded;
            ReleaseOwner();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded; SceneManager.sceneUnloaded += SceneUnloaded;
            ApplyScene(SceneManager.GetActiveScene());
        }
        static void SceneLoaded(Scene scene, LoadSceneMode mode) => ApplyScene(scene);
        static void SceneUnloaded(Scene scene) { if (owner != null && owner.SceneHandle == scene.handle) ReleaseOwner(); }
        static void ReleaseOwner()
        {
            var previous = owner;
            if (previous == null) return;
            previous.Dispose();
            foreach (string error in previous.ReleaseErrors) Debug.LogError("ENV_BLUE_FINISH_RELEASE_FAILED " + error);
            if (!previous.Clone) owner = null;
        }
        static void ApplyScene(Scene scene)
        {
            if (!Application.isPlaying || !scene.IsValid() || !scene.isLoaded || scene != SceneManager.GetActiveScene() || scene.path != CanonicalScene) return;
            if (owner != null) { if (owner.SceneHandle != scene.handle) Debug.LogError("ENV_BLUE_FINISH_FAILED unexpected existing scene owner."); return; }
            try
            {
                var roots = scene.GetRootGameObjects().Where(g => g.name == "Environment_V2").ToArray();
                if (roots.Length != 1) throw new InvalidOperationException("Expected one canonical Environment_V2 root.");
                var all = roots[0].GetComponentsInChildren<MeshRenderer>(true);
                var targets = Paths.Select(path => all.Single(r => HierarchyPath(r.transform) == path)).ToArray();
                owner = Acquire(scene.handle, targets);
                Debug.Log("ENV_BLUE_FINISH three exact ENV slots use one owned SP_Blue material.");
            }
            catch (Exception error) { Debug.LogError("ENV_BLUE_FINISH_FAILED " + error); }
        }

        public static bool ValidateTargets(string scenePath, IReadOnlyList<TargetInfo> targets, out string reason)
        {
            reason = null;
            if (scenePath != CanonicalScene || targets == null || targets.Count != 3) { reason = "Canonical scene/exact three targets required."; return false; }
            var ids = new HashSet<int>(); int source = targets[0]?.SourceMaterialId ?? 0;
            for (int i = 0; i < 3; i++)
            {
                var t = targets[i];
                if (t == null || t.Path != Paths[i] || !ids.Add(t.RendererId) || t.RendererId == 0 || t.MeshId == 0 || source == 0 ||
                    t.SourceMaterialId != source || t.Materials == null || !t.Materials.SequenceEqual(Names[i]) || t.MeshSubmeshes != Names[i].Length ||
                    t.Start != 0 || t.Batched || !t.Active || !t.Enabled || t.HasCanvasOrScript || t.HasPropertyBlock || !t.RequiredProperties ||
                    t.Shader != "Universal Render Pipeline/Lit" || t.BaseMap != "PBR_PaintedMetal_BaseColor")
                { reason = "Unexpected imported blue ENV metadata at ordinal " + i; return false; }
            }
            return true;
        }
        public static TargetInfo Describe(MeshRenderer renderer, int ordinal)
        {
            if (!renderer || ordinal < 0 || ordinal >= 3) throw new ArgumentException("Three ordered ENV renderers required.");
            var materials = renderer.sharedMaterials; var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            var source = materials.Length > Slots[ordinal] ? materials[Slots[ordinal]] : null;
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); bool hasBlock = !block.isEmpty;
            for (int slot = 0; slot < materials.Length; slot++) { block.Clear(); renderer.GetPropertyBlock(block, slot); hasBlock |= !block.isEmpty; }
            return new TargetInfo { Path = HierarchyPath(renderer.transform), Materials = materials.Select(m => m ? m.name : null).ToArray(),
                RendererId = renderer.GetInstanceID(), MeshId = mesh ? mesh.GetInstanceID() : 0, SourceMaterialId = source ? source.GetInstanceID() : 0,
                Shader = source && source.shader ? source.shader.name : null, BaseMap = source && source.HasProperty("_BaseMap") && source.GetTexture("_BaseMap") ? source.GetTexture("_BaseMap").name : null,
                MeshSubmeshes = mesh ? mesh.subMeshCount : 0, Start = renderer.subMeshStartIndex, Batched = renderer.isPartOfStaticBatch,
                Active = renderer.gameObject.activeInHierarchy, Enabled = renderer.enabled && !renderer.forceRenderingOff,
                HasCanvasOrScript = renderer.GetComponentInParent<Canvas>(true) || renderer.GetComponentInChildren<Canvas>(true) ||
                    renderer.GetComponentsInParent<MonoBehaviour>(true).Length != 0 || renderer.GetComponentsInChildren<MonoBehaviour>(true).Length != 0,
                HasPropertyBlock = hasBlock, RequiredProperties = source && new[] { "_BaseMap", "_BumpScale", "_Smoothness" }.All(source.HasProperty) };
        }
        public static void ConfigureOwnedMaterial(Material source, Material copy)
        {
            if (!source || !copy || source == copy || copy.hideFlags != HideFlags.HideAndDontSave || copy.shader != source.shader || !copy.shader || copy.shader.name != "Universal Render Pipeline/Lit" ||
                new[] { "_BaseMap", "_BumpScale", "_Smoothness" }.Any(p => !copy.HasProperty(p))) throw new InvalidOperationException("Distinct owned URP Lit copy required.");
            copy.SetTexture("_BaseMap", null); copy.SetFloat("_BumpScale", .12f); copy.SetFloat("_Smoothness", .35f);
        }
        static OwnedAssignment Acquire(int sceneHandle, MeshRenderer[] targets, Action<int> beforeAssign = null)
        {
            if (!ValidateTargets(CanonicalScene, targets.Select(Describe).ToArray(), out string reason)) throw new InvalidOperationException(reason);
            var assignment = new OwnedAssignment(sceneHandle, targets);
            try { assignment.Apply(beforeAssign); return assignment; }
            catch
            {
                assignment.Dispose();
                foreach (string error in assignment.ReleaseErrors) Debug.LogError("ENV_BLUE_FINISH_PARTIAL_RELEASE_FAILED " + error);
                if (assignment.Clone && Application.isPlaying) owner = assignment; // Keep failed runtime rollback owned for unload/reset retry.
                throw; // Preflight is all-or-none; native assignments are not promised transactional.
            }
        }
#if UNITY_EDITOR
        // Fixture access cannot target an ordinary loaded scene or source GameObject.
        public static OwnedAssignment AcquireFixture(MeshRenderer[] targets, Action<int> beforeAssign = null, Action<int> beforeRestore = null)
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || targets == null || targets.Length != 3 ||
                targets.Any(r => !r || !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewSceneObject(r) || r.gameObject.scene != targets[0].gameObject.scene))
                throw new InvalidOperationException("Owned preview-only fixture required.");
            var result = Acquire(targets[0].gameObject.scene.handle, targets, beforeAssign); result.BeforeRestore = beforeRestore; return result;
        }
#endif
        public sealed class OwnedAssignment : IDisposable
        {
            readonly MeshRenderer[] targets; readonly Material[][] originals;
            public readonly int SceneHandle; public Material Clone { get; private set; }
            public readonly List<string> ReleaseErrors = new List<string>(); bool disposed;
#if UNITY_EDITOR
            internal Action<int> BeforeRestore;
#endif
            internal OwnedAssignment(int sceneHandle, MeshRenderer[] renderers)
            {
                SceneHandle = sceneHandle; targets = renderers; originals = renderers.Select(r => r.sharedMaterials).ToArray();
            }
            internal void Apply(Action<int> beforeAssign)
            {
                var source = originals[0][Slots[0]];
                Clone = new Material(source) { name = source.name, hideFlags = HideFlags.HideAndDontSave };
                ConfigureOwnedMaterial(source, Clone);
                for (int i = 0; i < 3; i++) { beforeAssign?.Invoke(i); var values = (Material[])originals[i].Clone(); values[Slots[i]] = Clone; targets[i].sharedMaterials = values; }
                for (int i = 0; i < 3; i++) if (!targets[i].sharedMaterials.SequenceEqual(originals[i].Select((m, slot) => slot == Slots[i] ? Clone : m)))
                    throw new InvalidOperationException("Owned blue material assignment mismatch.");
            }
            public void Dispose()
            {
                if (disposed) return; ReleaseErrors.Clear();
                for (int i = 0; i < 3; i++)
                    try
                    {
                        if (!targets[i]) continue;
                        var values = targets[i].sharedMaterials;
                        if (values.Length > Slots[i] && values[Slots[i]] == Clone && Clone) {
#if UNITY_EDITOR
                            BeforeRestore?.Invoke(i);
#endif
                            values[Slots[i]] = originals[i][Slots[i]]; targets[i].sharedMaterials = values;
                            if (targets[i].sharedMaterials[Slots[i]] != originals[i][Slots[i]]) throw new InvalidOperationException("Slot restore getter mismatch."); }
                    }
                    catch (Exception error) { ReleaseErrors.Add("target " + i + ": " + error); }
                bool referenced = false;
                for (int i = 0; i < 3; i++)
                    try { if (targets[i] && Clone && targets[i].sharedMaterials.Contains(Clone)) { referenced = true; ReleaseErrors.Add("target " + i + ": owned clone still referenced; retained for retry."); } }
                    catch (Exception error) { referenced = true; ReleaseErrors.Add("reference verification " + i + ": " + error); }
                if (referenced || ReleaseErrors.Count != 0) return;
                try { if (Clone) { if (Application.isPlaying) UnityEngine.Object.Destroy(Clone); else UnityEngine.Object.DestroyImmediate(Clone); } Clone = null; disposed = true; }
                catch (Exception error) { ReleaseErrors.Add("owned material: " + error); }
            }
        }
        static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>(); for (var t = transform; t; t = t.parent) parts.Add(t.name);
            parts.Reverse(); return string.Join("/", parts);
        }
    }
}
