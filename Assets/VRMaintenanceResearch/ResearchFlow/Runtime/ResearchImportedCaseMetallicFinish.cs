using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Experimental.Rendering;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Conditional proposal: one owned Case-front material; retain packed mask keywords, source assets and task geometry.</summary>
    public static class ResearchImportedCaseMetallicFinish
    {
        public const string CanonicalScene = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        public const string TargetPath = "Environment_V2/Computer/Computer_TaskCase", ResourceKey = "ResearchImportedCaseMetallicMask";
        const string OriginalMask = "PBR_BrushedSteel_Mask";
        static readonly string[] Names = { "SP_Steel", "Ink", "SP_Steel" }, Keywords = { "_METALLICSPECGLOSSMAP", "_NORMALMAP", "_OCCLUSIONMAP" };
        static OwnedAssignment owner;
        public sealed class TargetInfo
        {
            public string Path, Mesh, Shader, SourceMask; public string[] Materials, Keywords;
            public int RendererId, MeshId, Submeshes, Start; public bool Active, Enabled, Structure, CanvasOrScript, PropertyBlock, Batched, SharedSteel, RequiredProperties;
        }
        public sealed class MaskInfo
        {
            public string Name; public int Width, Height, Mips, Aniso; public TextureFormat Format; public FilterMode Filter; public float Bias;
            public bool SRGB, Readable, Streaming, Repeat;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { SceneManager.sceneLoaded -= Loaded; SceneManager.sceneUnloaded -= Unloaded; Release(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded; SceneManager.sceneUnloaded -= Unloaded; SceneManager.sceneUnloaded += Unloaded; Apply(SceneManager.GetActiveScene()); }
        static void Loaded(Scene scene, LoadSceneMode mode) => Apply(scene);
        static void Unloaded(Scene scene) { if (owner != null && owner.SceneHandle == scene.handle) Release(); }
        static void Release()
        {
            var previous = owner; if (previous == null) return; previous.Dispose();
            foreach (string error in previous.ReleaseErrors) Debug.LogError("ENV_CASE_FINISH_RELEASE_FAILED " + error);
            if (!previous.Clone) owner = null;
        }
        static void Apply(Scene scene)
        {
            if (!Application.isPlaying || !scene.IsValid() || !scene.isLoaded || scene != SceneManager.GetActiveScene() || scene.path != CanonicalScene) return;
            if (owner != null) { if (owner.SceneHandle != scene.handle) Debug.LogError("ENV_CASE_FINISH_FAILED unexpected existing owner."); return; }
            try
            {
                var roots = scene.GetRootGameObjects().Where(g => g.name == "Environment_V2").ToArray();
                if (roots.Length != 1) throw new InvalidOperationException("One canonical Environment_V2 required.");
                var renderer = roots[0].GetComponentsInChildren<MeshRenderer>(true).Single(r => PathOf(r.transform) == TargetPath);
                // The S1 Task A parts replaced this prop and it is switched off (ResearchS1SceneIntegration); nothing to finish.
                if (!renderer.gameObject.activeInHierarchy) { Debug.Log("ENV_CASE_FINISH skipped: Case-front prop is switched off."); return; }
                var mask = Resources.Load<Texture2D>(ResourceKey); // Shared resource asset; this policy never destroys or changes it.
                owner = Acquire(scene.handle, renderer, mask, Application.platform == RuntimePlatform.Android);
                Debug.Log("ENV_CASE_FINISH one Case-front slot uses owned SP_Steel copy and retained packed-map keywords.");
            }
            catch (Exception error) { Debug.LogError("ENV_CASE_FINISH_FAILED " + error); }
        }
        public static bool Validate(string scenePath, TargetInfo t, MaskInfo mask, bool requireAndroidFormat, out string reason)
        {
            reason = null;
            if (scenePath != CanonicalScene || t == null || t.Path != TargetPath || t.RendererId == 0 || t.MeshId == 0 || t.Mesh != "Cube.013_122" ||
                t.Materials == null || !t.Materials.SequenceEqual(Names) || !t.SharedSteel || t.Submeshes != 3 || t.Start != 0 || t.Batched ||
                !t.Active || !t.Enabled || !t.Structure || t.CanvasOrScript || t.PropertyBlock || !t.RequiredProperties || t.Shader != "Universal Render Pipeline/Lit" ||
                t.SourceMask != OriginalMask || t.Keywords == null || !t.Keywords.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(Keywords))
            { reason = "Unexpected exact imported Case-front ownership/material/mesh state."; return false; }
            if (mask == null || mask.Name != ResourceKey || mask.Width != 1024 || mask.Height != 1024 || mask.Mips != 11 || mask.Aniso != 4 ||
                mask.Filter != FilterMode.Trilinear || mask.Bias != 0 || mask.SRGB || mask.Readable || mask.Streaming || !mask.Repeat ||
                (requireAndroidFormat && mask.Format != TextureFormat.ASTC_6x6))
            { reason = "Reviewed linear 1024/full11-mip/AF4 packed resource metadata required; Android ASTC6x6."; return false; }
            return true;
        }
        public static TargetInfo Describe(MeshRenderer r)
        {
            if (!r) throw new ArgumentException("Case renderer required."); var a = r.sharedMaterials; var f = r.GetComponent<MeshFilter>(); var m = a.Length > 0 ? a[0] : null;
            var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block); bool mpb = !block.isEmpty;
            for (int i = 0; i < a.Length; i++) { block.Clear(); r.GetPropertyBlock(block, i); mpb |= !block.isEmpty; }
            var mesh = f ? f.sharedMesh : null;
            return new TargetInfo { Path = PathOf(r.transform), Mesh = mesh ? mesh.name : null, RendererId = r.GetInstanceID(), MeshId = mesh ? mesh.GetInstanceID() : 0,
                Materials = a.Select(x => x ? x.name : null).ToArray(), Submeshes = mesh ? mesh.subMeshCount : 0, Start = r.subMeshStartIndex, Batched = r.isPartOfStaticBatch,
                Active = r.gameObject.activeInHierarchy, Enabled = r.enabled && !r.forceRenderingOff, SharedSteel = a.Length == 3 && m && a[2] == m,
                Structure = r.GetComponents<Renderer>().Length == 1 && r.GetComponents<MeshFilter>().Length == 1 && r.GetComponents<MeshCollider>().Length == 1,
                CanvasOrScript = r.GetComponentInParent<Canvas>(true) || r.GetComponentInChildren<Canvas>(true) || r.GetComponentsInParent<MonoBehaviour>(true).Length != 0 || r.GetComponentsInChildren<MonoBehaviour>(true).Length != 0,
                PropertyBlock = mpb, Shader = m && m.shader ? m.shader.name : null, Keywords = m ? m.shaderKeywords : null,
                SourceMask = m && m.HasProperty("_MetallicGlossMap") && m.GetTexture("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap").name : null,
                RequiredProperties = m && new[] { "_MetallicGlossMap", "_BumpMap", "_OcclusionMap" }.All(m.HasProperty) };
        }
        public static MaskInfo DescribeMask(Texture2D t) => t ? new MaskInfo { Name = t.name, Width = t.width, Height = t.height, Mips = t.mipmapCount, Format = t.format,
            Aniso = t.anisoLevel, Filter = t.filterMode, Bias = t.mipMapBias, SRGB = GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat), Readable = t.isReadable, Streaming = t.streamingMipmaps,
            Repeat = t.wrapModeU == TextureWrapMode.Repeat && t.wrapModeV == TextureWrapMode.Repeat && t.wrapModeW == TextureWrapMode.Repeat } : null;
        public static void ConfigureOwned(Material source, Material copy, Texture2D mask)
        {
            if (!source || !copy || source == copy || copy.hideFlags != HideFlags.HideAndDontSave || copy.shader != source.shader || !copy.shader ||
                copy.shader.name != "Universal Render Pipeline/Lit" || !copy.HasProperty("_MetallicGlossMap") || !mask) throw new InvalidOperationException("Distinct owned URP Lit material and shared mask required.");
            copy.SetTexture("_MetallicGlossMap", mask); // Only recipe setter; every scalar, keyword, map ST, other texture and render flag stays copied.
        }
        static OwnedAssignment Acquire(int handle, MeshRenderer target, Texture2D mask, bool android, Action beforeAssign = null)
        {
            if (!Validate(CanonicalScene, Describe(target), DescribeMask(mask), android, out string reason)) throw new InvalidOperationException(reason);
            var assignment = new OwnedAssignment(handle, target);
            try { assignment.Apply(mask, beforeAssign); return assignment; }
            catch { assignment.Dispose(); foreach (string error in assignment.ReleaseErrors) Debug.LogError("ENV_CASE_FINISH_PARTIAL_RELEASE_FAILED " + error);
                if (assignment.Clone && Application.isPlaying) owner = assignment; throw; }
        }
#if UNITY_EDITOR
        public static OwnedAssignment AcquireFixture(MeshRenderer target, Texture2D mask, Action beforeAssign = null, Action beforeRestore = null)
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || !target || !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewSceneObject(target) ||
                !mask || mask.hideFlags != HideFlags.HideAndDontSave) throw new InvalidOperationException("Owned preview renderer/resource fixture required.");
            var value = Acquire(target.gameObject.scene.handle, target, mask, false, beforeAssign); value.BeforeRestore = beforeRestore; return value;
        }
