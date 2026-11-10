#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Owner 4 Nov 2026: the front-guard prototype in the DEV Reader. Editor runs switch the DEV flavour on for the Play session
    // (the DEV APK compiles it in), then drive every step with simulated controller rays, never Button.onClick.Invoke.
    public static partial class ResearchS1FlowDriver
    {
        public static string SetDevFlavor(bool on) { ResearchBuildFlavor.EditorDevOverride = on; return "dev_flavor=" + ResearchBuildFlavor.Name; }

        // Before a session: the Setup names the DEV build, and study/pilot groups are refused with a stated reason.
        public static string DevFlavorSetupReport()
        {
            var flow = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            var setup = UnityEngine.Object.FindFirstObjectByType<ResearcherSetupView>(FindObjectsInactive.Include);
            Require(flow && flow.Record == null, "Setup report needs the idle setup screen.");
            setup?.SendMessage("Refresh", SendMessageOptions.DontRequireReceiver);
            var scope = setup ? setup.GetType().GetField("setupScope", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(setup) as TMP_Text : null;
            ParticipantRecord Probe(ParticipantGroup g) => new ParticipantRecord { participant_id = "PROBE_" + g, group = g, session_state = RegistryState.Available };
            var rows = Enum.GetValues(typeof(ParticipantGroup)).Cast<ParticipantGroup>().Select(g => new
            {
                group = g.ToString(), allowed = ResearchBuildFlavor.Allows(g),
                blocker = flow.ResearcherStartBlocker(Probe(g)) is string b && b == ResearchBuildFlavor.DevStudyBlocker ? b : null
            }).ToArray();
            return JsonConvert.SerializeObject(new { flavor = ResearchBuildFlavor.Name, scope = scope ? scope.text : null, scope_active = scope && scope.gameObject.activeInHierarchy, rows });
        }

        public static string DevReaderTour() => Start(DevReaderTourRoutine(), 300);

        static IEnumerator DevReaderTourRoutine()
        {
            ReaderLog.Clear();
            Require(ResearchBuildFlavor.DevReaderPrototype && controller.Record.test_data && controller.Flow.IsTaskActive && controller.Flow.CurrentTask == "B",
                "DEV Reader tour needs the DEV flavour and an active DEV/TEST Task B.");
            Require(controller.ReaderResource == ResearchReferenceViewer.DevPrototypeFor(controller.Language), "Task B resolves the DEV prototype: " + controller.ReaderResource);
            yield return RayClick("reference");
            yield return Settle();
            var viewer = controller.view.ReferenceViewer;
            var panel = controller.view.referencePanel;
            var rect = (RectTransform)panel.transform;
            ResearchFlowVisualInput.SetHeadMotion(Vector3.zero, 0);
            ResearchFlowVisualInput.SetHeadFacing(rect.TransformPoint(rect.rect.center));
            yield return Settle();
            if (panel.PlacementBlocked)
            {
                // Same recovery as ReaderTourRoutine: the pedestal fan can block the first pose; turn, then Panel Recovery by ray.
                var head = controller.origin.Camera.transform;
                ResearchFlowVisualInput.SetHeadFacing(head.position + Quaternion.AngleAxis(90, Vector3.up) * head.forward);
                yield return Wait(1.2);
                if (panel.PlacementBlocked) { yield return RayClick("panel_recovery"); yield return Settle(); }
                ResearchFlowVisualInput.SetHeadFacing(rect.TransformPoint(rect.rect.center));
                yield return Settle();
                ReaderLog.Add(new { label = "placement_recovery", blocked_after = panel.PlacementBlocked });
            }
            yield return AwaitViewer(() => panel.IsReadableFromHead, "reference readable facing");
            foreach (var section in ResearchReferenceScheme.Active)
            {
                if (controller.ReferenceSection != section) yield return RayClick("reference_tab." + section);
                yield return AwaitViewer(() => viewer.IsReady && viewer.BoundSection == section && controller.view.IsReferenceActuallyVisible, section + " ready");
                yield return Wait(.6);
                var meta = JObject.FromObject(viewer.Metadata);
                Require(((string)meta["content_id"]).StartsWith(ResearchReferenceViewer.DevPrototypeContent, StringComparison.Ordinal)
                    && (string)meta["approval"] == "S1_DEV_PROTOTYPE_NOT_STUDY", section + " shows the DEV prototype package");
                Require(viewer.ContentsAvailable, section + " offers contents");
                ViewerState(section + "_open"); Capture("dev_" + section);
                var ids = viewer.SectionIds;
                if (section == "video")
                {
                    yield return RayClick("reference_media.play");
                    yield return AwaitViewer(() => viewer.NativeVideoPlayer && viewer.NativeVideoPlayer.isPlaying && viewer.NativeVideoPlayer.time > .5, "video playing");
                }
                yield return RayClick("reference_media.contents");
                yield return AwaitViewer(() => viewer.ContentsOpen, section + " contents open");
                yield return Wait(.3); ViewerState(section + "_contents"); Capture("dev_" + section + "_contents");
                // Jump straight to the last topic, then back to the second: free choice in both directions.
                foreach (var id in new[] { ids.Last(), ids[1] })
                {
                    if (!viewer.ContentsOpen) { yield return RayClick("reference_media.contents"); yield return AwaitViewer(() => viewer.ContentsOpen, "contents reopened"); }
                    yield return RayClick("reference_media.section." + id);
                    yield return AwaitViewer(() => !viewer.ContentsOpen && viewer.SectionId == id && viewer.IsReady, section + " jumped to " + id);
                    yield return Wait(section == "video" ? 1.2 : .5);
                    ViewerState(section + "_" + id); Capture("dev_" + section + "_" + id);
                }
                if (section == "video")
                {
                    Require(viewer.NativeVideoPlayer.isPlaying, "Chapter jump keeps playing");
                    double before = viewer.NativeVideoPlayer.time;
                    yield return RayClick("reference_media.pause");
                    yield return AwaitViewer(() => !viewer.NativeVideoPlayer.isPlaying, "video paused");
                    yield return Wait(.4); ViewerState("video_paused"); Capture("dev_video_paused");
                    ReaderLog.Add(new { label = "video_pause", before, after = viewer.NativeVideoPlayer.time });
                }
            }
            yield return RayClick("reference_close");
            yield return Settle();
            Require(!controller.ReferenceOpen && controller.Flow.IsTaskActive && controller.Flow.CurrentTask == "B" && !controller.Flow.Paused,
                "Closing the Reader returns to the active Task B.");
            Capture("dev_back_to_task_b");
            string[] lines;
            using (var stream = new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream)) lines = reader.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var events = lines.Select(JObject.Parse)
                .Where(e => ((string)e["event"] ?? "").StartsWith("REFERENCE_VIEWER_SECTION", StringComparison.Ordinal) || ((string)e["event"] ?? "").StartsWith("REFERENCE_VIEWER_CONTENTS", StringComparison.Ordinal))
                .Select(e => new { e = (string)e["event"], section = (string)e.SelectToken("after.viewer.section_id") ?? (string)e.SelectToken("after.section_id"),
                    to = (string)e.SelectToken("after.to_section"), media = (string)e.SelectToken("after.viewer.media_type") ?? (string)e.SelectToken("after.media_type"),
                    input = (string)e["input_source"] }).ToArray();
            ReaderLog.Add(new { label = "log", events_path = controller.Logger.EventsPath, count = events.Length, events });
            ResearchFlowVisualInput.SetHeadMotion(Vector3.zero, 0);
            yield return Settle();
            Detail = JsonConvert.SerializeObject(ReaderLog);
        }
    }
}
#endif
