#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Oculus.Interaction.Editor.QuickActions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchRoomTeleport
    {
        public static string ApplyOpenDoorways()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ResearchFlowSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Stable clean ResearchFlow Edit Mode required before opening doors.");
            var environment = scene.GetRootGameObjects().Single(g => g.name == "Environment_V2");
            var rooms = new[] { "Training", "Computer", "Fan" };
            var hinges = new[] { new Vector3(13.62f, .015f, .28f), new Vector3(4.42f, .015f, .28f), new Vector3(-5.78f, .015f, .28f) };
            var leaves = rooms.Select(room => environment.transform.Find(room + "/" + room + "_Door_Leaf")).ToArray();
            var open = Quaternion.Euler(0, 90, 0);
            for (int i = 0; i < leaves.Length; i++)
                if (!leaves[i] || Vector3.Distance(leaves[i].position, hinges[i]) > .0001f ||
                    (Quaternion.Angle(leaves[i].rotation, Quaternion.identity) > .01f && Quaternion.Angle(leaves[i].rotation, open) > .01f))
                    throw new InvalidOperationException(rooms[i] + " doorway differs from audited hinge/rotation; inspect before changing.");
            var before = leaves.Select(t => new { t.name, position = t.position.ToString("F7"), rotation = t.eulerAngles.ToString("F3"), colliders = t.GetComponentsInChildren<Collider>(true).Length }).ToArray();
            var sceneBytes = File.ReadAllBytes(scene.path);
            string evidence = "Logs/QuestFinalIntegration/open-doorways-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
            Directory.CreateDirectory("Logs/QuestFinalIntegration");
            File.WriteAllBytes(evidence + "-scene-before.unity", sceneBytes);
            File.WriteAllText(evidence + "-before.json", JsonConvert.SerializeObject(new { scene = scene.path, scene.isDirty, before }, Formatting.Indented));
            int changed = 0;
            foreach (var leaf in leaves)
            {
                if (Quaternion.Angle(leaf.rotation, open) < .01f) continue;
                Undo.RecordObject(leaf, "Open neutral participant doorway");
                leaf.rotation = open;
                PrefabUtility.RecordPrefabInstancePropertyModifications(leaf);
                EditorUtility.SetDirty(leaf);
                changed++;
            }
            Physics.SyncTransforms();
            var verification = AuditDoorwayAccess(scene);
            if (!File.ReadAllBytes(scene.path).SequenceEqual(sceneBytes)) throw new IOException("Scene changed externally; preserve it before saving door changes.");
            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                File.WriteAllText(evidence + "-before-save.json", JsonConvert.SerializeObject(new { scene = scene.path, scene.isDirty, before, changed, verification, hardware_verified = false }, Formatting.Indented));
                Undo.FlushUndoRecordObjects();
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Doorway scene save failed.");
            }
            File.WriteAllText(evidence + "-after.json", JsonConvert.SerializeObject(new { scene = scene.path, scene.isDirty, before, changed, verification, hardware_verified = false }, Formatting.Indented));
            return evidence + "-after.json";
        }

        public static string VerifyOpenDoorwayAccess() => JsonConvert.SerializeObject(AuditDoorwayAccess(SceneManager.GetActiveScene()), Formatting.Indented);

        private static object AuditDoorwayAccess(Scene scene)
        {
            if (scene.path != ResearchFlowSceneBuilder.ScenePath) throw new InvalidOperationException("ResearchFlow scene required.");
            var environment = scene.GetRootGameObjects().Single(g => g.name == "Environment_V2");
            var controller = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ResearchSessionFlowController>(true)).Single();
            var neutral = controller.transform.Find("NeutralRoomDestinations");
            if (!neutral || neutral.childCount != 27) throw new InvalidOperationException("Expected existing 27 neutral destinations.");
            var destinations = controller.GetComponentsInChildren<Transform>(true).Where(t => t.parent == neutral || t.name.StartsWith("ResearchFlow_Station_", StringComparison.Ordinal)).ToArray();
            if (destinations.Length != 30) throw new InvalidOperationException("Expected all 30 existing teleport destinations.");
            var floors = environment.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy && c.name.EndsWith("_Floor", StringComparison.Ordinal)).ToArray();
            var obstacles = environment.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy && r.bounds.size.x < 4 && r.bounds.size.z < 4 && r.bounds.max.y > .1f).ToArray();
            var pointChecks = new List<object>();
            Physics.SyncTransforms();
            foreach (var destination in destinations)
            {
                var point = destination.position;
                bool floor = floors.Any(f => f.Raycast(new Ray(point + Vector3.up, Vector3.down), out var hit, 1.1f) && Mathf.Abs(hit.point.y - point.y) < .001f);
                var clearance = new Bounds(point + Vector3.up * 1.1f, new Vector3(.8f, 2.1f, .8f));
                var furniture = obstacles.Where(r => clearance.Intersects(r.bounds)).Select(r => r.name).ToArray();
                var solid = Physics.OverlapCapsule(point + Vector3.up * .4f, point + Vector3.up * 1.75f, .4f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(c => c.transform.IsChildOf(environment.transform) && !floors.Contains(c)).Select(c => c.name).Distinct().ToArray();
                if (!floor || furniture.Length > 0 || solid.Length > 0)
                    throw new InvalidOperationException(destination.name + " clearance failed: floor=" + floor + "; renderers=" + string.Join(",", furniture) + "; colliders=" + string.Join(",", solid));
                pointChecks.Add(new { destination.name, point = new[] { point.x, point.y, point.z }, floor, furniture_clearance = true, collider_clearance = true });
            }
            var rays = new List<object>();
            foreach (float x in new[] { 13f, 3.8f, -6.6f })
                foreach (bool outward in new[] { true, false })
                {
                    var from = new Vector3(x, 1.2f, outward ? 1f : -1.7f);
                    var to = new Vector3(x, .01f, outward ? -1.7f : 1f);
                    var delta = to - from;
                    var hits = Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)
                        .Where(h => h.collider.transform.IsChildOf(environment.transform)).Select(h => h.collider.name).Distinct().ToArray();
                    if (hits.Length > 0) throw new InvalidOperationException("Doorway ray x=" + x + " outward=" + outward + " blocked by " + string.Join(",", hits));
                    rays.Add(new { x, outward, from = new[] { from.x, from.y, from.z }, to = new[] { to.x, to.y, to.z }, unobstructed = true });
                }
            return new { destinations = pointChecks, doorway_rays = rays, checks = pointChecks.Count + rays.Count, scope = "Editor clearance and six straight controller-height rays; native curved teleport commit and physical Quest remain separate.", hardware_verified = false };
        }

        public static string Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ResearchFlowSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty)) throw new InvalidOperationException("Stable clean ResearchFlow Edit Mode required");
            var controller = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ResearchSessionFlowController>(true)).Single();
            if (controller.transform.Find("NeutralRoomDestinations")) throw new InvalidOperationException("Destinations already exist; inspect before changing");
            var points = new[] {
                new Vector3(7.1f,0,4), new Vector3(12,0,4), new Vector3(9.7f,0,2.3f), new Vector3(9.7f,0,6.5f), new Vector3(12.5f,0,6.5f), new Vector3(7,0,6.5f), new Vector3(13,0,1),
                new Vector3(-1.9f,0,4.5f), new Vector3(1.9f,0,4.8f), new Vector3(0,0,2.8f), new Vector3(0,0,7.5f), new Vector3(-3.4f,0,5.6f), new Vector3(3.4f,0,7.2f), new Vector3(3.8f,0,1),
                new Vector3(-11.6f,0,4), new Vector3(-7.8f,0,4), new Vector3(-9.7f,0,2.2f), new Vector3(-9.7f,0,6.7f), new Vector3(-6.8f,0,6.7f), new Vector3(-12.5f,0,6.7f), new Vector3(-6.6f,0,1),
                new Vector3(13,0,-1.7f), new Vector3(7,0,-1.7f), new Vector3(3.8f,0,-1.7f), new Vector3(-1.5f,0,-1.7f), new Vector3(-6.6f,0,-1.7f), new Vector3(-12,0,-1.7f)
            };
            var environment = scene.GetRootGameObjects().Single(g => g.name == "Environment_V2");
            var floors = environment.GetComponentsInChildren<Collider>(true).Where(c => c.name.EndsWith("_Floor")).ToArray();
            var obstacles = environment.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.gameObject.activeInHierarchy && r.bounds.size.x < 4 && r.bounds.size.z < 4 && r.bounds.max.y > .1f).ToArray();
            foreach (var point in points)
            {
                if (!floors.Any(f => f.Raycast(new Ray(point + Vector3.up, Vector3.down), out var hit, 1.1f) && Mathf.Abs(hit.point.y - point.y) < .001f)) throw new InvalidOperationException("Missing floor at " + point);
                var clearance = new Bounds(point + Vector3.up * 1.1f, new Vector3(.8f,2.1f,.8f));
                if (obstacles.Any(r => clearance.Intersects(r.bounds))) throw new InvalidOperationException("Furniture clearance at " + point + ": " + string.Join(",", obstacles.Where(r => clearance.Intersects(r.bounds)).Select(r => r.name)));
            }
            var root = new GameObject("NeutralRoomDestinations");
            Undo.RegisterCreatedObjectUndo(root, "Neutral native teleport destinations");
            root.transform.SetParent(controller.transform, false);
            for (int i = 0; i < points.Length; i++)
            {
                var target = new GameObject("Destination_" + (i + 1).ToString("D2"));
                target.transform.SetParent(root.transform, false);
                target.transform.position = points[i];
                QuickActionsAPI.AddTeleportInteraction(target, TeleportSurfaceType.Hotspot, TeleportHotspotSnap.SnapPosition);
                // Lift only the visual ring, never the native destination/origin.
                foreach (var renderer in target.GetComponentsInChildren<Renderer>(true)) renderer.transform.position += Vector3.up * .003f;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            File.WriteAllText("Logs/QuestFinalIntegration/teleport-destinations.json", JsonConvert.SerializeObject(new { points = points.Select(p => new[] {p.x,p.y,p.z}), clearance_radius=.4f, head_clearance=2.15f, snap_rotation=false, hardware_verified=false }, Formatting.Indented));
            Undo.FlushUndoRecordObjects();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed");
            return "NEUTRAL_TELEPORT_DESTINATIONS=" + points.Length;
        }
    }
}
#endif
