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
    /// <summary>Owned preview fixtures for the scoped blue finish, including preservation and failed-release retry.</summary>
    public static class ResearchImportedFanBlueFinishChecks
    {
        public static void Run(string outputPath = null)
        {
            var active = SceneManager.GetActiveScene();
            if (!active.IsValid() || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Stable EditMode required.");
            var roots = active.GetRootGameObjects(); string original = ResearchEnvironmentNativeCapture.OriginalState(roots); bool dirty = active.isDirty;
            Scene preview = default; var owned = new List<Object>(); var checks = new List<object>(); var cleanup = new List<string>(); string failure = null;
            ResearchImportedFanBlueFinish.OwnedAssignment assignment = null;
            void Check(string name, bool passed) { checks.Add(new { check = name, passed }); if (!passed) throw new InvalidOperationException(name); }
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit"); if (!shader) throw new InvalidOperationException("Pinned URP Lit required.");
                var map = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "PBR_PaintedMetal_BaseColor", hideFlags = HideFlags.HideAndDontSave }; owned.Add(map);
                var source = Mat("SP_Blue", shader, owned); source.SetTexture("_BaseMap", map); source.SetFloat("_BumpScale", 1); source.SetFloat("_Smoothness", .9f);
                source.SetColor("_BaseColor", new Color(.16f, .36f, .58f, 1)); source.enableInstancing = true; source.renderQueue = 2460;
                foreach (string property in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" }) { var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "OwnedSeed" + property, hideFlags = HideFlags.HideAndDontSave }; owned.Add(texture); source.SetTexture(property, texture); }
                foreach (string keyword in new[] { "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP" }) source.EnableKeyword(keyword);
                var steel = Mat("SP_Steel", shader, owned); var ink = Mat("Ink", shader, owned); var yellow = Mat("Yellow.001", shader, owned); var foreign = Mat("OwnedForeign", shader, owned);
                preview = EditorSceneManager.NewPreviewScene(); var root = Node("Environment_V2", null, preview); var fan = Node("Fan", root.transform, preview); var white = Mat("White.001", shader, owned); var equipment = Mat("R02_Equipment.001", shader, owned);
                var targets = new[] {
                    Renderer("Fan_DiagnosticTray", fan.transform, new[] { source }, preview, owned),
                    Renderer("Fan_InspectionTray", fan.transform, new[] { source }, preview, owned),
                    Renderer("Fan_ReturnTray", fan.transform, new[] { source }, preview, owned),
                    Renderer("Fan_SparesRack", fan.transform, new[] { white, steel, equipment, source, steel }, preview, owned),
                    Renderer("Fan_ToolPanel", fan.transform, new[] { source, ink, steel, yellow, steel }, preview, owned) };
                int[] slots = { 0, 0, 0, 3, 0 };
                string[] meshes = { "Cube.037_73", "Cube.033_70", "Cube.040_39", "Cube.041_123", "Cube.035_0" };
                for (int i = 0; i < targets.Length; i++) targets[i].GetComponent<MeshFilter>().sharedMesh.name = meshes[i];
                var control = Renderer("OtherBlueControl", fan.transform, new[] { source }, preview, owned);
                var sourceJson = EditorJsonUtility.ToJson(source); bool sourceDirty = EditorUtility.IsDirty(source);
                var arrays = targets.Select(r => r.sharedMaterials).ToArray();
                var meshIds = targets.Select(r => r.GetComponent<MeshFilter>().sharedMesh.GetInstanceID()).ToArray();
                var matrices = targets.Select(r => r.localToWorldMatrix).ToArray(); var bounds = targets.Select(r => r.bounds).ToArray();
                var colliderJson = targets.Select(r => EditorJsonUtility.ToJson(r.GetComponent<BoxCollider>())).ToArray();
                Func<ResearchImportedFanBlueFinish.TargetInfo[]> fresh = () => targets.Select(ResearchImportedFanBlueFinish.Describe).ToArray();
                string canonical = ResearchImportedFanBlueFinish.CanonicalScene;
                Check("valid exact three metadata", ResearchImportedFanBlueFinish.ValidateTargets(canonical, fresh(), out _));
                foreach (string invalid in new[] { "wrong_scene", "missing", "duplicate", "wrong_path", "wrong_slots", "other_source", "wrong_shader", "wrong_map", "inactive", "disabled", "script_canvas", "property_block", "batched", "submesh_count", "submesh_start", "no_mesh", "required_property" })
                {
                    var info = fresh(); string path = canonical;
                    switch (invalid)
                    {
                        case "wrong_scene": path = "Assets/Other.unity"; break;
                        case "missing": info = info.Take(2).ToArray(); break;
                        case "duplicate": info[2].RendererId = info[0].RendererId; break;
                        case "wrong_path": info[2].Path += "_Other"; break;
                        case "wrong_slots": info[3].Materials = new[] { "SP_Blue", "SP_Steel", "R02_Equipment.001", "White.001", "SP_Steel" }; break;
                        case "other_source": info[2].SourceMaterialId++; break;
                        case "wrong_shader": info[2].Shader = "Unlit/Color"; break;
                        case "wrong_map": info[2].BaseMap = null; break;
                        case "inactive": info[2].Active = false; break;
                        case "disabled": info[2].Enabled = false; break;
                        case "script_canvas": info[2].HasCanvasOrScript = true; break;
                        case "property_block": info[2].HasPropertyBlock = true; break;
                        case "batched": info[2].Batched = true; break;
                        case "submesh_count": info[2].MeshSubmeshes++; break;
                        case "submesh_start": info[2].Start = 1; break;
                        case "no_mesh": info[2].MeshId = 0; break;
                        case "required_property": info[2].RequiredProperties = false; break;
                    }
                    Check(invalid + " metadata rejected", !ResearchImportedFanBlueFinish.ValidateTargets(path, info, out _));
                }
                for (int ordinal = 0; ordinal < targets.Length; ordinal++)
                {
                    var wrong = fresh(); wrong[ordinal].MeshName += "_Other";
                    Check("ordinal " + ordinal + " unexpected actual mesh rejected", !ResearchImportedFanBlueFinish.ValidateTargets(canonical, wrong, out _));
                    wrong = fresh(); wrong[ordinal].HasPropertyBlock = true;
                    Check("ordinal " + ordinal + " material property block rejected", !ResearchImportedFanBlueFinish.ValidateTargets(canonical, wrong, out _));
                    wrong = fresh(); wrong[ordinal].Path += "_Other";
                    Check("ordinal " + ordinal + " hierarchy mismatch rejected", !ResearchImportedFanBlueFinish.ValidateTargets(canonical, wrong, out _));
                    wrong = fresh(); wrong[ordinal] = null;
                    Check("ordinal " + ordinal + " null target rejected", !ResearchImportedFanBlueFinish.ValidateTargets(canonical, wrong, out _));
                    wrong = fresh(); wrong[ordinal].SourceMaterialId = 0;
                    Check("ordinal " + ordinal + " missing source identity rejected", !ResearchImportedFanBlueFinish.ValidateTargets(canonical, wrong, out _));
                }
                bool threw = false; targets[2].name += "_Other";
                try { using (ResearchImportedFanBlueFinish.AcquireFixture(targets)) { } } catch (InvalidOperationException) { threw = true; }
                targets[2].name = "Fan_ReturnTray";
                Check("actual third bad path assigns no earlier target", threw && targets.Select((r, i) => r.sharedMaterials.SequenceEqual(arrays[i])).All(v => v));
                var block = new MaterialPropertyBlock(); block.SetFloat("_Smoothness", .8f); targets[2].SetPropertyBlock(block, 0); threw = false;
                try { using (ResearchImportedFanBlueFinish.AcquireFixture(targets)) { } } catch (InvalidOperationException) { threw = true; }
                targets[2].SetPropertyBlock(null, 0);
                Check("actual third MPB rejects before assignment", threw && targets.Select((r, i) => r.sharedMaterials.SequenceEqual(arrays[i])).All(v => v));
                block.SetFloat("_Smoothness", .8f); targets[3].SetPropertyBlock(block, 1); threw = false;
                try { using (ResearchImportedFanBlueFinish.AcquireFixture(targets)) { } } catch (InvalidOperationException) { threw = true; }
                targets[3].SetPropertyBlock(null, 1);
                Check("Rack MPB in unselected slot rejects before any assignment", threw && targets.Select((r, i) => r.sharedMaterials.SequenceEqual(arrays[i])).All(v => v));
                threw = false;
                try { ResearchImportedFanBlueFinish.AcquireFixture(targets, i => { if (i == 1) throw new InvalidOperationException("Owned injected second setter failure"); }); }
                catch (InvalidOperationException) { threw = true; }
                Check("injected partial assignment restores actual slots", threw && targets.Select((r, i) => r.sharedMaterials.SequenceEqual(arrays[i])).All(v => v));
                bool rejectRestore = true;
                assignment = ResearchImportedFanBlueFinish.AcquireFixture(targets, null, i => { if (i == 0 && rejectRestore) throw new InvalidOperationException("Owned injected first restore failure"); });
                var retained = assignment.Clone; assignment.Dispose();
                Check("failed restore retains live clone and attempts all four later targets", retained && assignment.Clone == retained && assignment.ReleaseErrors.Count > 0 && targets[0].sharedMaterials[0] == retained && targets.Skip(1).Select((r,i) => r.sharedMaterials.SequenceEqual(arrays[i+1])).All(v => v));
                rejectRestore = false; assignment.Dispose();
                Check("release retry restores last owned slot then destroys clone", assignment.ReleaseErrors.Count == 0 && !retained && assignment.Clone == null && targets.Select((r, i) => r.sharedMaterials.SequenceEqual(arrays[i])).All(v => v));
                assignment = ResearchImportedFanBlueFinish.AcquireFixture(targets); var clone = assignment.Clone;
                Check("one shared named clone on five exact selected Fan slots", clone && clone != source && clone.name == "SP_Blue" && targets.Select((r,i) => r.sharedMaterials[slots[i]] == clone).All(v => v));
                Check("recipe exact three property values", !clone.GetTexture("_BaseMap") && clone.GetFloat("_BumpScale") == .12f && clone.GetFloat("_Smoothness") == .35f);
                Check("other shader properties and render metadata retained", OtherMaterialPropertiesEqual(source, clone));
                string configured = EditorJsonUtility.ToJson(clone); ResearchImportedFanBlueFinish.ConfigureOwnedMaterial(source, clone);
                Check("owned recipe idempotent", EditorJsonUtility.ToJson(clone) == configured);
                Check("source owned material JSON and dirty state unchanged", EditorJsonUtility.ToJson(source) == sourceJson && EditorUtility.IsDirty(source) == sourceDirty);
                Check("all unselected Fan slots and separate shared-blue control unchanged", targets.Select((r,i) => r.sharedMaterials.Where((m,slot) => slot != slots[i]).SequenceEqual(arrays[i].Where((m,slot) => slot != slots[i]))).All(v => v) && control.sharedMaterials.Single() == source);
                Check("mesh matrix bounds colliders shadows layers unchanged", targets.Select((r, i) => r.GetComponent<MeshFilter>().sharedMesh.GetInstanceID() == meshIds[i] && r.localToWorldMatrix.Equals(matrices[i]) && r.bounds.Equals(bounds[i]) &&
                    EditorJsonUtility.ToJson(r.GetComponent<BoxCollider>()) == colliderJson[i] && r.shadowCastingMode == ShadowCastingMode.On && r.receiveShadows && r.gameObject.layer == 0 && r.enabled).All(v => v));
                threw = false; try { using (ResearchImportedFanBlueFinish.AcquireFixture(targets)) { } } catch (InvalidOperationException) { threw = true; }
                Check("already-owned targets do not allocate second assignment", threw && targets[2].sharedMaterials[0] == clone);
                var changed = targets[3].sharedMaterials; changed[0] = foreign; targets[3].sharedMaterials = changed;
                targets[2].sharedMaterials = new[] { foreign }; assignment.Dispose(); assignment.Dispose();
                Check("cleanup conditional slots and collateral preservation", assignment.ReleaseErrors.Count == 0 && !clone && targets[0].sharedMaterials[0] == source && targets[1].sharedMaterials[0] == source && targets[2].sharedMaterials[0] == foreign && targets[3].sharedMaterials[0] == foreign && targets[3].sharedMaterials[3] == source && targets[4].sharedMaterials[0] == source);
                Check("cleanup source and separate control preserved", EditorJsonUtility.ToJson(source) == sourceJson && control.sharedMaterials.Single() == source);
                assignment = null;
            }
            catch (Exception error) { failure = error.ToString(); }
            finally
            {
                if (assignment != null) { assignment.Dispose(); cleanup.AddRange(assignment.ReleaseErrors); }
                try { if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); } catch (Exception error) { cleanup.Add(error.ToString()); }
                foreach (var value in owned) try { if (value) Object.DestroyImmediate(value); } catch (Exception error) { cleanup.Add(error.ToString()); }
            }
            bool preserved = SceneManager.GetActiveScene() == active && active.isDirty == dirty && ResearchEnvironmentNativeCapture.OriginalState(roots) == original;
            bool passed = failure == null && cleanup.Count == 0 && preserved;
            if (outputPath != null) { Directory.CreateDirectory(Path.GetDirectoryName(outputPath)); File.WriteAllText(outputPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { passed, count = checks.Count, checks, failure, cleanup, original_scene_preserved = preserved,
                scope = "Owned preview policy/cleanup fixture only; no production integration, visual, native performance or optical acceptance." }, Newtonsoft.Json.Formatting.Indented)); }
            if (!passed) throw new InvalidOperationException("Owned blue finish fixture failed: " + failure);
            Debug.Log("ENV_FAN_BLUE_FINISH_FIXTURE_PASS " + checks.Count);
        }
        static Material Mat(string name, Shader shader, List<Object> owned) { var m = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave }; owned.Add(m); return m; }
        static GameObject Node(string name, Transform parent, Scene scene)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(go, scene); if (parent) go.transform.SetParent(parent, false); return go;
        }
        static MeshRenderer Renderer(string name, Transform parent, Material[] materials, Scene scene, List<Object> owned)
        {
            var mesh = new Mesh { name = "OwnedBlueFixtureMesh", hideFlags = HideFlags.HideAndDontSave }; owned.Add(mesh);
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.subMeshCount = materials.Length;
            for (int i = 0; i < materials.Length; i++) mesh.SetTriangles(new[] { 0, 1, 2 }, i); mesh.RecalculateBounds();
            var go = Node(name, parent, scene); go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = materials; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
            go.AddComponent<BoxCollider>(); return r;
        }
        static bool OtherMaterialPropertiesEqual(Material a, Material b)
        {
            if (a.shader != b.shader || a.renderQueue != b.renderQueue || a.enableInstancing != b.enableInstancing || a.doubleSidedGI != b.doubleSidedGI || a.globalIlluminationFlags != b.globalIlluminationFlags || !a.shaderKeywords.SequenceEqual(b.shaderKeywords) ||
                a.GetTag("RenderType", false) != b.GetTag("RenderType", false) || a.GetShaderPassEnabled("MOTIONVECTORS") != b.GetShaderPassEnabled("MOTIONVECTORS")) return false;
            for (int i = 0; i < a.shader.GetPropertyCount(); i++)
            {
                string p = a.shader.GetPropertyName(i); if (p == "_BaseMap" || p == "_BumpScale" || p == "_Smoothness") continue;
                switch (a.shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: if (!a.GetColor(p).Equals(b.GetColor(p))) return false; break;
                    case ShaderPropertyType.Vector: if (!a.GetVector(p).Equals(b.GetVector(p))) return false; break;
                    case ShaderPropertyType.Float: case ShaderPropertyType.Range: if (a.GetFloat(p) != b.GetFloat(p)) return false; break;
                    case ShaderPropertyType.Int: if (a.GetInteger(p) != b.GetInteger(p)) return false; break;
                    case ShaderPropertyType.Texture: if (a.GetTexture(p) != b.GetTexture(p) || !a.GetTextureScale(p).Equals(b.GetTextureScale(p)) || !a.GetTextureOffset(p).Equals(b.GetTextureOffset(p))) return false; break;
                }
            }
            return a.GetTextureScale("_BaseMap").Equals(b.GetTextureScale("_BaseMap")) && a.GetTextureOffset("_BaseMap").Equals(b.GetTextureOffset("_BaseMap"));
        }
    }
}
#endif
