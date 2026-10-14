#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Explicit correction of measured ResearchFlow foot gaps and missing floor UV0.</summary>
    public static class ResearchEnvironmentGrounding
    {
        private const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        private const string MeshRoot = "Assets/VRMaintenanceResearch/ResearchFlow/Meshes";
        private const string EvidenceRoot = "Logs/QuestFinalIntegration";
        private static readonly string[] Benches = { "Training_Practice_A", "Computer_Main", "Computer_Support", "Fan_Main", "Fan_Support" };
        private static readonly string[] Stools = { "Training_Stool_2", "Computer_Stool_Parked", "Fan_Stool_Parked" };
        private static readonly string[] Floors = { "Training_Floor", "Computer_Floor", "Fan_Floor", "Corridor_Floor", "Factory_Floor" };

        public static string Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || scene.path != ScenePath)
                throw new InvalidOperationException("Stable ResearchFlow Edit Mode required.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve dirty scene before grounding: " + SceneManager.GetSceneAt(i).path);

            var environment = scene.GetRootGameObjects().Single(g => g.name == "Environment_V2");
            var meshes = environment.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter Find(string name) => meshes.Single(m => m.name == name);
            var tracked = Benches.Concat(Stools).Concat(Floors).Concat(new[] { "Polish_Training_ReturnCart", "Polish_Training_CartBins", "Robot_A_Base", "Robot_B_Base" }).Select(Find).ToArray();
            var before = Snapshot(tracked);
            var originalScene = File.ReadAllBytes(ScenePath);
            var colliderBefore = environment.GetComponentsInChildren<Collider>(true).Select(c => c.GetInstanceID() + ":" + EditorJsonUtility.ToJson(c)).ToArray();
            var changes = new List<string>();
            var deferred = new List<string>();
            var meshPlans = new List<(MeshFilter target, Mesh mesh, string path)>();
            var moves = new List<(Transform target, Vector3 delta)>();

            foreach (string name in Floors)
            {
                var mf = Find(name);
                Require(Mathf.Abs(WorldVertices(mf).Max(v => v.y)) < .0001f, name + " real floor top differs from audited y=0.");
                string path = MeshRoot + "/Grounded_" + name + ".asset";
                if (AssetDatabase.GetAssetPath(mf.sharedMesh) == path)
                {
                    Require(ValidFloorUV(mf), name + " owned UV mesh changed; inspect before overwriting.");
                    continue;
                }
                Require(mf.sharedMesh.uv.Length == 0, name + " UV0 differs from audited missing UV0.");
                var clone = UnityEngine.Object.Instantiate(mf.sharedMesh);
                clone.name = "Grounded_" + name;
                clone.uv = WorldVertices(mf).Select(v => new Vector2(v.x, v.z)).ToArray();
                // Positions/normals stay identical. Tangents must agree with the newly supplied UVs.
                clone.RecalculateTangents();
                meshPlans.Add((mf, clone, path));
            }
            foreach (string name in Benches)
            {
                var mf = Find(name);
                float min = WorldVertices(mf).Min(v => v.y);
                if (Mathf.Abs(min) < .0001f) continue;
                Require(Mathf.Abs(min - .005f) < .0001f, name + " foot height differs from measured 5 mm.");
                var clone = UnityEngine.Object.Instantiate(mf.sharedMesh);
                clone.name = "Grounded_" + name;
                clone.vertices = GroundPads(mf);
                clone.RecalculateBounds();
                meshPlans.Add((mf, clone, MeshRoot + "/Grounded_" + name + ".asset"));
            }
            foreach (string name in Stools) PlanMove(Find(name), .095f, moves);

            var cart = Find("Polish_Training_ReturnCart");
            float cartGap = WorldVertices(cart).Min(v => v.y);
            if (Mathf.Abs(cartGap) > .0001f)
            {
                Require(Mathf.Abs(cartGap - .0022025f) < .0001f, "Return cart height differs from audit.");
                var bins = Find("Polish_Training_CartBins");
                Require(cart.transform.parent == bins.transform.parent && Mathf.Abs(WorldVertices(bins).Min(v => v.y) - .182f) < .0001f,
                    "Cart sibling contents differ from audit; do not move cart alone.");
                moves.Add((cart.transform, Vector3.down * cartGap));
                moves.Add((bins.transform, Vector3.down * cartGap));
            }
            foreach (string letter in new[] { "A", "B" })
            {
                var robot = Find("Robot_" + letter + "_Base");
                var root = robot.transform.parent;
                Require(root.name == "Robot_" + letter + "_RobotRoot", "Unexpected robot hierarchy.");
                if (!CanGroundRobotRoot(root, out string reason))
                    deferred.Add(root.name + ": " + reason);
                else
                {
                    float gap = WorldVertices(robot).Min(v => v.y);
                    Require(Mathf.Abs(gap) < .0001f || Mathf.Abs(gap - .02f) < .0001f, "Robot base height differs from audit.");
                    if (Mathf.Abs(gap) > .0001f) moves.Add((root, Vector3.down * gap));
                }
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
            Directory.CreateDirectory(EvidenceRoot);
            string evidence = EvidenceRoot + "/grounding-" + stamp;
            File.WriteAllBytes(evidence + "-scene-before.unity", originalScene);
            File.WriteAllText(evidence + "-before.json", JsonConvert.SerializeObject(new { scene = scene.path, scene.isDirty, meshes = before, deferred }, Formatting.Indented));
            if (!AssetDatabase.IsValidFolder(MeshRoot)) AssetDatabase.CreateFolder("Assets/VRMaintenanceResearch/ResearchFlow", "Meshes");
            foreach (var plan in meshPlans)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(plan.path);
                if (existing != null)
                {
                    Require(existing.vertices.SequenceEqual(plan.mesh.vertices) && existing.triangles.SequenceEqual(plan.mesh.triangles) && existing.uv.SequenceEqual(plan.mesh.uv),
                        "Existing owned mesh differs; preserve it: " + plan.path);
                    UnityEngine.Object.DestroyImmediate(plan.mesh);
                }
                else
                {
                    Require(!File.Exists(plan.path), "Unexpected asset occupies " + plan.path);
                    AssetDatabase.CreateAsset(plan.mesh, plan.path);
                    AssetDatabase.SaveAssetIfDirty(plan.mesh);
                    existing = plan.mesh;
                }
                Undo.RecordObject(plan.target, "Ground audited furniture / map floor UV0");
                plan.target.sharedMesh = existing;
                EditorUtility.SetDirty(plan.target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(plan.target);
                changes.Add(plan.target.name + ": owned mesh " + plan.path);
            }
            foreach (var move in moves)
            {
                Undo.RecordObject(move.target, "Ground audited furniture");
                move.target.position += move.delta;
                EditorUtility.SetDirty(move.target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(move.target);
                changes.Add(move.target.name + ": world delta " + move.delta.ToString("F7"));
            }
            var cube = Find("Training_NeutralCube").gameObject;
            if (cube.activeSelf)
            {
                Undo.RecordObject(cube, "Hide obsolete participant helper block");
                cube.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cube);
                changes.Add("Training_NeutralCube: hidden in participant integration scene");
            }
            Physics.SyncTransforms();
            Require(colliderBefore.SequenceEqual(environment.GetComponentsInChildren<Collider>(true).Select(c => c.GetInstanceID() + ":" + EditorJsonUtility.ToJson(c))), "Collider configuration changed; scene not saved.");
            foreach (string name in Benches.Concat(Stools)) Require(Mathf.Abs(WorldVertices(Find(name)).Min(v => v.y)) < .0001f, name + " actual feet did not reach floor.");
            foreach (string name in Floors) Require(ValidFloorUV(Find(name)), name + " planar UV0 validation failed.");
            Require(File.ReadAllBytes(ScenePath).SequenceEqual(originalScene), "Scene changed externally; do not overwrite.");
            var after = Snapshot(tracked);
            // Capture dirty state and exact geometry before the only scene save.
            EditorSceneManager.MarkSceneDirty(scene);
            File.WriteAllText(evidence + "-before-save.json", JsonConvert.SerializeObject(new { scene = scene.path, scene.isDirty, before, after, changes, deferred, hardware_verified = false }, Formatting.Indented));
            Undo.FlushUndoRecordObjects();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Grounding scene save failed.");
            File.WriteAllText(evidence + "-after.json", JsonConvert.SerializeObject(new { scene = scene.path, scene.isDirty, before, after, changes, deferred, collider_configuration_unchanged = true, hardware_verified = false }, Formatting.Indented));
            return evidence + "-after.json";
        }

        private static bool CanGroundRobotRoot(Transform root, out string reason)
        {
            reason = null;
            if (root.GetComponentsInParent<Animation>(true).Length > 0 || root.GetComponentsInChildren<Animation>(true).Length > 0)
            { reason = "Legacy Animation requires separate binding review."; return false; }
            foreach (var animator in root.GetComponentsInParent<Animator>(true).Concat(root.GetComponentsInChildren<Animator>(true)).Distinct())
            {
                if (animator.applyRootMotion || animator.isHuman)
                { reason = animator.name + ": root motion or humanoid animation requires separate review."; return false; }
                // A child Animator cannot bind this root or its ancestors.
                if (animator.transform != root && !root.IsChildOf(animator.transform)) continue;
                string rootPath = AnimationUtility.CalculateTransformPath(root, animator.transform);
                var controller = animator.runtimeAnimatorController;
                if (controller == null) continue;
                foreach (var clip in controller.animationClips.Distinct())
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        if (typeof(Transform).IsAssignableFrom(binding.type) && BindingAffectsRootPath(binding.path, rootPath))
                        { reason = clip.name + ": ancestor/root transform binding " + binding.path + "/" + binding.propertyName; return false; }
            }
            return true;
        }

        private static bool BindingAffectsRootPath(string bindingPath, string rootPath) =>
            bindingPath.Length == 0 || bindingPath == rootPath || rootPath.StartsWith(bindingPath + "/", StringComparison.Ordinal);

        public static string CheckAnimationBindingPaths()
        {
            const string root = "Factory/Robot_A_RobotRoot";
            Require(BindingAffectsRootPath("", root), "Animator transform must block root grounding.");
            Require(BindingAffectsRootPath("Factory", root), "Ancestor transform must block root grounding.");
            Require(BindingAffectsRootPath(root, root), "Exact root transform must block root grounding.");
            Require(!BindingAffectsRootPath(root + "/Robot_A_Joint_1", root), "Joint descendants must not block root grounding.");
            Require(!BindingAffectsRootPath("Factory/Robot_B_RobotRoot", root), "Sibling robot is not an ancestor.");
            Require(!BindingAffectsRootPath("Factory/Robot_A", root), "Partial name is not an ancestor.");
            return "ANIMATION_BINDING_PATH_PASS checks=6";
        }

        private static void PlanMove(MeshFilter mf, float expectedGap, List<(Transform target, Vector3 delta)> moves)
        {
            float gap = WorldVertices(mf).Min(v => v.y);
            if (Mathf.Abs(gap) < .0001f) return;
            Require(Mathf.Abs(gap - expectedGap) < .0001f, mf.name + " actual vertex height differs from audit.");
            Require(mf.GetComponentsInChildren<Collider>(true).Length == 0, mf.name + " gained collision components; inspect before moving.");
            moves.Add((mf.transform, Vector3.down * gap));
        }

        private static Vector3[] GroundPads(MeshFilter mf)
        {
            var vertices = mf.sharedMesh.vertices;
            var world = WorldVertices(mf);
            var parent = Enumerable.Range(0, vertices.Length).ToArray();
            int Root(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            void Join(int a, int b) { parent[Root(a)] = Root(b); }
            var welded = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < world.Length; i++)
            {
                var p = world[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 100000), Mathf.RoundToInt(p.y * 100000), Mathf.RoundToInt(p.z * 100000));
                if (welded.TryGetValue(key, out int first)) Join(i, first); else welded.Add(key, i);
            }
            var triangles = mf.sharedMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3) { Join(triangles[i], triangles[i + 1]); Join(triangles[i], triangles[i + 2]); }
            var pads = Enumerable.Range(0, vertices.Length).GroupBy(Root).Where(g => g.Min(i => world[i].y) < .006f && g.Max(i => world[i].y) < .046f).Select(g => g.ToArray()).ToArray();
            Require(pads.Length == 4, mf.name + " expected four disconnected audited foot pads.");
            foreach (var pad in pads)
            {
                float gap = pad.Min(i => world[i].y);
                Require(Mathf.Abs(gap - .005f) < .0001f, mf.name + " pad differs from measured 5 mm gap.");
                foreach (int index in pad) vertices[index] = mf.transform.InverseTransformPoint(world[index] - Vector3.up * gap);
            }
            return vertices;
        }

        private static bool ValidFloorUV(MeshFilter mf)
        {
            var uv = mf.sharedMesh.uv;
            var world = WorldVertices(mf);
            return uv.Length == world.Length && uv.Distinct().Count() > 3 && uv.Select((v, i) => Vector2.Distance(v, new Vector2(world[i].x, world[i].z)) < .0001f).All(v => v);
        }
        private static Vector3[] WorldVertices(MeshFilter mf) => mf.sharedMesh.vertices.Select(mf.transform.TransformPoint).ToArray();
        private static object[] Snapshot(IEnumerable<MeshFilter> meshes) => meshes.Select(m =>
        {
            var v = WorldVertices(m);
            return (object)new { m.name, position = new[] { m.transform.position.x, m.transform.position.y, m.transform.position.z }, mesh = AssetDatabase.GetAssetPath(m.sharedMesh), min_y = v.Min(p => p.y), max_y = v.Max(p => p.y), renderer_min_y = m.GetComponent<Renderer>().bounds.min.y, vertices = v.Length, uv0 = m.sharedMesh.uv.Length, collider_bounds = m.GetComponents<Collider>().Select(c => new { min_y = c.bounds.min.y, max_y = c.bounds.max.y }).ToArray() };
        }).ToArray();
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
#endif
