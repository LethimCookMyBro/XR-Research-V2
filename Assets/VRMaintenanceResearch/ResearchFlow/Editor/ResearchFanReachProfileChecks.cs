#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchFanReachProfileChecks
    {
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();
        static string State(ResearchS1TaskBMechanics fan)
        {
            var state = JObject.Parse(JsonUtility.ToJson(fan.Capture())); state.Remove("capturedAtSeconds");
            return state.ToString(Formatting.None);
        }
        static Transform Node(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        public static string Run(string directory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !Path.IsPathRooted(directory) || Directory.Exists(directory))
                throw new InvalidOperationException("Stopped Editor and fresh absolute output path required.");
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
            if (!donor) throw new InvalidOperationException("Canonical fan donor missing.");
            var beforeScenes = Scenes(); var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var rows = new List<object>(); var errors = new List<string>();
            void Check(string id, bool ok, object measured = null)
            { rows.Add(new { id, ok, measured }); if (!ok) errors.Add(id); }
            try
            {
                var go = UnityEngine.Object.Instantiate(donor.gameObject); SceneManager.MoveGameObjectToScene(go, scene);
                foreach (var component in go.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled = false;
                foreach (var pointable in go.GetComponentsInChildren<Oculus.Interaction.PointableElement>(true))
                {
                    var type = typeof(Oculus.Interaction.PointableElement);
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    type.GetField("_points", flags).SetValue(pointable, new List<Pose>());
                    type.GetField("_pointIds", flags).SetValue(pointable, new List<int>());
                    type.GetField("_selectingPoints", flags).SetValue(pointable, new List<Pose>());
                    type.GetField("_selectingPointIds", flags).SetValue(pointable, new List<int>());
                }
                foreach (var body in go.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
                var fan = go.GetComponent<ResearchS1TaskBMechanics>();
                go.transform.position += new Vector3(120, 0, 120); Physics.SyncTransforms();
                var parts = new[] { fan.frontGuard, fan.blade, fan.spinner, fan.rearGuard, fan.rearLock };
                var partPositions = parts.Select(p => p.transform.position).ToArray();
                var scales = parts.Select(p => p.transform.lossyScale).ToArray();
                var poses = parts.Select(p => p.Pose).ToArray();
                var basis = Node(go.transform, "COL_FAN_PED_BASE_Body").GetComponent<Collider>();
                var stand = Node(go.transform, "COL_FAN_PED_STAND_Tube").GetComponent<Collider>();
                var pipe = Node(go.transform, "COL_FAN_PED_HEAD_Pipe").GetComponent<Collider>();
                var footBefore = basis.bounds; var standBefore = stand.bounds; var pipeBefore = pipe.bounds;
                var original = State(fan); var fixedPositions = fan.fixedAssembly.Select(t => t.localPosition).ToArray();
                var profile = new ResearchS1FanReachProfile(fan);
                Check("constructor_does_not_move", State(fan) == original && fan.spinner.transform.position == partPositions[2]);
                profile.SetLowered(true);
                Check("head_lowered_300mm", Mathf.Abs(fan.spinner.transform.position.y - partPositions[2].y + .30f) < .0001f,
                    new { old_y = partPositions[2].y, new_y = fan.spinner.transform.position.y });
                Check("base_supported_at_original_height", Vector3.Distance(basis.bounds.center, footBefore.center) < .0001f && Vector3.Distance(basis.bounds.size, footBefore.size) < .0001f);
                Check("stand_bottom_unchanged", Mathf.Abs(stand.bounds.min.y - standBefore.min.y) < .0001f);
                Check("pipe_bottom_unchanged", Mathf.Abs(pipe.bounds.min.y - pipeBefore.min.y) < .0001f);
                Check("stand_shortened_300mm", Mathf.Abs(stand.bounds.size.y - standBefore.size.y + .30f) < .0001f);
                Check("pipe_shortened_300mm", Mathf.Abs(pipe.bounds.size.y - pipeBefore.size.y + .30f) < .0001f);
                Check("fixed_roots_unchanged", fan.fixedAssembly.Select((t, i) => Vector3.Distance(t.localPosition, fixedPositions[i]) < .0001f).All(x => x));
                Check("task_snapshot_unchanged", State(fan) == original);
                for (int i = 0; i < parts.Length; i++)
                {
                    var now = parts[i].Pose; var prior = poses[i];
                    Check(parts[i].stableId + ":mate_preserved", Mathf.Abs(now.depthMm - prior.depthMm) < .05f && Mathf.Abs(now.lateralMm - prior.lateralMm) < .05f && Mathf.Abs(now.tiltDeg - prior.tiltDeg) < .05f && Mathf.Abs(Mathf.DeltaAngle(now.turnDeg, prior.turnDeg)) < .05f);
                    Check(parts[i].stableId + ":size_preserved", Vector3.Distance(parts[i].transform.lossyScale, scales[i]) < .00001f);
                }
                var once = parts.Select(p => p.transform.position).ToArray(); profile.SetLowered(true);
                Check("lowering_idempotent", parts.Select((p, i) => p.transform.position == once[i]).All(x => x));
                profile.SetLowered(false);
                Check("rollback_positions", parts.Select((p, i) => Vector3.Distance(p.transform.position, partPositions[i]) < .0001f).All(x => x));
                Check("rollback_snapshot", State(fan) == original);
                Check("rollback_tubes", Vector3.Distance(stand.bounds.size, standBefore.size) < .0001f && Vector3.Distance(pipe.bounds.size, pipeBefore.size) < .0001f);
                profile.SetLowered(true); profile.SetLowered(false);
                Check("repeat_cycle_restores", State(fan) == original && parts.Select((p, i) => Vector3.Distance(p.transform.position, partPositions[i]) < .0001f).All(x => x));
                UnityEngine.Object.DestroyImmediate(go);
            }
            catch (Exception error) { errors.Add(error.ToString()); }
            finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(active); }
            Check("original_scenes_preserved", beforeScenes.SequenceEqual(Scenes()));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonConvert.SerializeObject(new { status = errors.Count == 0 ? "PASS" : "ISSUES", evidence = "EDITOR_CLONE_GEOMETRY_NOT_WEARER_COMFORT", rows, errors }, Formatting.Indented));
            return "FAN_REACH_PROFILE " + (errors.Count == 0 ? "PASS" : "ISSUES") + " checks=" + rows.Count + " errors=" + errors.Count;
        }
    }
}
#endif
