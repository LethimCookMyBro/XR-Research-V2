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

        public static string AdjustStationPositions()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ResearchFlowSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Stable clean ResearchFlow Edit Mode required.");
            var controller = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ResearchSessionFlowController>(true)).Single();
            var computer = controller.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ResearchFlow_Station_Computer");
            var fan = controller.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ResearchFlow_Station_Fan");
            if (!computer || !fan) throw new InvalidOperationException("Stations not found in scene.");
            Undo.RecordObject(computer, "Adjust Computer station clearance");
            computer.position = new Vector3(0, 0, 5.60f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(computer);
            EditorUtility.SetDirty(computer);

            Undo.RecordObject(fan, "Adjust Fan station clearance");
            fan.position = new Vector3(-9.70f, 0, 5.15f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(fan);
            EditorUtility.SetDirty(fan);

            Physics.SyncTransforms();
            var audit = AuditDoorwayAccess(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
            return JsonConvert.SerializeObject(audit, Formatting.Indented);
        }

        // Inspection only: measure authored marker visual and interactive footprints
        // separately from the player's landing clearance; never scale collider/teleport range to fit an image.
        public static string DescribeTeleportMarkers()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ResearchFlowSceneBuilder.ScenePath) throw new InvalidOperationException("Canonical ResearchFlow scene required");
            var controller = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ResearchSessionFlowController>(true)).Single();
            var targets = controller.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Destination_", StringComparison.Ordinal)
                    || t.name.StartsWith("ResearchFlow_Station_", StringComparison.Ordinal))
                .Where(t => t.GetComponentInChildren<Oculus.Interaction.Locomotion.TeleportInteractable>(true)).ToArray();
            return JsonConvert.SerializeObject(new { count = targets.Length,
                targets = targets.Select(t => new {
                    name = t.name,
                    position = new[] { t.position.x, t.position.y, t.position.z },
                    markers = t.GetComponentsInChildren<Renderer>(true).Select(r => new {
                        name = r.name, active = r.gameObject.activeInHierarchy, enabled = r.enabled,
                        center = new[] { r.bounds.center.x, r.bounds.center.y, r.bounds.center.z },
                        bounds_m = new[] { r.bounds.size.x, r.bounds.size.y, r.bounds.size.z }
                    }).ToArray(),
                    colliders = t.GetComponentsInChildren<Collider>(true).Select(c => new {
                        name = c.name, enabled = c.enabled, trigger = c.isTrigger,
                        size = new[] { c.bounds.size.x, c.bounds.size.y, c.bounds.size.z }
                    }).ToArray()
                }).ToArray(), hardware_verified = false }, Formatting.None);
        }

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

        public static string DescribeUnsavedSceneObjects()
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var components = roots.SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .Where(c => c && EditorUtility.IsDirty(c))
                .Select(c => c.GetType().Name + ":" + c.gameObject.name)
                .ToArray();
            return JsonConvert.SerializeObject(new { scene = scene.path, scene.isDirty,
                dirty_component_count = components.Length, components = components.Take(90).ToArray(),
                any_non_ring = components.Any(n => !n.EndsWith(":Ring", StringComparison.Ordinal)) },
                Formatting.Indented);
        }

        // The localization/preview captures sometimes dirty serialized TMP mesh caches in
        // the open scene after our ring-only SaveScene. Never save those generated glyph
        // caches into the canonical scene. Reload only when the exact known cache types
        // are dirty and the already saved 30 ring visual changes are present on disk.
        public static string ReloadSavedSceneAfterPreviewCaches()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ResearchFlowSceneBuilder.ScenePath
                || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
                || SceneManager.sceneCount != 1) throw new InvalidOperationException("Canonical stopped single scene required");
            if (!scene.isDirty) return "SCENE_ALREADY_CLEAN";
            var dirty = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Component>(true))
                .Where(c => c && EditorUtility.IsDirty(c)).ToArray();
            bool Generated(Component c) => c is TMPro.TMP_Text || c is RectTransform &&
                (c.name == "Label" || c.name == "Handle") ||
                c is MeshFilter && (c.name.EndsWith("_label", StringComparison.Ordinal) ||
                    c.name == "Permanent neutral lab identification") ||
                c is MeshRenderer && (c.name == "OculusCursor" || c.name == "Mesh" ||
                    c.name == "Permanent neutral lab identification" || c.name.EndsWith("_label", StringComparison.Ordinal));
            if (dirty.Length == 0 || dirty.Any(c => !Generated(c)))
                throw new InvalidOperationException("Unexpected authored component dirty; refuse to discard unsaved changes.");
            var targets = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name.StartsWith("Destination_", StringComparison.Ordinal) ||
                    t.name.StartsWith("ResearchFlow_Station_", StringComparison.Ordinal))
                .Where(t => t.GetComponentInChildren<Oculus.Interaction.Locomotion.TeleportInteractable>(true)).ToArray();
            if (targets.Length != 30 || targets.Any(t => Mathf.Abs(
                Mathf.Max(t.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "Ring").bounds.size.x,
                  t.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "Ring").bounds.size.z) - .78f) > .015f))
                throw new InvalidOperationException("Ring visual changes not verified; refuse reload.");
            string scenePath = scene.path;
            byte[] before = File.ReadAllBytes(scenePath);
            string report = Path.GetFullPath("Logs/QuestFinalIntegration/20261022_preview_dirty_objects.json");
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            if (!File.Exists(report))
                File.WriteAllText(report, JsonConvert.SerializeObject(new {
                    dirty_types = dirty.Select(c => c.GetType().Name + ":" + c.name).ToArray(),
                    serialized_scene_bytes = before.Length, discarded_only_generated_preview_cache = true
                }, Formatting.Indented));
            var reopened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (reopened.isDirty || !before.SequenceEqual(File.ReadAllBytes(scenePath)))
                throw new IOException("Canonical scene did not reload cleanly and byte-identically.");
            return "RELOADED_SAVED_SCENE_ONLY_PREVIEW_CACHE_DISCARDED count=" + dirty.Length;
        }

        // Only the visible floor ring is enlarged. The actual teleport target, native SDK
        // trigger collider, player landing point, rotation and movement logs never change.
        // 0.78 m visual diameter stays inside the existing 1.0 m trigger footprint.
        public static string EnlargeTeleportMarkerVisuals()
        {
            const float targetDiameterMetres = .78f;
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ResearchFlowSceneBuilder.ScenePath
                || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || EditorApplication.isUpdating || Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Canonical clean stopped Editor scene required for ring-only change.");
            var controller = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ResearchSessionFlowController>(true)).Single();
            var targets = controller.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Destination_", StringComparison.Ordinal)
                    || t.name.StartsWith("ResearchFlow_Station_", StringComparison.Ordinal))
                .Where(t => t.GetComponentInChildren<Oculus.Interaction.Locomotion.TeleportInteractable>(true)).ToArray();
            if (targets.Length != 30) throw new InvalidOperationException("Unexpected teleport target count: " + targets.Length);
            var prepared = targets.Select(t => {
                var ring = t.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "Ring");
                var collider = t.GetComponentsInChildren<Collider>(true).Single(c => c.name == "Teleport Collider");
                if (ring.GetComponentsInChildren<Collider>(true).Length != 0 || !collider.isTrigger || !collider.enabled)
                    throw new InvalidOperationException(t.name + " ring and native trigger ownership changed.");
                return new { target = t, ring, collider, beforePosition = t.position,
                    beforeColliderCenter = collider.bounds.center, beforeColliderSize = collider.bounds.size,
                    beforeDiameter = Mathf.Max(ring.bounds.size.x, ring.bounds.size.z) };
            }).ToArray();
            foreach (var item in prepared)
                if (item.beforeDiameter < .4f || item.beforeDiameter > .9f)
                    throw new InvalidOperationException(item.target.name + " unexpected authored ring diameter " + item.beforeDiameter);
            if (prepared.All(i => Mathf.Abs(i.beforeDiameter - targetDiameterMetres) < .003f))
                return JsonConvert.SerializeObject(new { modified = 0, count = targets.Length,
                    visual_diameter = prepared[0].beforeDiameter, collider_footprint_unchanged = true,
                    landing_positions_unchanged = true, study_design_review_required = true,
                    hardware_verified = false }, Formatting.Indented);
            byte[] sceneBefore = File.ReadAllBytes(scene.path);
            foreach (var item in prepared)
            {
                if (Mathf.Abs(item.beforeDiameter - targetDiameterMetres) < .003f) continue;
                Undo.RecordObject(item.ring.transform, "Enlarge floor teleport ring visual only");
                item.ring.transform.localScale *= targetDiameterMetres / item.beforeDiameter;
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.ring.transform);
                EditorUtility.SetDirty(item.ring.transform);
            }
            Physics.SyncTransforms();
            foreach (var item in prepared)
                if (Vector3.Distance(item.beforePosition, item.target.position) > .0001f
                    || Vector3.Distance(item.beforeColliderCenter, item.collider.bounds.center) > .0001f
                    || Vector3.Distance(item.beforeColliderSize, item.collider.bounds.size) > .0001f
                    || Mathf.Abs(Mathf.Max(item.ring.bounds.size.x, item.ring.bounds.size.z) - targetDiameterMetres) > .012f)
                    throw new InvalidOperationException(item.target.name + " visual-only proof failed before save.");
            if (!sceneBefore.SequenceEqual(File.ReadAllBytes(scene.path)))
                throw new IOException("Scene changed on disk concurrently; refuse to overwrite.");
            string backup = Path.GetFullPath("Logs/QuestFinalIntegration/teleport_marker_visual_before_20261022.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (File.Exists(backup)) throw new IOException("Teleport marker snapshot already exists: " + backup);
            File.WriteAllBytes(backup, sceneBefore);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Teleport visual scene save failed.");
            return JsonConvert.SerializeObject(new { backup, modified = prepared.Count(i => Mathf.Abs(i.beforeDiameter - targetDiameterMetres) > .003f),
                count = targets.Length, visual_diameter_before = prepared[0].beforeDiameter,
                visual_diameter_after = Mathf.Max(prepared[0].ring.bounds.size.x, prepared[0].ring.bounds.size.z),
                collider_footprint_unchanged = true, landing_positions_unchanged = true,
                scene = scene.path, study_design_review_required = true, hardware_verified = false }, Formatting.Indented);
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
