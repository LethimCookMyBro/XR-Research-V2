#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchCarryConstraintChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Vector3 Far = new Vector3(120, 20, 120);
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();
        static void Set(object value, string field, object data) => value.GetType().GetField(field, Private).SetValue(value, data);
        static T Get<T>(object value, string field) => (T)value.GetType().GetField(field, Private).GetValue(value);
        static object Call(object value, string name, params object[] data) => value.GetType().GetMethod(name, Private).Invoke(value, data);
        static Collider[] Solids(GameObject go) => go.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy).ToArray();
        static Matrix4x4 Shift(Transform root, Vector3 delta) => Matrix4x4.TRS(root.position + delta, root.rotation, root.lossyScale);
        static void Pose(Transform root, Matrix4x4 pose)
        {
            root.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            var body = root.GetComponent<Rigidbody>();
            if (body) { body.position = root.position; body.rotation = root.rotation; }
            Physics.SyncTransforms();
        }
        static void Freeze(GameObject go)
        {
            go.SetActive(true);
            foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            foreach (var b in go.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; b.useGravity = false; }
        }
        public static string Run(string directory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stopped Editor required.");
            if (!Path.IsPathRooted(directory) || Directory.Exists(directory)) throw new InvalidOperationException("Fresh absolute evidence directory required.");
            var before = Scenes();
            var active = SceneManager.GetActiveScene();
            var a = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            var b = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
            if (!a || !b) throw new InvalidOperationException("Canonical A and B donors required.");
            var rows = new List<object>();
            var issues = new List<string>();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            bool restored = false;
            void Check(string id, bool actual, bool expected, object details = null)
            {
                rows.Add(new { id, actual, expected, passed = actual == expected, details });
                if (actual != expected) issues.Add(id);
            }
            try
            {
                Check("production_physics_query_scene", scene.GetPhysicsScene() == Physics.defaultPhysicsScene, true);
                var donors = new[] { b.frontGuard.gameObject, b.blade.gameObject, b.spinner.gameObject, a.ram[0].gameObject, a.sleds[0].gameObject };
                foreach (var donor in donors)
                {
                    var clone = UnityEngine.Object.Instantiate(donor);
                    SceneManager.MoveGameObjectToScene(clone, scene);
                    Freeze(clone);
                    clone.transform.localScale = donor.transform.lossyScale;
                    clone.transform.SetPositionAndRotation(Far, Quaternion.identity);
                    Physics.SyncTransforms();
                    var solids = Solids(clone);
                    if (solids.Length == 0) throw new InvalidOperationException("No active donor collider " + donor.name);
                    var origin = clone.transform.localToWorldMatrix;
                    Check(donor.name + ":clear_20mm", ResearchS1HeldCollision.Allows(clone.transform, Shift(clone.transform, Vector3.right * .02f)), true);
                    Check(donor.name + ":clear_129mm", ResearchS1HeldCollision.Allows(clone.transform, Shift(clone.transform, Vector3.right * .129f)), true,
                        new { sample_count = ResearchS1HeldCollision.Samples(.129f, 0, 0), source = "actual donor clone, no nearby foreign collider" });
                    Check(donor.name + ":clear_300mm", ResearchS1HeldCollision.Allows(clone.transform, Shift(clone.transform, Vector3.right * .3f)), true);
                    var floor = new GameObject("REPRO_FLOOR");
                    SceneManager.MoveGameObjectToScene(floor, scene);
                    floor.transform.position = new Vector3(Far.x, solids.Min(c => c.bounds.min.y) - .05f + .0002f, Far.z);
                    var floorBox = floor.AddComponent<BoxCollider>(); floorBox.size = new Vector3(4, .1f, 4);
                    Physics.SyncTransforms();
                    Check(donor.name + ":floor_50mm_lift", ResearchS1HeldCollision.Allows(clone.transform, Shift(clone.transform, Vector3.up * .05f)), true,
                        new { minY = solids.Min(c => c.bounds.min.y), floorTop = floorBox.bounds.max.y });
                    Check(donor.name + ":floor_20mm_slide", ResearchS1HeldCollision.Allows(clone.transform, Shift(clone.transform, Vector3.right * .02f)), true,
                        new { initial_overlap_m = .0002f, motion = "tangent with unchanged shallow penetration" });
                    Check(donor.name + ":floor_20mm_push_down", ResearchS1HeldCollision.Allows(clone.transform, Shift(clone.transform, Vector3.down * .02f)), false);
                    UnityEngine.Object.DestroyImmediate(floor);
                    var bounds = solids[0].bounds; foreach (var c in solids.Skip(1)) bounds.Encapsulate(c.bounds);
                    var barrier = new GameObject("REPRO_THIN_BARRIER"); SceneManager.MoveGameObjectToScene(barrier, scene);
                    barrier.transform.position = new Vector3(bounds.max.x + .01f, bounds.center.y, bounds.center.z);
                    var wall = barrier.AddComponent<BoxCollider>(); wall.size = new Vector3(.0001f, bounds.size.y + .4f, bounds.size.z + .4f);
                    Physics.SyncTransforms();
                    Check(donor.name + ":thin_wall_60mm_crossing", ResearchS1HeldCollision.Allows(clone.transform, Shift(clone.transform, Vector3.right * .06f)), false);
                    UnityEngine.Object.DestroyImmediate(barrier);
                    UnityEngine.Object.DestroyImmediate(clone);
                }
                var whole = UnityEngine.Object.Instantiate(b.gameObject);
                SceneManager.MoveGameObjectToScene(whole, scene);
                Freeze(whole);
                whole.transform.localScale = b.transform.lossyScale;
                whole.transform.position += Far;
                var fan = whole.GetComponent<ResearchS1TaskBMechanics>();
                fan.port = whole.AddComponent<ResearchTaskPort>();
                fan.port.taskId = "B"; fan.port.SetInteractionAllowed(true);
                var guard = fan.frontGuard;
                Set(guard, "mateInRoot", guard.transform.worldToLocalMatrix * guard.mate.localToWorldMatrix);
                Call(fan.frontClip, "SampleClip", false);
                var mate = Get<Matrix4x4>(guard, "mateInRoot");
                var entry = SocketRules.RootPose(guard.seat.localToWorldMatrix, 0, guard.insertTurnDegrees, mate);
                Pose(guard.transform, entry);
                Set(guard, "captured", true);
                var outward = SocketRules.RootPose(guard.seat.localToWorldMatrix, guard.approachMm + 10, guard.insertTurnDegrees, mate);
                Check("bayonet_cap_contact_scoped_to_entry", Call(fan, "GuardEntryContact", guard, outward) != null, true);
                var sideways = Matrix4x4.TRS((Vector3)outward.GetColumn(3) + guard.seat.right * .02f, outward.rotation, outward.lossyScale);
                Check("bayonet_cap_contact_rejects_sideways", Call(fan, "GuardEntryContact", guard, sideways) == null, true);
                Call(fan.frontClip, "SampleClip", true);
                Check("bayonet_cap_contact_rejects_closed_clip", Call(fan, "GuardEntryContact", guard, outward) == null, true);
                Call(fan.frontClip, "SampleClip", false);
                var locked = SocketRules.RootPose(guard.seat.localToWorldMatrix, 0, 0, mate);
                Pose(guard.transform, locked);
                Check("bayonet_cap_contact_rejects_locked_twist", Call(fan, "GuardEntryContact", guard, outward) == null, true);
                Pose(guard.transform, entry);
                var guardBounds = Solids(guard.gameObject)[0].bounds;
                var guardWall = new GameObject("REPRO_GUARD_FOREIGN_WALL");
                SceneManager.MoveGameObjectToScene(guardWall, scene);
                var withdrawal = ((Vector3)outward.GetColumn(3) - guard.transform.position).normalized;
                var extent = Mathf.Abs(withdrawal.x) * guardBounds.extents.x + Mathf.Abs(withdrawal.y) * guardBounds.extents.y + Mathf.Abs(withdrawal.z) * guardBounds.extents.z;
                guardWall.transform.SetPositionAndRotation(guardBounds.center + withdrawal * (extent + .005f), Quaternion.LookRotation(withdrawal));
                var guardWallBox = guardWall.AddComponent<BoxCollider>();
                guardWallBox.size = new Vector3(2, 2, .001f);
                Physics.SyncTransforms();
                var wallPose = fan.FreePose(guard, outward);
                Check("bayonet_cap_exemption_preserves_foreign_wall", Vector3.Distance(guard.transform.position, wallPose.GetColumn(3)) < .00001f, true);
                UnityEngine.Object.DestroyImmediate(guardWall);
                Physics.SyncTransforms();
                var beforePlace = guard.Placement.ToString();
                // The filled guard shell still surrounds the blade cap until ~45 mm out; detaching is checked past that guide.
                var cleared = SocketRules.RootPose(guard.seat.localToWorldMatrix, 60, guard.insertTurnDegrees, mate);
                var proposed = (Matrix4x4)Call(guard, "FrontPose", SocketRules.Measure(guard.seat.localToWorldMatrix, cleared * mate), cleared);
                bool moved = Vector3.Distance(guard.transform.position, proposed.GetColumn(3)) > .00001f;
                Check("guard_actual_assembly_withdraw", moved, true, new { beforePlace, capturedAfter = Get<bool>(guard, "captured"), approachMm = guard.approachMm,
                    requested_mm = Vector3.Distance(guard.transform.position, cleared.GetColumn(3)) * 1000 });
                if (moved) Pose(guard.transform, proposed);
                Check("innercap_gate_after_real_withdraw", fan.ManipulationBlockReason(fan.spinner) == PartBlockReason.None, true,
                    new { guardPlacement = guard.Placement.ToString(), reason = fan.ManipulationBlockReason(fan.spinner).ToString() });
                var detached = SocketRules.RootPose(guard.seat.localToWorldMatrix, 200, guard.insertTurnDegrees, mate);
                var carried = fan.FreePose(guard, detached);
                Check("guard_can_carry_clear_after_withdraw", Vector3.Distance(carried.GetColumn(3), detached.GetColumn(3)) < .00001f, true);
                Pose(guard.transform, carried);
                Check("detached_guard_has_no_cap_exemption", Call(fan, "GuardEntryContact", guard, Shift(guard.transform, withdrawal * .03f)) == null, true);
                var cap = fan.spinner;
                Set(cap, "mateInRoot", cap.transform.worldToLocalMatrix * cap.mate.localToWorldMatrix);
                var capBefore = cap.ThreadProgressDegrees;
                var capNext = (Matrix4x4)Call(cap, "ThreadPose", cap.Pose, -30f, cap.transform.localToWorldMatrix);
                Check("innercap_loosen_after_guard_placed", cap.ThreadProgressDegrees < capBefore, true,
                    new { beforeDegrees = capBefore, afterDegrees = cap.ThreadProgressDegrees });
                Check("innercap_loosen_pose_clears_actual_colliders", (bool)Call(fan, "AllowsThreadPose", cap, capNext, true), true);
                Pose(cap.transform, capNext);
                Check("innercap_retains_engaged_thread_state", cap.ThreadState == S1ThreadState.EngagedLoose, true, cap.ThreadState.ToString());
                Pose(guard.transform, entry);
                Set(guard, "captured", true);
                var overlapRows = new List<object>();
                foreach (var own in Solids(guard.gameObject)) foreach (var other in Solids(whole))
                {
                    if (other.transform.IsChildOf(guard.transform) || Physics.GetIgnoreCollision(own, other)) continue;
                    if (Physics.ComputePenetration(own, own.transform.position, own.transform.rotation, other, other.transform.position, other.transform.rotation, out var direction, out var depth))
                        overlapRows.Add(new { collider = own.name, foreign = other.name, depth_mm = depth * 1000, direction = direction.ToString("F6") });
                }
                rows.Add(new { id = "guard_initial_contacts", pairs = overlapRows });
                foreach (var c in Solids(whole)) if (!c.transform.IsChildOf(guard.transform)) c.enabled = false;
                Set(guard, "captured", true);
                proposed = (Matrix4x4)Call(guard, "FrontPose", SocketRules.Measure(guard.seat.localToWorldMatrix, cleared * mate), cleared);
                Check("guard_withdraw_foreign_disabled_control", Vector3.Distance(guard.transform.position, proposed.GetColumn(3)) > .00001f, true,
                    new { capturedAfter = Get<bool>(guard, "captured") });
                Pose(guard.transform, proposed);
                Check("guard_state_detached_after_withdraw", guard.Placement == S1Placement.NotInstalled, true, guard.Placement.ToString());
                Check("innercap_gate_foreign_disabled_control", fan.ManipulationBlockReason(fan.spinner) == PartBlockReason.None, true,
                    fan.ManipulationBlockReason(fan.spinner).ToString());
                UnityEngine.Object.DestroyImmediate(whole);
            }
            catch (Exception error) { issues.Add(error.ToString()); }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                SceneManager.SetActiveScene(active);
                restored = before.SequenceEqual(Scenes());
                if (!restored) issues.Add("scene state changed");
            }
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonConvert.SerializeObject(new
            { evidence = "EDITOR_ACTUAL_DONOR_PRODUCTION_COLLISION_NOT_NATIVE_INPUT", status = issues.Count == 0 ? "PASS" : "ISSUES", restored, issues, rows }, Formatting.Indented));
            return "CARRY_CONSTRAINT_CHECKS " + (issues.Count == 0 ? "PASS" : "ISSUES") + " issues=" + issues.Count + " restored=" + restored;
        }
    }
}
#endif
