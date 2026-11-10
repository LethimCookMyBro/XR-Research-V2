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

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchCueRecenterChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
            var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty;
        }).ToArray();
        public static string Run(string directory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed
                || !Path.IsPathRooted(directory) || Directory.Exists(directory))
                throw new InvalidOperationException("Stopped Editor, no XR and fresh absolute directory required.");
            Directory.CreateDirectory(directory); var rows = new List<object>(); int failures = 0;
            void Check(bool pass, string name, object observed = null) { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            var before = Scenes(); var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var owner = new GameObject("CueRecenterFixture"); owner.SetActive(false);
                var view = owner.AddComponent<ResearchFlowView>(); view.enabled = false;
                var camera = new GameObject("FixtureHead", typeof(Camera)).GetComponent<Camera>();
                camera.enabled = false; camera.fieldOfView = 60; camera.aspect = 16f / 9; camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.transform.position = new Vector3(1000, 1.7f, 998); view.Head = camera.transform;
                var rect = new GameObject("FixtureReader", typeof(RectTransform), typeof(Canvas)).GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(1180, 800); rect.position = new Vector3(1000, 1.7f, 1000); rect.localScale = Vector3.one * .001f;
                rect.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var panel = rect.gameObject.AddComponent<ResearchPanelMotion>(); panel.enabled = false; panel.view = view; panel.panelId = "reference"; view.referencePanel = panel;
                typeof(ResearchPanelMotion).GetField("environment", Hidden).SetValue(panel, Array.Empty<Renderer>());
                void Reset()
                {
                    typeof(ResearchPanelMotion).GetProperty("RecenterPending").SetValue(panel, false);
                    typeof(ResearchPanelMotion).GetField("placementAccepted", Hidden).SetValue(panel, true);
                }
                object PoseState() => new { accepted = panel.HasAcceptedPose, pending = panel.RecenterPending, blocked = panel.PlacementBlocked, pose = panel.Pose(), rejection = panel.LastRejection };
                Canvas.ForceUpdateCanvases(); Physics.SyncTransforms();
                Check(panel.PlacementClear(rect.position, rect.rotation), "fixture accepted pose world-clear", panel.LastRejection);
                Check(panel.IsReadableFromHead, "fixture full view fits");
                Reset(); var position = rect.position; var rotation = rect.rotation;
                panel.Recenter(false, false, onlyIfNeeded: true, reason: "displayed_instruction:video_pause");
                Check(panel.HasAcceptedPose, "valid cue refresh retains accepted pose immediately", PoseState());
                Check(!panel.RecenterPending, "valid cue refresh does not create transient hidden pending state", PoseState());
                Check(position == rect.position && rotation == rect.rotation, "valid cue refresh does not move panel", PoseState());
                Reset(); typeof(ResearchPanelMotion).GetProperty("RecenterPending").SetValue(panel, true);
                panel.Recenter(false, false, onlyIfNeeded: true, reason: "pending_fixture");
                Check(panel.RecenterPending && !panel.HasAcceptedPose, "existing pending request is not bypassed", PoseState());
                Reset(); camera.transform.rotation = Quaternion.Euler(0, 90, 0);
                panel.Recenter(false, false, onlyIfNeeded: true, reason: "out_of_view_fixture");
                Check(panel.RecenterPending && !panel.HasAcceptedPose, "out of view still invalidates and queues", PoseState());
                camera.transform.rotation = Quaternion.identity;
                var obstacle = new GameObject("FixtureBlockingCollider", typeof(BoxCollider)); obstacle.transform.position = rect.position;
                obstacle.GetComponent<BoxCollider>().size = new Vector3(2, 2, .2f); Physics.SyncTransforms(); Reset();
                Check(!panel.PlacementClear(rect.position, rect.rotation), "actual blocker fails full placement", panel.LastRejection);
                panel.Recenter(false, false, onlyIfNeeded: true, reason: "blocked_fixture");
                Check(panel.RecenterPending && !panel.HasAcceptedPose, "blocked pose still invalidates and queues", PoseState());
                UnityEngine.Object.DestroyImmediate(obstacle); Physics.SyncTransforms(); Reset();
                typeof(ResearchPanelMotion).GetField("placementAccepted", Hidden).SetValue(panel, false);
                panel.Recenter(false, false, onlyIfNeeded: true, reason: "unaccepted_fixture");
                Check(panel.RecenterPending && !panel.HasAcceptedPose, "unaccepted pose still requires placement", PoseState());
                Reset(); typeof(ResearchPanelMotion).GetField("requestedFrame", Hidden).SetValue(panel, -123);
                panel.Recenter(false, false, onlyIfNeeded: false, reason: "explicit_fixture");
                Check((int)typeof(ResearchPanelMotion).GetField("requestedFrame", Hidden).GetValue(panel) != -123,
                    "explicit recenter still enters placement request path", PoseState());
            }
            catch (Exception error) { Check(false, "fixture exception", error.ToString()); }
            finally { EditorSceneManager.CloseScene(scene, true); if (active.IsValid()) SceneManager.SetActiveScene(active); }
            Check(before.SequenceEqual(Scenes()), "owning scenes preserved");
            string status = failures == 0 ? "PASS" : "FAIL";
            File.WriteAllText(Path.Combine(directory, "cue-recenter.json"), JsonConvert.SerializeObject(new { status, evidence = "STOPPED_EDITOR_ACTUAL_GEOMETRY_NOT_NATIVE_PLAYBACK", checks = rows.Count, failures, rows }, Formatting.Indented));
            return "CUE_RECENTER " + status + " checks=" + rows.Count + " failures=" + failures;
        }
    }
}
#endif