#endif
        public sealed class OwnedAssignment : IDisposable
        {
            readonly MeshRenderer target; readonly Material[] originals; bool disposed;
            public readonly int SceneHandle; public Material Clone { get; private set; } public readonly List<string> ReleaseErrors = new List<string>();
#if UNITY_EDITOR
            internal Action BeforeRestore;
#endif
            internal OwnedAssignment(int handle, MeshRenderer renderer) { SceneHandle = handle; target = renderer; originals = renderer.sharedMaterials; }
            internal void Apply(Texture2D mask, Action beforeAssign)
            {
                Clone = new Material(originals[0]) { name = originals[0].name, hideFlags = HideFlags.HideAndDontSave }; ConfigureOwned(originals[0], Clone, mask);
                beforeAssign?.Invoke(); var a = (Material[])originals.Clone(); a[0] = Clone; target.sharedMaterials = a;
                if (!target.sharedMaterials.SequenceEqual(new[] { Clone, originals[1], originals[2] })) throw new InvalidOperationException("Case slot assignment getter mismatch.");
            }
            public void Dispose()
            {
                if (disposed) return; ReleaseErrors.Clear();
                try
                {
                    if (target) { var a = target.sharedMaterials; if (a.Length > 0 && Clone && a[0] == Clone) {
#if UNITY_EDITOR
                        BeforeRestore?.Invoke();
#endif
                        a[0] = originals[0]; target.sharedMaterials = a; if (target.sharedMaterials[0] != originals[0]) throw new InvalidOperationException("Case restore getter mismatch."); } }
                }
                catch (Exception error) { ReleaseErrors.Add("slot restore: " + error); }
                bool referenced = false;
                try { if (target && Clone && target.sharedMaterials.Contains(Clone)) { referenced = true; ReleaseErrors.Add("Owned Case clone still referenced; retained for retry."); } }
                catch (Exception error) { referenced = true; ReleaseErrors.Add("reference verification: " + error); }
                if (referenced || ReleaseErrors.Count != 0) return;
                try { if (Clone) { if (Application.isPlaying) UnityEngine.Object.Destroy(Clone); else UnityEngine.Object.DestroyImmediate(Clone); } Clone = null; disposed = true; }
                catch (Exception error) { ReleaseErrors.Add("owned material: " + error); }
            }
        }
        static string PathOf(Transform t) { var parts = new List<string>(); for (; t; t = t.parent) parts.Add(t.name); parts.Reverse(); return string.Join("/", parts); }
    }
}