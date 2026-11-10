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
    public static class ResearchFanReassemblyChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Hidden).SetValue(obj, value);
        static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Hidden).GetValue(obj);
        static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Hidden).Invoke(obj, args);
        static void Place(Transform root, Matrix4x4 pose)
        {
            root.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            var body = root.GetComponent<Rigidbody>();
            if (body) { body.position = root.position; body.rotation = root.rotation; }
            Physics.SyncTransforms();
        }
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();

        public static string Run(string directory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !Path.IsPathRooted(directory) || Directory.Exists(directory))
                throw new InvalidOperationException("Stopped Editor and fresh absolute evidence directory required.");
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
            if (!donor) throw new InvalidOperationException("Canonical fan donor required.");
            var before = Scenes(); var active = SceneManager.GetActiveScene();
            var rows = new List<object>(); var failures = new List<string>();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            void Check(bool pass, string id, object detail = null) { rows.Add(new { id, pass, detail }); if (!pass) failures.Add(id); }
            try
            {
                var clone = UnityEngine.Object.Instantiate(donor.gameObject); SceneManager.MoveGameObjectToScene(clone, scene);
                clone.transform.localScale = donor.transform.lossyScale; clone.transform.position += new Vector3(120, 20, 120);
                foreach (var m in clone.GetComponentsInChildren<MonoBehaviour>(true)) m.enabled = false;
                foreach (var b in clone.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; b.useGravity = false; }
                var fan = clone.GetComponent<ResearchS1TaskBMechanics>();
                fan.port = clone.AddComponent<ResearchTaskPort>(); fan.port.taskId = "B"; fan.port.SetInteractionAllowed(true);
                var guard = fan.frontGuard;
                Call(fan.frontClip, "SampleClip", false);
                var mate = guard.transform.worldToLocalMatrix * guard.mate.localToWorldMatrix;
                Set(guard, "mateInRoot", mate);
                Matrix4x4 At(float depth, float turn) => SocketRules.RootPose(guard.seat.localToWorldMatrix, depth, turn, mate);
                void Step(float depth, string direction)
                {
                    var desired = At(depth, guard.insertTurnDegrees);
                    float oldDepth = guard.Pose.depthMm;
                    var accepted = (Matrix4x4)Call(guard, "FrontPose", SocketRules.Measure(guard.seat.localToWorldMatrix, desired * mate), desired);
                    Place(guard.transform, accepted);
                    var observed = guard.Pose;
                    Check(Mathf.Abs(observed.depthMm - depth) < .08f, direction + "_" + depth + "mm", new
                    { requested_depth_mm = depth, old_depth_mm = oldDepth, observed_depth_mm = observed.depthMm, captured = Get<bool>(guard, "captured"), placement = guard.Placement.ToString() });
                }
                Place(guard.transform, At(0, guard.insertTurnDegrees)); Set(guard, "captured", true);
                Step(30, "withdraw_initial");
                for (int depth = 35; depth <= 80; depth += 5) Step(depth, "withdraw_continued");
                Check(guard.Placement == S1Placement.NotInstalled, "withdrawal_finishes_detached");
                Check(fan.ManipulationBlockReason(fan.spinner) == PartBlockReason.None, "withdrawal_unblocks_inner_cap", fan.ManipulationBlockReason(fan.spinner).ToString());
                var cap = fan.spinner; Set(cap, "mateInRoot", cap.transform.worldToLocalMatrix * cap.mate.localToWorldMatrix);
                float initial = cap.ThreadProgressDegrees;
                var capPose = (Matrix4x4)Call(cap, "ThreadPose", cap.Pose, -10f, cap.transform.localToWorldMatrix);
                Check(cap.ThreadProgressDegrees < initial && (bool)Call(fan, "AllowsThreadPose", cap, capPose, true), "inner_cap_can_loosen_after_withdrawal", new { before = initial, after = cap.ThreadProgressDegrees });
                cap.AuthorInitialPose(cap.fullTurnDegrees);
                Place(guard.transform, At(80, guard.insertTurnDegrees)); Set(guard, "captured", false);
                for (int depth = 75; depth >= 0; depth -= 5) Step(depth, "refit_continued");
                var locked = At(0, 0);
                var lockedAccepted = (Matrix4x4)Call(guard, "FrontPose", SocketRules.Measure(guard.seat.localToWorldMatrix, locked * mate), locked);
                Place(guard.transform, lockedAccepted); Call(fan.frontClip, "SampleClip", true);
                Check(guard.Placement == S1Placement.Seated && fan.frontClip.ClipClosed, "refit_locks_and_clips", new { placement = guard.Placement.ToString(), fan.frontClip.ClipClosed });
                Check(fan.ManipulationBlockReason(fan.spinner) == PartBlockReason.FrontGuardPresent, "refit_restores_inner_cap_guard", fan.ManipulationBlockReason(fan.spinner).ToString());

                // Owner 31 Oct: a real hand wobbles 1-2 cm and 10 deg and does not twist an exact angle. The old guide dropped such a guard into the cap.
                Call(fan.frontClip, "SampleClip", false);
                var axis = ((Vector3)At(10, 0).GetColumn(3) - (Vector3)At(0, 0).GetColumn(3)).normalized;
                var side = Vector3.Cross(axis, Vector3.up).normalized;
                Matrix4x4 Hand(float depth, float turn, float lateralMm, float tiltDeg)
                {
                    var pose = At(depth, turn); var p = (Vector3)pose.GetColumn(3);
                    return Matrix4x4.Translate(side * lateralMm / 1000f) * Matrix4x4.Translate(p) * Matrix4x4.Rotate(Quaternion.AngleAxis(tiltDeg, side)) * Matrix4x4.Translate(-p) * pose;
                }
                MatePose Move(Matrix4x4 hand)
                {
                    var accepted = (Matrix4x4)Call(guard, "FrontPose", SocketRules.Measure(guard.seat.localToWorldMatrix, hand * mate), hand);
                    Place(guard.transform, accepted); return guard.Pose;
                }
                Place(guard.transform, At(0, 0)); Set(guard, "captured", true);
                var wobble = SocketRules.Measure(guard.seat.localToWorldMatrix, Hand(10, 0, 12, 10) * mate);
                Check(wobble.lateralMm > 8 && wobble.tiltDeg > 8, "wobble_fixture_exceeds_old_guide", new { wobble.lateralMm, wobble.tiltDeg });
                var turned = Move(Hand(0, guard.insertTurnDegrees * .4f, 12, 10));
                Check(Mathf.Abs(turned.turnDeg) < .1f, "small_twist_stays_locked", new { turned.turnDeg });
                turned = Move(Hand(0, guard.insertTurnDegrees * .75f, 12, 10));
                Check(Mathf.Abs(turned.turnDeg - guard.insertTurnDegrees) < .1f && turned.depthMm < .3f, "twist_past_two_thirds_snaps_unlocked", new { turned.turnDeg });
                for (int depth = 5; depth <= 50; depth += 5)
                {
                    var pulled = Move(Hand(depth, guard.insertTurnDegrees, depth % 10 == 0 ? 12 : -12, 10));
                    Check(Mathf.Abs(pulled.depthMm - depth) < .1f && pulled.lateralMm < .1f, "wobbly_withdraw_follows_axis_" + depth + "mm", new { pulled.depthMm, pulled.lateralMm });
                }
                Move(Hand(60, guard.insertTurnDegrees, 12, 10));
                Check(guard.Placement == S1Placement.NotInstalled, "wobbly_withdraw_detaches", new { guard.Pose.depthMm, placement = guard.Placement.ToString() });
                for (int depth = 50; depth >= 0; depth -= 5)
                {
                    var refit = Move(Hand(depth, guard.insertTurnDegrees + 15, 10, 8));
                    Check(Mathf.Abs(refit.depthMm - depth) < .1f && Mathf.Abs(refit.turnDeg - guard.insertTurnDegrees) < .1f, "wobbly_misturned_refit_" + depth + "mm", new { refit.depthMm, refit.turnDeg });
                }
                var relocked = Move(Hand(0, guard.insertTurnDegrees * .2f, 10, 8));
                Check(guard.Placement == S1Placement.Seated && fan.FrontGuardLocked, "twist_back_snaps_locked", new { relocked.turnDeg, placement = guard.Placement.ToString() });
                UnityEngine.Object.DestroyImmediate(clone);
            }
            catch (Exception error) { Check(false, "execution", (error.InnerException ?? error).ToString()); }
            finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(active); }
            Check(before.SequenceEqual(Scenes()), "original_scene_state_preserved");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonConvert.SerializeObject(new
            { status = failures.Count == 0 ? "PASS" : "ISSUES", failures, rows, evidence = "Actual donor clone and production FrontPose/FreePose/ThreadPose calls. Explicit synthetic hand poses. No native controller or wearer claim." }, Formatting.Indented));
            return "FAN_REASSEMBLY " + (failures.Count == 0 ? "PASS" : "ISSUES") + " checks=" + rows.Count + " failures=" + failures.Count;
        }
    }
}
#endif
