#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Owner 30 Oct log: in Task B the Reader opened at its Tutorial pose (18 m away, edge-on), was face-hidden and never re-placed.
    // Reopen the Reader from that stale pose under several gaze directions and require it to end visible near the wearer.
    public static partial class ResearchS1FlowDriver
    {
        public static string ReaderOpenSweep() => Start(ReaderOpenSweepRoutine(), 120);

        // Fraction of the Reader's outline that the Main panel's outline covers, in viewing angles from the eye.
        static float ViewOverlap(Transform head, RectTransform reader, RectTransform main)
        {
            if (!main.gameObject.activeInHierarchy) return 0;
            var r = new Vector3[4]; reader.GetWorldCorners(r);
            var m = new Vector3[4]; main.GetWorldCorners(m);
            var frame = Quaternion.Inverse(Quaternion.LookRotation(((r[0] + r[2]) * .5f + (m[0] + m[2]) * .5f) * .5f - head.position, Vector3.up));
            Rect Angular(Vector3[] corners)
            {
                var min = new Vector2(float.MaxValue, float.MaxValue); var max = -min;
                foreach (var corner in corners)
                {
                    var local = frame * (corner - head.position);
                    var angle = new Vector2(Mathf.Atan2(local.x, local.z), Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude));
                    min = Vector2.Min(min, angle); max = Vector2.Max(max, angle);
                }
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
            Rect a = Angular(r), b = Angular(m);
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w <= 0 || h <= 0 ? 0 : w * h / (a.width * a.height);
        }

        static IEnumerator ReaderOpenSweepRoutine()
        {
            Require(controller.Flow.IsTaskActive, "Reader open sweep requires an active task.");
            var station = controller.stations[controller.CurrentStationIndex];
            float eye = (float)controller.StationEyeHeight;
            var gazes = new (string name, Vector3 target)[]
            {
                ("level", station.position + station.forward * 2f + Vector3.up * eye),
                ("fan", station.position + station.forward * .7f + Vector3.up * .95f),
                ("fan_steep", station.position + station.forward * .45f + Vector3.up * .8f),
                ("left_bench", station.position + station.forward * .6f - station.right * .6f + Vector3.up * .9f),
                ("right_bench", station.position + station.forward * .6f + station.right * .6f + Vector3.up * .9f),
            };
            var hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            var faceHidden = typeof(ResearchPanelMotion).GetField("faceHidden", hidden);
            var results = new List<string>(); int failures = 0;
            ResearchFlowVisualInput.SetHeadMotion(Vector3.zero, 0);
            var main = controller.view.mainPanel;
            Pose? mainOverReader = null;
            for (int run = 0; run < gazes.Length + 1; run++)
            {
                var gaze = run < gazes.Length ? gazes[run] : (name: "main_moved_over_reader", target: gazes[0].target);
                // Owner photo 30 Oct: the wearer had dragged Main to where the Reader anchor sits; the Reader then opened on top of it.
                if (run == gazes.Length && mainOverReader.HasValue)
                { main.transform.SetPositionAndRotation(mainOverReader.Value.position, mainOverReader.Value.rotation); yield return Wait(.3); }
                controller.CloseReference("reader_open_sweep", "System"); yield return Settle();
                var panel = controller.view.referencePanel;
                panel.transform.SetPositionAndRotation(station.position + new Vector3(18, 1.6f, 0), Quaternion.LookRotation(station.right));
                ResearchFlowVisualInput.SetHeadFacing(gaze.target); yield return Wait(.3);
                controller.OpenReference("None"); yield return Wait(1.5);
                var head = controller.view.Head;
                bool visible = panel.HasAcceptedPose && !panel.PlacementBlocked && panel.GetComponent<Canvas>().enabled;
                bool near = Vector3.Distance(panel.transform.position, head.position) < 2.5f;
                float overlap = ViewOverlap(controller.view.Head, (RectTransform)panel.transform, (RectTransform)main.transform);
                bool clearOfMain = overlap < .02f;
                if (run == 0) mainOverReader = new Pose(panel.transform.position + Vector3.down * .25f + panel.transform.right * .45f, panel.transform.rotation);
                if (!visible || !near || !clearOfMain) failures++;
                results.Add(gaze.name + ":visible=" + visible + ",near=" + near + ",main_overlap=" + overlap.ToString("0.00") + ",accepted=" + panel.HasAcceptedPose + ",blocked=" + panel.PlacementBlocked
                    + ",face_hidden=" + faceHidden.GetValue(panel) + ",rejection=" + panel.LastRejection);
            }
            controller.CloseReference("reader_open_sweep", "System"); yield return Settle();
            System.IO.File.WriteAllText("Temp/ReaderOpenSweep.txt", string.Join("\n", results));
            Require(failures == 0, "READER_OPEN_SWEEP_FAIL " + failures + " | " + string.Join(" | ", results));
            Detail = "READER_OPEN_SWEEP_PASS " + string.Join(" | ", results);
        }
    }
}
#endif
