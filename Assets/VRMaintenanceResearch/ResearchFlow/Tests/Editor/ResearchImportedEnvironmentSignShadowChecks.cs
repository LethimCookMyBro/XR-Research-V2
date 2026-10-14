#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Owned preview fixture; no original ENV, TMP, source Assets or scene save.</summary>
    public static class ResearchImportedEnvironmentSignShadowChecks
    {
        public static void Run(string outputPath=null)
        {
            var activeScene=SceneManager.GetActiveScene();
            if(!activeScene.IsValid() || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stable EditMode required for owned preview-only fixture.");
            var originalRoots=activeScene.GetRootGameObjects();
            string originalState=ResearchEnvironmentNativeCapture.OriginalState(originalRoots);
            bool originalDirty=activeScene.isDirty;
            var checks = new List<object>(); var cleanup = new List<string>(); string error = null;
            Scene preview = default; Mesh mesh = null; Material ink = null, paint = null;
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) throw new InvalidOperationException("Existing pinned URP Lit shader missing.");
                mesh = new Mesh { name = "OwnedSignShadowFixtureQuad", hideFlags = HideFlags.HideAndDontSave };
                mesh.vertices = new[] { new Vector3(-.4f,-.2f,0), new Vector3(-.4f,.2f,0), new Vector3(.4f,.2f,0), new Vector3(.4f,-.2f,0) };
                mesh.triangles = new[] { 0,1,2,0,2,3 }; mesh.RecalculateBounds();
                ink = new Material(shader) { name = "V2_Ink", hideFlags = HideFlags.HideAndDontSave };
                paint = new Material(shader) { name = "SP_Paint", hideFlags = HideFlags.HideAndDontSave };
                preview = EditorSceneManager.NewPreviewScene();
                var root = Node("Environment_V2", null, preview);
                var training = Node("Training", root.transform, preview);
                var computer = Node("Computer", root.transform, preview);
                var fan = Node("Fan", root.transform, preview);
                var targets = new[] { Renderer("Room_Title_0", training.transform, mesh, ink, preview),
                    Renderer("Room_Title_1", computer.transform, mesh, ink, preview), Renderer("Room_Title_2", fan.transform, mesh, ink, preview),
                    Renderer("Room_Steps_0", training.transform, mesh, paint, preview) };
                var board = Renderer("Room_Sign_0", training.transform, mesh, paint, preview);
                var describe = typeof(ResearchImportedEnvironmentSignShadows).GetMethod("Describe", BindingFlags.Static | BindingFlags.NonPublic);
                var apply = typeof(ResearchImportedEnvironmentSignShadows).GetMethod("ApplyValidated", BindingFlags.Static | BindingFlags.NonPublic);
                if (describe == null || apply == null) throw new MissingMethodException("Expected exact policy implementation.");
                Func<ResearchImportedEnvironmentSignShadows.TargetInfo[]> fresh = () => targets.Select(r =>
                    (ResearchImportedEnvironmentSignShadows.TargetInfo)describe.Invoke(null, new object[] { r })).ToArray();
                Action<string,bool> require = (name, passed) => { checks.Add(new { check = name, passed }); if (!passed) throw new InvalidOperationException(name); };
                Func<string,ResearchImportedEnvironmentSignShadows.TargetInfo[],Action<int>,bool> invoke = (path, info, setter) =>
                    (bool)apply.Invoke(null, new object[] { path, info, setter, null });
                string canonical = ResearchImportedEnvironmentSignShadows.CanonicalScene;
                var writes = new List<int>();
                var valid = fresh();
                require("valid four exact imported paths/materials", ResearchImportedEnvironmentSignShadows.ValidateTargets(canonical, valid, out _));
                foreach (var test in new[] { "wrong_scene", "missing", "duplicate", "fourth_wrong_ink", "title_wrong_paint", "canvas", "script", "no_mesh", "extra_material", "disabled", "inactive", "shadows_only" })
                {
                    var info = fresh(); string path = canonical;
                    switch (test)
                    {
                        case "wrong_scene": path = "Assets/Other.unity"; break;
                        case "missing": info = info.Take(3).ToArray(); break;
                        case "duplicate": info[3].RendererId = info[0].RendererId; break;
                        case "fourth_wrong_ink": info[3].MaterialName = "V2_Ink"; break;
                        case "title_wrong_paint": info[1].MaterialName = "SP_Paint"; break;
                        case "canvas": info[3].HasCanvas = true; break;
                        case "script": info[3].HasScript = true; break;
                        case "no_mesh": info[3].MeshId = 0; break;
                        case "extra_material": info[3].MaterialCount = 2; break;
                        case "disabled": info[3].Enabled = false; break;
                        case "inactive": info[3].Active = false; break;
                        case "shadows_only": info[3].Casting = ShadowCastingMode.ShadowsOnly; break;
                    }
                    writes.Clear();
                    require(test + " rejects entire preflight with zero setters", !invoke(path, info, i => writes.Add(i)) && writes.Count == 0);
                }
                require("missing setter rejected", !invoke(canonical, fresh(), null));
                var matrices = targets.Select(r => r.transform.localToWorldMatrix).ToArray();
                var bounds = targets.Select(r => r.bounds).ToArray();
                bool boardReceive = board.receiveShadows; var boardMatrix = board.transform.localToWorldMatrix; var boardBounds = board.bounds;
                writes.Clear();
                require("actual owned four renderer cast-only setters", invoke(canonical, fresh(), i => { writes.Add(i); targets[i].shadowCastingMode = ShadowCastingMode.Off; }) && writes.SequenceEqual(new[] { 0,1,2,3 }));
                require("owned target meshes/materials/matrices/bounds/receive retained", targets.Select((r,i) =>
                    r.shadowCastingMode == ShadowCastingMode.Off && r.receiveShadows && r.GetComponent<MeshFilter>().sharedMesh == mesh &&
                    r.sharedMaterials.Length == 1 && r.sharedMaterials[0] == (i == 3 ? paint : ink) &&
                    r.transform.localToWorldMatrix.Equals(matrices[i]) && r.bounds.Equals(bounds[i])).All(x => x));
                require("separate board sharing SP_Paint unchanged", board.shadowCastingMode == ShadowCastingMode.On && board.receiveShadows == boardReceive &&
                    board.transform.localToWorldMatrix.Equals(boardMatrix) && board.bounds.Equals(boardBounds) && board.sharedMaterials.Single() == paint);
                require("Off state idempotently eligible", ResearchImportedEnvironmentSignShadows.ValidateTargets(canonical, fresh(), out _));
                writes.Clear(); bool thrown = false;
                try { invoke(canonical, fresh(), i => { if (i == 1) throw new InvalidOperationException("owned synthetic setter failure"); writes.Add(i); }); }
                catch (TargetInvocationException failure) when (failure.InnerException is InvalidOperationException) { thrown = true; }
                require("unexpected setter failure stays failure, no transactional rollback claim", thrown && writes.SequenceEqual(new[] { 0 }));
            }
            catch (Exception failure) { error = failure.ToString(); }
            finally
            {
                try { if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); } catch (Exception failure) { cleanup.Add(failure.ToString()); }
                foreach (var value in new Object[] { mesh, ink, paint })
                    try { if (value) Object.DestroyImmediate(value); } catch (Exception failure) { cleanup.Add(failure.ToString()); }
            }
            bool preserved=ResearchEnvironmentNativeCapture.OriginalState(originalRoots)==originalState && activeScene.isDirty==originalDirty;
            bool passedAll=error==null && cleanup.Count==0 && preserved;
            if(outputPath!=null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath,Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    passed=passedAll,count=checks.Count,checks,error,cleanup,original_scene_preserved=preserved,dirtyBefore=originalDirty,dirtyAfter=activeScene.isDirty,
                    scope="Owned preview glyph caster and shared board preservation checks; no original ENV/TMP/UI edits, scene save, native pixels or wearer proof."
                },Newtonsoft.Json.Formatting.Indented));
            }
            if(!passedAll)throw new InvalidOperationException("Owned sign shadow preview fixture FAIL: "+error);
            Debug.Log("ENV_SIGN_SHADOW_FIXTURE_PASS "+checks.Count);

        }

        static GameObject Node(string name, Transform parent, Scene scene)
        {
            var value = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(value, scene); if (parent) value.transform.SetParent(parent, false); return value;
        }
        static MeshRenderer Renderer(string name, Transform parent, Mesh mesh, Material material, Scene scene)
        {
            var value = Node(name, parent, scene); value.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = value.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true; return renderer;
        }
    }
}
#endif
