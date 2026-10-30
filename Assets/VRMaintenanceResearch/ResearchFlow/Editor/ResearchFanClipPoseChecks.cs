#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchFanClipPoseChecks
    {
        public static string Run(string directory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor required.");
            if (Directory.Exists(directory)) throw new IOException("Use a fresh evidence directory.");
            Directory.CreateDirectory(directory);
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            int checks = 0;
            void Check(bool pass, string name)
            {
                checks++;
                if (!pass) throw new InvalidOperationException(name);
            }
            try
            {
                root = UnityEngine.Object.Instantiate(donor.gameObject);
                SceneManager.MoveGameObjectToScene(root, scene);
                var fan = root.GetComponent<ResearchS1TaskBMechanics>();
                var port = root.AddComponent<ResearchTaskPort>();
                port.taskId = "B";
                port.demoMode = true;
                fan.port = port;
                port.SetInteractionAllowed(true);
                var clip = fan.frontClip;
                Check(clip.ClipPosesAuthored, "Current scene includes baked clip poses");
                var sample = typeof(ResearchS1FanPart).GetMethod("SampleClip", BindingFlags.Instance | BindingFlags.NonPublic);
                var fixedTransforms = root.GetComponentsInChildren<Transform>(true).Where(t => t != clip.transform).ToArray();
                var positions = fixedTransforms.Select(t => t.localPosition).ToArray();
                var rotations = fixedTransforms.Select(t => t.localRotation).ToArray();
                var open = clip.clipOpen;
                var closed = clip.clipClose;
                clip.clipOpen = clip.clipClose = null;
                for (int cycle = 0; cycle < 25; cycle++)
                {
                    sample.Invoke(clip, new object[] { true });
                    Check(clip.ClipClosed && !fan.CanManipulate(fan.frontGuard), "Closed clip blocks guard");
                    var before = clip.transform.localRotation;
                    sample.Invoke(clip, new object[] { false });
                    Check(!clip.ClipClosed && fan.CanManipulate(fan.frontGuard), "Open clip releases guard");
                    Check(Mathf.Abs(Quaternion.Angle(before, clip.transform.localRotation) - 35) < .02f, "Authored 35-degree hinge motion");
                    Check(Quaternion.Angle(clip.body.rotation, clip.transform.rotation) < .01f, "Rigidbody agrees with visible hinge");
                    port.SetInteractionAllowed(false);
                    Check(!fan.CanManipulate(fan.frontGuard), "Pause still blocks interaction");
                    port.SetInteractionAllowed(true);
                }
                clip.clipOpen = open;
                clip.clipClose = closed;
                for (int i = 0; i < fixedTransforms.Length; i++)
                    Check(Vector3.Distance(positions[i], fixedTransforms[i].localPosition) < .000001f &&
                          Quaternion.Angle(rotations[i], fixedTransforms[i].localRotation) < .01f, "Only clip local pose changes");
                bool rejected = false;
                try { clip.AuthorClipPoses(Pose.identity, Pose.identity); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Reject identical authored states");
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var threadPose = typeof(ResearchS1FanPart).GetMethod("ThreadPose", flags);
                var progress = typeof(ResearchS1FanPart).GetField("progressDegrees", flags);
                var backoff = typeof(ResearchS1FanPart).GetField("stopBackoff", flags);
                var captured = typeof(ResearchS1FanPart).GetField("captured", flags);
                foreach (var part in new[] { fan.spinner, fan.rearLock })
                {
                    float initial = part.ThreadProgressDegrees;
                    float delta = part.rightHanded ? 20 : -20;
                    var hand = part.Pose;
                    threadPose.Invoke(part, new object[] { hand, delta, part.transform.localToWorldMatrix });
                    float centered = part.ThreadProgressDegrees;
                    progress.SetValue(part, initial);
                    backoff.SetValue(part, 0d);
                    var wrist = new VRMaintenanceResearch.AssetLibraryQA.MatePose(hand.depthMm, 6.975647f, 20, hand.turnDeg);
                    threadPose.Invoke(part, new object[] { wrist, delta, part.transform.localToWorldMatrix });
                    Check(centered < initial && Mathf.Abs(centered - part.ThreadProgressDegrees) < .01f,
                        "Captured thread accepts off-axis twist: " + part.stableId + " initial=" + initial + " centered=" + centered + " offset=" + part.ThreadProgressDegrees);
                    Check((bool)captured.GetValue(part), "Engaged thread remains captured");
                    progress.SetValue(part, 0f);
                    captured.SetValue(part, false);
                    threadPose.Invoke(part, new object[] { wrist, delta, part.transform.localToWorldMatrix });
                    Check(!(bool)captured.GetValue(part) && part.ThreadProgressDegrees == 0,
                        "Off-axis free part does not engage a thread");
                }
                string result = "FAN_CLIP_POSE_PASS checks=" + checks + " cycles=25 runtime_animation_dependency=none";
                File.WriteAllText(Path.Combine(directory, "result.txt"), result);
                return result;
            }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
#endif
