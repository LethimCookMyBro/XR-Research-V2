#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // 8 Nov 2026: can the Reader be opened, read and pressed from the poses a participant really takes at the current station?
    // Opens from each pose without turning the head first, records placement state, then turns to the Reader as a person would,
    // and finally presses all four format tabs with the SDK ray. Simulated head/controllers in the Editor (60 deg mono view): not Quest.
    public static partial class ResearchS1FlowDriver
    {
        public static readonly List<object> ReachRows = new List<object>();
        public static string ReaderReachReport() => JsonConvert.SerializeObject(ReachRows);
        public static string ReaderReachSweep() => Start(ReaderReachRoutine(), 300);

        static void ReachRow(string pose, string phase)
        {
            var panel = controller.view.referencePanel;
            var head = controller.view.Head;
            ReachRows.Add(new { pose, phase, task = controller.Flow?.CurrentTask, open = controller.ReferenceOpen,
                accepted = panel.HasAcceptedPose, blocked = panel.PlacementBlocked, recenter_pending = panel.RecenterPending, busy = controller.view.PanelPlacementBusy,
                rejection = panel.LastRejection, kind = panel.PlacementFailureKind, fits_view = panel.IsReadableFromHead,
                visible = controller.view.IsReferenceActuallyVisible, feedback = controller.view.ReaderPlacementFeedbackVisible,
                distance = System.Math.Round(Vector3.Distance(panel.transform.position, head.position), 2),
                head = head.position.ToString("F2"), panel_pos = panel.transform.position.ToString("F2") });
        }

        static IEnumerator ReaderReachRoutine()
        {
            Require(controller.Flow.IsTaskActive, "Reader reach sweep requires an active task.");
            ReachRows.Clear();
            var station = controller.stations[controller.CurrentStationIndex];
            float eye = (float)controller.StationEyeHeight;
            var work = Object.FindObjectsByType<ResearchExamineTarget>(FindObjectsSortMode.None)
                .Single(t => t.stableTarget == controller.Flow.CurrentTask + ".module").transform;
            Vector3 home = station.position + Vector3.up * eye;
            var flat = new Vector3(work.position.x, 0, work.position.z);
            var poses = new (string name, Vector3 headAt, Vector3 gaze)[]
            {
                ("stand_level", home, home + station.forward * 2f),
                ("stand_look_work", home, work.position),
                ("stand_look_work_low", home, work.position + Vector3.down * .35f),
                ("crouch_look_work", station.position + Vector3.up * 1.15f, work.position),
                ("side_right_look_work", flat + Vector3.up * eye + station.right * .75f, work.position),
                ("side_left_look_work", flat + Vector3.up * eye - station.right * .75f, work.position),
            };
            ResearchFlowVisualInput.SetHeadMotion(Vector3.zero, 0);
            foreach (var p in poses)
            {
                controller.CloseReference("reader_reach_sweep", "System"); yield return Settle();
                ResearchFlowVisualInput.WalkHeadTo(p.headAt); ResearchFlowVisualInput.SetHeadFacing(p.gaze); yield return Wait(.4);
                controller.OpenReference("None"); yield return Wait(2);
                ReachRow(p.name, "opened_without_turning"); Capture("reach_" + p.name + "_a");
                var rect = (RectTransform)controller.view.referencePanel.transform;
                ResearchFlowVisualInput.SetHeadFacing(rect.TransformPoint(rect.rect.center)); yield return Wait(1.5);
                ReachRow(p.name, "turned_to_reader"); Capture("reach_" + p.name + "_b");
            }
            // Real presses from the plain standing pose: open with the ray, then every format tab.
            controller.CloseReference("reader_reach_sweep", "System"); yield return Settle();
            ResearchFlowVisualInput.WalkHeadTo(home); ResearchFlowVisualInput.SetHeadFacing(work.position); yield return Wait(.4);
            yield return RayClick("reference"); yield return Wait(1.5);
            var reader = (RectTransform)controller.view.referencePanel.transform;
            ResearchFlowVisualInput.SetHeadFacing(reader.TransformPoint(reader.rect.center)); yield return Wait(1);
            foreach (var section in ResearchReferenceScheme.Active)
            {
                string result = "ok";
                IEnumerator click = null;
                try { click = RayClick("reference_tab." + section); } catch (System.Exception e) { result = e.Message; }
                if (click != null) yield return click;
                yield return Wait(1.2);
                var viewer = controller.view.ReferenceViewer;
                ReachRows.Add(new { pose = "press_tabs", phase = section, bound = viewer.BoundSection, ready = viewer.IsReady,
                    visible = controller.view.IsReferenceActuallyVisible, result });
                Capture("reach_tab_" + section);
            }
            controller.CloseReference("reader_reach_sweep", "System"); yield return Settle();
            ResearchFlowVisualInput.WalkHeadTo(home);
            Detail = "READER_REACH " + ReachRows.Count;
        }
    }
}
#endif
