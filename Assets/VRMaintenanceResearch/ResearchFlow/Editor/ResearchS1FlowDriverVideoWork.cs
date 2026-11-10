#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // 8 Nov 2026 check: does the Reader video keep playing while the participant works, and what stops it?
    // Real ray clicks through the production Reader; DEV session, simulated head/controllers; not Quest evidence.
    public static partial class ResearchS1FlowDriver
    {
        public static readonly List<object> VideoRows = new List<object>();
        public static string VideoWorkReport() => JsonConvert.SerializeObject(VideoRows);

        static void VideoRow(string label)
        {
            var viewer = controller.view.ReferenceViewer;
            var player = viewer ? viewer.NativeVideoPlayer : null;
            var panel = controller.view.referencePanel;
            VideoRows.Add(new { label, t = System.Math.Round(Time.realtimeSinceStartupAsDouble, 2),
                playing = player && player.isPlaying, video_time = player && player.isPrepared ? System.Math.Round(player.time, 2) : -1,
                reader_visible = controller.view.IsReferenceActuallyVisible, reader_open = controller.ReferenceOpen,
                placement_blocked = panel.PlacementBlocked, rejection = panel.LastRejection,
                hint = viewer && viewer.StatusMessageVisible, head = controller.origin.Camera.transform.position.ToString("F2"), task_state = controller.Flow?.State.ToString() });
        }

        static IEnumerator FaceReader(RectTransform rect)
        {
            ResearchFlowVisualInput.SetHeadFacing(rect.TransformPoint(rect.rect.center)); yield return Settle();
        }

        public static string VideoRecord(string label) { VideoRow(label); return VideoWorkReport(); }

        // Opens the Reader on the video, plays it, looks at the fan (head turn only), walks to the fan's side and back.
        public static string VideoWorkBegin() => Start(VideoWorkRoutine(), 180);
        static IEnumerator VideoWorkRoutine()
        {
            Require(controller.Flow.IsTaskActive, "Video work check needs an active task.");
            VideoRows.Clear();
            // Same open sequence as ReaderTour: face the station view first (opening while facing the fan leaves the Reader concealed).
            var station = controller.stations[controller.CurrentStationIndex];
            ResearchFlowVisualInput.SetHeadMotion(Vector3.zero, 0);
            ResearchFlowVisualInput.SetHeadFacing(station.position + station.forward * 2f + Vector3.up * (float)controller.StationEyeHeight);
            yield return Settle();
            yield return RayClick("reference"); yield return Settle();
            var panel = controller.view.referencePanel; var viewer = controller.view.ReferenceViewer;
            var rect = (RectTransform)panel.transform;
            ResearchFlowVisualInput.SetHeadFacing(rect.TransformPoint(rect.rect.center)); yield return Settle();
            VideoRow("reader_opened"); Capture("vw_00_reader_opened");
            if (panel.PlacementBlocked)
            {
                var head = controller.origin.Camera.transform;
                ResearchFlowVisualInput.SetHeadFacing(head.position + Quaternion.AngleAxis(90, Vector3.up) * head.forward);
                yield return Wait(1.2);
                if (panel.PlacementBlocked) { yield return RayClick("panel_recovery"); yield return Settle(); }
                ResearchFlowVisualInput.SetHeadFacing(rect.TransformPoint(rect.rect.center)); yield return Settle();
                VideoRow("reader_after_recovery"); Capture("vw_00b_reader_recovered");
            }
            yield return AwaitViewer(() => panel.IsReadableFromHead, "reference readable facing");
            if (controller.ReferenceSection != "video") yield return RayClick("reference_tab.video");
            yield return AwaitViewer(() => viewer.IsReady && viewer.BoundSection == "video" && controller.view.IsReferenceActuallyVisible, "video ready");
            yield return RayClick("reference_media.play");
            yield return AwaitViewer(() => viewer.NativeVideoPlayer && viewer.NativeVideoPlayer.isPlaying && viewer.NativeVideoPlayer.time > 1, "video playing");
            VideoRow("playing_facing_reader"); Capture("vw_01_playing");

            var work = Object.FindObjectsByType<ResearchExamineTarget>(FindObjectsSortMode.None)
                .Single(t => t.stableTarget == controller.Flow.CurrentTask + ".module").transform;
            ResearchFlowVisualInput.SetHeadFacing(work.position); yield return Wait(3);
            VideoRow("head_turned_to_workpiece_3s"); Capture("vw_02_head_to_work");

            // Side step like the Task B rear-guard view: walk beside the workpiece and look at it.
            var home = controller.origin.Camera.transform.position;
            ResearchFlowVisualInput.WalkHeadTo(new Vector3(work.position.x, home.y, work.position.z) + station.right * .85f); yield return Wait(.5);
            ResearchFlowVisualInput.SetHeadFacing(work.position); yield return Wait(2);
            VideoRow("beside_workpiece_2s"); Capture("vw_03_beside");

            ResearchFlowVisualInput.WalkHeadTo(home); yield return Wait(.5);
            ResearchFlowVisualInput.SetHeadFacing(rect.TransformPoint(rect.rect.center)); yield return Wait(2.5);
            VideoRow("back_facing_reader_2s"); Capture("vw_04_back");
            if (!(viewer.NativeVideoPlayer && viewer.NativeVideoPlayer.isPlaying))
            {
                yield return AwaitViewer(() => viewer.IsReady && controller.view.IsReferenceActuallyVisible, "video ready after return");
                yield return RayClick("reference_media.play");
                yield return AwaitViewer(() => viewer.NativeVideoPlayer && viewer.NativeVideoPlayer.isPlaying, "video playing again");
                VideoRow("played_again");
            }
            // Amendment cases: forced system hide (edge-on far to the side) for 3 s resumes; for 12 s stays paused with a hint;
            // a participant Pause before the hide never resumes.
            // Behind the Reader plane: the panel faces away from the eye (rear_face), a deterministic system hide.
            var far = rect.position + rect.forward * 1.2f;
            System.Func<IEnumerator> faceReader = () => FaceReader(rect);
            ResearchFlowVisualInput.WalkHeadTo(far); ResearchFlowVisualInput.SetHeadFacing(rect.position); yield return Wait(3);
            VideoRow("forced_hide_3s"); Require(controller.view.referencePanel.PlacementBlocked, "forced hide did not hide the Reader");
            ResearchFlowVisualInput.WalkHeadTo(home); yield return faceReader(); yield return Wait(3);
            VideoRow("returned_after_3s_expect_playing");
            ResearchFlowVisualInput.WalkHeadTo(far); ResearchFlowVisualInput.SetHeadFacing(rect.position); yield return Wait(12);
            VideoRow("forced_hide_12s");
            ResearchFlowVisualInput.WalkHeadTo(home); yield return faceReader(); yield return Wait(3);
            VideoRow("returned_after_12s_expect_paused_hint");
            yield return RayClick("reference_media.play");
            yield return AwaitViewer(() => viewer.NativeVideoPlayer && viewer.NativeVideoPlayer.isPlaying, "play after long hide");
            yield return Wait(1.5);
            yield return RayClick("reference_media.pause");
            yield return AwaitViewer(() => viewer.NativeVideoPlayer && !viewer.NativeVideoPlayer.isPlaying, "user pause");
            VideoRow("user_paused");
            ResearchFlowVisualInput.WalkHeadTo(far); ResearchFlowVisualInput.SetHeadFacing(rect.position); yield return Wait(3);
            ResearchFlowVisualInput.WalkHeadTo(home); yield return faceReader(); yield return Wait(3);
            VideoRow("user_paused_hide_return_expect_paused");
            yield return RayClick("reference_media.play");
            yield return AwaitViewer(() => viewer.NativeVideoPlayer && viewer.NativeVideoPlayer.isPlaying, "play before repair");
            ResearchFlowVisualInput.SetHeadFacing(work.position); yield return Wait(.5);
            VideoRow("before_repair");
        }
    }
}
#endif
