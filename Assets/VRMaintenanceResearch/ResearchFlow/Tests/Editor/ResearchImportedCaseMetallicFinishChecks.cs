#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Owned preview Case mask/lifetime/collider/task-reference fixtures; no source resources or UI writes.</summary>
    public static class ResearchImportedCaseMetallicFinishChecks
    {
        public static void Run(string outputPath = null)
        {
            var active = SceneManager.GetActiveScene();
            if (!active.IsValid() || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Stable EditMode required.");
            var roots = active.GetRootGameObjects(); string original = ResearchEnvironmentNativeCapture.OriginalState(roots); bool dirty = active.isDirty;
            Scene preview = default; var owned = new List<Object>(); var checks = new List<object>(); var cleanup = new List<string>(); string failure = null; bool injectRestoreFailure = false;
            ResearchImportedCaseMetallicFinish.OwnedAssignment assignment = null;
            void Check(string name, bool passed) { checks.Add(new { check = name, passed }); if (!passed) throw new InvalidOperationException(name); }
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit"); if (!shader) throw new InvalidOperationException("Pinned URP Lit required.");
                var sourceMask = Mask("PBR_BrushedSteel_Mask", owned); var candidateMask = Mask(ResearchImportedCaseMetallicFinish.ResourceKey, owned);
                var source = Mat("SP_Steel", shader, owned); var ink = Mat("Ink", shader, owned); var foreign = Mat("OwnedForeign", shader, owned);
                source.SetTexture("_MetallicGlossMap", sourceMask); source.SetFloat("_Metallic", 1); source.SetFloat("_Smoothness", .45f); source.SetFloat("_BumpScale", .35f);
                foreach (string property in new[] { "_BaseMap", "_BumpMap", "_OcclusionMap" }) { var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = "OwnedSeed" + property, hideFlags = HideFlags.HideAndDontSave }; owned.Add(t); source.SetTexture(property, t); }
                foreach (string keyword in new[] { "_METALLICSPECGLOSSMAP", "_NORMALMAP", "_OCCLUSIONMAP" }) source.EnableKeyword(keyword);
                source.SetColor("_BaseColor", new Color(.86f, .89f, .92f, 1)); source.enableInstancing = true; source.renderQueue = 2460;
                preview = EditorSceneManager.NewPreviewScene(); var env = Node("Environment_V2", null, preview); var computer = Node("Computer", env.transform, preview);
                var target = Renderer("Computer_TaskCase", computer.transform, new[] { source, ink, source }, preview, owned);
                var control = Renderer("OtherSteelControl", computer.transform, new[] { source, ink, source }, preview, owned);
                var collider = target.GetComponent<MeshCollider>(); var mesh = target.GetComponent<MeshFilter>().sharedMesh;
                var taskGo = Node("OwnedInactiveExamineTarget", null, preview); taskGo.SetActive(false); var task = taskGo.AddComponent<ResearchExamineTarget>(); task.stableTarget = "A.module"; task.ConfigureFocus(new Collider[] { collider }, target.transform);
                string taskJson = EditorJsonUtility.ToJson(task), sourceJson = EditorJsonUtility.ToJson(source), colliderJson = EditorJsonUtility.ToJson(collider), maskJson = EditorJsonUtility.ToJson(candidateMask);
                bool sourceDirty = EditorUtility.IsDirty(source); var matrix = target.localToWorldMatrix; var bounds = target.bounds;
                var arrays = target.sharedMaterials; Func<ResearchImportedCaseMetallicFinish.TargetInfo> freshTarget = () => ResearchImportedCaseMetallicFinish.Describe(target);
                Func<ResearchImportedCaseMetallicFinish.MaskInfo> freshMask = () => ResearchImportedCaseMetallicFinish.DescribeMask(candidateMask);
                string canonical = ResearchImportedCaseMetallicFinish.CanonicalScene;
                Check("valid exact owned Case and resource metadata", ResearchImportedCaseMetallicFinish.Validate(canonical, freshTarget(), freshMask(), false, out _));
                foreach (string invalid in new[] { "scene", "path", "mesh_name", "mesh_count", "mesh_id", "start", "batch", "inactive", "disabled", "structure", "canvas_script", "mpb", "slot_order", "shared_steel", "source_mask", "shader", "keyword", "properties", "missing_mask", "mask_name", "size", "mips", "af", "filter", "bias", "srgb", "readable", "streaming", "wrap", "android_format" })
                {
                    var t = freshTarget(); var m = freshMask(); string scene = canonical; bool android = false;
                    switch (invalid)
                    {
                        case "scene": scene = "Other"; break; case "path": t.Path += "_Other"; break; case "mesh_name": t.Mesh += "_Other"; break;
                        case "mesh_count": t.Submeshes = 4; break; case "mesh_id": t.MeshId = 0; break; case "start": t.Start = 1; break;
                        case "batch": t.Batched = true; break; case "inactive": t.Active = false; break; case "disabled": t.Enabled = false; break;
                        case "structure": t.Structure = false; break; case "canvas_script": t.CanvasOrScript = true; break; case "mpb": t.PropertyBlock = true; break;
                        case "slot_order": t.Materials = new[] { "Ink", "SP_Steel", "SP_Steel" }; break; case "shared_steel": t.SharedSteel = false; break;
                        case "source_mask": t.SourceMask = "Other"; break; case "shader": t.Shader = "Other"; break; case "keyword": t.Keywords = new[] { "_NORMALMAP", "_OCCLUSIONMAP" }; break;
                        case "properties": t.RequiredProperties = false; break; case "missing_mask": m = null; break; case "mask_name": m.Name = "Other"; break;
                        case "size": m.Width = 512; break; case "mips": m.Mips = 10; break; case "af": m.Aniso = 1; break; case "filter": m.Filter = FilterMode.Bilinear; break;
                        case "bias": m.Bias = 1; break; case "srgb": m.SRGB = true; break; case "readable": m.Readable = true; break; case "streaming": m.Streaming = true; break;
                        case "wrap": m.Repeat = false; break; case "android_format": android = true; break;
                    }
                    Check(invalid + " preflight rejected", !ResearchImportedCaseMetallicFinish.Validate(scene, t, m, android, out _));
                }
                var androidInfo = freshMask(); androidInfo.Format = TextureFormat.ASTC_6x6;
                Check("pure Android ASTC6x6 descriptor accepted", ResearchImportedCaseMetallicFinish.Validate(canonical, freshTarget(), androidInfo, true, out _));
                bool threw = false; target.name += "_Other";
                try { using (ResearchImportedCaseMetallicFinish.AcquireFixture(target, candidateMask)) { } } catch (InvalidOperationException) { threw = true; }
                target.name = "Computer_TaskCase"; Check("actual bad path has no slot assignment", threw && target.sharedMaterials.SequenceEqual(arrays));
                var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Metallic", .2f); target.SetPropertyBlock(mpb, 2); threw = false;
                try { using (ResearchImportedCaseMetallicFinish.AcquireFixture(target, candidateMask)) { } } catch (InvalidOperationException) { threw = true; }
                target.SetPropertyBlock(null, 2); Check("actual unselected-slot MPB blocks entire assignment", threw && target.sharedMaterials.SequenceEqual(arrays));
                threw = false; try { ResearchImportedCaseMetallicFinish.AcquireFixture(target, candidateMask, () => { throw new InvalidOperationException("Owned assignment failure"); }); } catch (InvalidOperationException) { threw = true; }
                Check("injected pre-set failure preserves source slots and resource", threw && target.sharedMaterials.SequenceEqual(arrays) && candidateMask && EditorJsonUtility.ToJson(candidateMask) == maskJson);
                injectRestoreFailure = true; assignment = ResearchImportedCaseMetallicFinish.AcquireFixture(target, candidateMask, null, () => { if (injectRestoreFailure) throw new InvalidOperationException("Owned restore failure"); });
                var retained = assignment.Clone; assignment.Dispose();
                Check("failed release retains live clone and shared mask", retained && assignment.Clone == retained && assignment.ReleaseErrors.Count > 0 && target.sharedMaterials[0] == retained && candidateMask);
                injectRestoreFailure = false; assignment.Dispose();
                Check("retry restores original then destroys only owned material", assignment.ReleaseErrors.Count == 0 && !retained && target.sharedMaterials.SequenceEqual(arrays) && candidateMask && sourceMask);
                assignment = ResearchImportedCaseMetallicFinish.AcquireFixture(target, candidateMask); var clone = assignment.Clone;
                Check("exact front slot uses one named owned copy", clone && clone != source && clone.name == "SP_Steel" && target.sharedMaterials[0] == clone && target.sharedMaterials[1] == ink && target.sharedMaterials[2] == source);
                Check("only metallic gloss texture binding changes", clone.GetTexture("_MetallicGlossMap") == candidateMask && OtherPropertiesEqual(source, clone));
                string configured = EditorJsonUtility.ToJson(clone); ResearchImportedCaseMetallicFinish.ConfigureOwned(source, clone, candidateMask);
                Check("single binding recipe idempotent", EditorJsonUtility.ToJson(clone) == configured);
                Check("source material and original mask unchanged", EditorJsonUtility.ToJson(source) == sourceJson && EditorUtility.IsDirty(source) == sourceDirty && source.GetTexture("_MetallicGlossMap") == sourceMask);
                Check("separate steel consumer untouched", control.sharedMaterials.SequenceEqual(arrays));
                Check("original mesh transform bounds collider and rendering fields unchanged", target.GetComponent<MeshFilter>().sharedMesh == mesh && target.localToWorldMatrix.Equals(matrix) && target.bounds.Equals(bounds) && EditorJsonUtility.ToJson(collider) == colliderJson && target.shadowCastingMode == ShadowCastingMode.On && target.receiveShadows && target.enabled && target.gameObject.layer == 0);
                var serializedTask = new SerializedObject(task);
                Check("actual external examine module refs and task fields preserved", EditorJsonUtility.ToJson(task) == taskJson && task.stableTarget == "A.module" && serializedTask.FindProperty("focusColliders").arraySize == 1 && serializedTask.FindProperty("focusColliders").GetArrayElementAtIndex(0).objectReferenceValue == collider && serializedTask.FindProperty("focusAnchor").objectReferenceValue == target.transform);
                var changed = target.sharedMaterials; changed[1] = foreign; target.sharedMaterials = changed; assignment.Dispose(); assignment.Dispose();
                Check("release restores owned slot and preserves external collateral", !clone && assignment.ReleaseErrors.Count == 0 && target.sharedMaterials[0] == source && target.sharedMaterials[1] == foreign && target.sharedMaterials[2] == source);
                Check("resource lifetime not owned by policy", candidateMask && sourceMask && EditorJsonUtility.ToJson(candidateMask) == maskJson);
                target.sharedMaterials = (Material[])arrays.Clone(); // Restore owned fixture collateral before a new exact preflight.
                assignment = ResearchImportedCaseMetallicFinish.AcquireFixture(target, candidateMask); var externallyReplaced = assignment.Clone;
                target.sharedMaterials = new[] { foreign, ink, source }; assignment.Dispose();
                Check("external front replacement retained; detached clone destroyed", !externallyReplaced && target.sharedMaterials[0] == foreign && target.sharedMaterials[1] == ink && target.sharedMaterials[2] == source && candidateMask);
                assignment = null;
            }
            catch (Exception error) { failure = error.ToString(); }
            finally
            {
                injectRestoreFailure = false;
                if (assignment != null) { assignment.Dispose(); cleanup.AddRange(assignment.ReleaseErrors); }
                try { if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); } catch (Exception error) { cleanup.Add(error.ToString()); }
                if (assignment != null && assignment.Clone) { assignment.Dispose(); cleanup.AddRange(assignment.ReleaseErrors); }
                foreach (var value in owned) try { if (value) Object.DestroyImmediate(value); } catch (Exception error) { cleanup.Add(error.ToString()); }
            }
            bool preserved = SceneManager.GetActiveScene() == active && active.isDirty == dirty && ResearchEnvironmentNativeCapture.OriginalState(roots) == original;
            bool passed = failure == null && cleanup.Count == 0 && preserved;
            if (outputPath != null) { Directory.CreateDirectory(Path.GetDirectoryName(outputPath)); File.WriteAllText(outputPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { passed, count = checks.Count, checks, failure, cleanup, original_scene_preserved = preserved,
                scope = "Owned preview metadata/binding/release/task-reference fixtures; Android ASTC descriptor acceptance is pure metadata only, not compressed pixels/native optics or actual runtime Apply." }, Newtonsoft.Json.Formatting.Indented)); }
            if (!passed) throw new InvalidOperationException("Owned Case finish fixture failed: " + failure);
            Debug.Log("ENV_CASE_FINISH_FIXTURE_PASS " + checks.Count);
        }
        static Texture2D Mask(string name, List<Object> owned)
        {
            var t = new Texture2D(1024, 1024, TextureFormat.RGBA32, true, true) { name = name, hideFlags = HideFlags.HideAndDontSave, anisoLevel = 4, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat };
            owned.Add(t); t.Apply(false, true); return t;
        }
        static Material Mat(string name, Shader shader, List<Object> owned) { var m = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave }; owned.Add(m); return m; }
        static GameObject Node(string name, Transform parent, Scene scene) { var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(go, scene); if (parent) go.transform.SetParent(parent, false); return go; }
        static MeshRenderer Renderer(string name, Transform parent, Material[] materials, Scene scene, List<Object> owned)
        {
            var mesh = new UnityEngine.Mesh { name = "Cube.013_122", hideFlags = HideFlags.HideAndDontSave }; owned.Add(mesh); mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.subMeshCount = 3;
            for (int i = 0; i < 3; i++) mesh.SetTriangles(new[] { 0, 1, 2 }, i); mesh.RecalculateBounds();
            var go = Node(name, parent, scene); go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = materials; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; go.AddComponent<MeshCollider>().sharedMesh = mesh; return r;
        }
        static bool OtherPropertiesEqual(Material a, Material b)
        {
            if (a.shader != b.shader || a.renderQueue != b.renderQueue || a.enableInstancing != b.enableInstancing || a.doubleSidedGI != b.doubleSidedGI || a.globalIlluminationFlags != b.globalIlluminationFlags || !a.shaderKeywords.SequenceEqual(b.shaderKeywords) || a.GetTag("RenderType", false) != b.GetTag("RenderType", false) || a.GetShaderPassEnabled("MOTIONVECTORS") != b.GetShaderPassEnabled("MOTIONVECTORS")) return false;
            for (int i = 0; i < a.shader.GetPropertyCount(); i++)
            {
                string p = a.shader.GetPropertyName(i); if (p == "_MetallicGlossMap") continue;
                switch (a.shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: if (!a.GetColor(p).Equals(b.GetColor(p))) return false; break; case ShaderPropertyType.Vector: if (!a.GetVector(p).Equals(b.GetVector(p))) return false; break;
                    case ShaderPropertyType.Float: case ShaderPropertyType.Range: if (a.GetFloat(p) != b.GetFloat(p)) return false; break; case ShaderPropertyType.Int: if (a.GetInteger(p) != b.GetInteger(p)) return false; break;
                    case ShaderPropertyType.Texture: if (a.GetTexture(p) != b.GetTexture(p) || !a.GetTextureScale(p).Equals(b.GetTextureScale(p)) || !a.GetTextureOffset(p).Equals(b.GetTextureOffset(p))) return false; break;
                }
            }
            return a.GetTextureScale("_MetallicGlossMap").Equals(b.GetTextureScale("_MetallicGlossMap")) && a.GetTextureOffset("_MetallicGlossMap").Equals(b.GetTextureOffset("_MetallicGlossMap"));
        }
    }
}
#endif