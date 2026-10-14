#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Isolated pose-pipeline regression. Native ray acquisition is covered separately by ResearchTechnicalInputChecks.
    public static class ResearchDragContinuityChecks
    {
        public static string Run(string evidenceDirectory = null)
        {
            var root = new GameObject("DragContinuityFixture");
            var obstacle = new GameObject("DragContinuityObstacle");
            var samples = new List<object>();
            int checks = 0;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
            try
            {
                var view = root.AddComponent<ResearchFlowView>();
                var head = new GameObject("Head").transform; head.SetParent(root.transform);
                head.position = new Vector3(30, 50, 30); view.Head = head;
                var slate = new GameObject("Slate", typeof(RectTransform), typeof(Canvas), typeof(ResearchPanelMotion));
                slate.transform.SetParent(root.transform);
                var panel = slate.GetComponent<ResearchPanelMotion>(); panel.view = view; panel.panelId = "main"; view.mainPanel = panel;
                // Match authored world-space Canvas units: 840 x 760 at .001 scale.
                // The old metre-sized rect depended on the removed oversized minimum envelope.
                ((RectTransform)slate.transform).sizeDelta = new Vector2(840, 760);
                slate.transform.localScale = Vector3.one * .001f;
                var slateCorners = new Vector3[4]; ((RectTransform)slate.transform).GetWorldCorners(slateCorners);
                Check(Mathf.Abs(Vector3.Distance(slateCorners[0], slateCorners[3]) - .84f) < .0001f
                    && Mathf.Abs(Vector3.Distance(slateCorners[0], slateCorners[1]) - .76f) < .0001f,
                    "Fixture has actual 0.84 x 0.76 metre Canvas bounds");
                var handle = slate.AddComponent<ResearchDragHandle>(); handle.panel = panel;
                var collider = obstacle.AddComponent<BoxCollider>(); collider.size = Vector3.one * .12f; obstacle.SetActive(false);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var begin = (Action<PointerEventData, Vector3>)typeof(ResearchDragHandle).GetMethod("Begin", flags).CreateDelegate(typeof(Action<PointerEventData, Vector3>), handle);
                var moveAt = (Action<Vector3, float>)typeof(ResearchDragHandle).GetMethod("MoveAt", flags).CreateDelegate(typeof(Action<Vector3, float>), handle);
                const float Frame = 1f / 72;
                Action<Vector3> move = point => moveAt(point, Frame);
                var remember = (Action)typeof(ResearchDragHandle).GetMethod("RememberClearPose", flags).CreateDelegate(typeof(Action), handle);
                var lateUpdate = (Action)typeof(ResearchDragHandle).GetMethod("LateUpdate", flags).CreateDelegate(typeof(Action), handle);
                var pointer = new PointerEventData(null) { pointerId = 731 };
                var start = head.position + new Vector3(-.3f, 0, 1.2f);
                panel.transform.SetPositionAndRotation(start, Quaternion.identity);
                Physics.SyncTransforms(); begin(pointer, start);
                var previous = start;
                int freezes = 0, jumps = 0;
                for (int i = 1; i <= 60; i++)
                {
                    var target = start + Vector3.right * (.01f * i);
                    bool blocked = i % 2 == 1;
                    obstacle.SetActive(blocked); obstacle.transform.position = target; Physics.SyncTransforms();
                    var rotation = panel.transform.rotation;
                    Check(panel.PlacementClear(target, rotation) != blocked, "Alternating obstacle fixture must really alternate placement validity");
                    int validations = panel.PlacementCalls;
                    move(target);
                    float step = Vector3.Distance(previous, panel.transform.position);
                    if (step < .001f) freezes++;
                    if (step > .015f) jumps++;
                    float error = Vector3.Distance(panel.transform.position, target);
                    samples.Add(new { sample = i, blocked, target = V(target), actual = V(panel.transform.position), step_metres = step, target_error_metres = error });
                    // 0.72 m/s target: the stabilized pose trails by a bounded, shrinking distance even while placement overlaps.
                    Check(error < .72f * .05f, "Every stabilized pose stays within 50 ms of the target even when placement temporarily overlaps");
                    Check(panel.PlacementCalls == validations, "Move must not call expensive placement validation");
                    if (i % 7 == 0) remember(); // Exercise the independent lower-cadence clearance path.
                    previous = panel.transform.position;
                }
                Check(freezes == 0 && jumps == 0 && handle.RejectedSamples == 0, "No rejected-frame freeze/jump on a uniform target sequence");
                Check(handle.FilterLatencyMilliseconds > 0 && handle.FilterLatencyMilliseconds < 30, "Added drag latency at 0.72 m/s is under 30 ms: " + handle.FilterLatencyMilliseconds);
                var filterProfile = FilterProfile(begin, moveAt, handle, panel, pointer, head, Check);
                panel.transform.SetPositionAndRotation(previous, Quaternion.LookRotation(Vector3.ProjectOnPlane(previous - head.position, Vector3.up)));
                begin(pointer, previous);
                int samplesBeforeInvalid = handle.MoveSamples;
                var beforeInvalid = panel.transform.position;
                move(new Vector3(float.NaN, 0, 0)); move(new Vector3(float.PositiveInfinity, 0, 0)); move(head.position);
                Check(handle.MoveSamples == samplesBeforeInvalid + 3 && handle.RejectedSamples == 3 && panel.transform.position == beforeInvalid, "Malformed/degenerate poses are rejected without corrupting transform");
                handle.OnEndDrag(pointer);
                Check(!handle.IsDragging && !handle.ReleaseRecovered && panel.PlacementClear(panel.transform.position, panel.transform.rotation), "Clear release preserves pose");
                var measuredSequence = new { handle.MoveSamples, handle.RejectedSamples, handle.MoveMilliseconds, handle.MaxMoveMilliseconds,
                    panel.PlacementCalls, panel.PlacementRejections, panel.PlacementMilliseconds };

                // Bounded diagnostic: keep the ray's world point still while the head translates laterally.
                // Position must stay still; record yaw coupling so an orientation fix can be reviewed from a repro.
                obstacle.SetActive(false); Physics.SyncTransforms();
                var fixedHead = head.position;
                var fixedTarget = fixedHead + new Vector3(-.3f, 0, 1.2f);
                var fixedRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(fixedTarget - fixedHead, Vector3.up));
                panel.transform.SetPositionAndRotation(fixedTarget, fixedRotation); begin(pointer, fixedTarget);
                var headSamples = new List<object>();
                float maxPositionError = 0, maxYawDegrees = 0;
                for (int i = 1; i <= 20; i++)
                {
                    head.position = fixedHead + Vector3.right * (.01f * i);
                    move(fixedTarget);
                    float positionError = Vector3.Distance(panel.transform.position, fixedTarget);
                    float yawDegrees = Quaternion.Angle(fixedRotation, panel.transform.rotation);
                    maxPositionError = Mathf.Max(maxPositionError, positionError); maxYawDegrees = Mathf.Max(maxYawDegrees, yawDegrees);
                    headSamples.Add(new { frame = i, head = V(head.position), fixed_target = V(fixedTarget),
                        panel_position = V(panel.transform.position), position_error_metres = positionError, yaw_change_degrees = yawDegrees });
                }
                Check(maxPositionError < .001f, "Stationary ray target keeps panel position still under lateral head motion");
                Check(maxYawDegrees < .01f, "Active drag preserves acquired orientation under lateral head motion: " + maxYawDegrees);
                var headCoupling = new { frames = 20, head_translation_metres = .2f, max_position_error_metres = maxPositionError,
                    max_yaw_change_degrees = maxYawDegrees, yaw_changes_with_head = maxYawDegrees > .1f, samples = headSamples };
                handle.OnEndDrag(pointer); head.position = fixedHead;

                // A remembered pose is newer than the initial pose; recovery must use it and recheck moving obstacles.
                start = head.position + new Vector3(-.65f, 0, 1.2f);
                var remembered = start + Vector3.up * .15f;
                var invalid = head.position + new Vector3(.65f, 0, 1.2f);
                for (int ending = 0; ending < 4; ending++)
                {
                    obstacle.SetActive(false); Physics.SyncTransforms(); handle.enabled = true;
                    panel.transform.SetPositionAndRotation(start, Quaternion.identity); begin(pointer, start);
                    move(remembered); remember();
                    obstacle.transform.position = invalid; obstacle.SetActive(true); Physics.SyncTransforms();
                    move(invalid);
                    if (ending == 0) handle.OnEndDrag(pointer);
                    else if (ending == 1)
                    {
                        handle.enabled = false;
                        // Ordinary MonoBehaviours do not receive runtime lifecycle callbacks in Edit Mode.
                        if (!Application.isPlaying) typeof(ResearchDragHandle).GetMethod("OnDisable", flags).Invoke(handle, null);
                    }
                    else if (ending == 2)
                    {
                        // Simulate the native ray being destroyed/lost after an acquired drag.
                        typeof(ResearchDragHandle).GetField("nativeDrag", flags).SetValue(handle, true); lateUpdate();
                    }
                    else
                    {
                        // Both old and current poses become obstructed. Recovery must recenter or hide safely.
                        collider.size = Vector3.one * 8; obstacle.transform.position = head.position; Physics.SyncTransforms();
                        handle.OnEndDrag(pointer);
                    }
                    Check(!handle.IsDragging && handle.ReleaseRecovered, "Release/disable/lost native ray/changed environment ends drag with recovery: ending=" + ending +
                        ", dragging=" + handle.IsDragging + ", recovered=" + handle.ReleaseRecovered);
                    if (ending < 3) Check(Vector3.Distance(panel.transform.position, remembered) < .0001f, "Recovery uses latest verified pose, not initial pose");
                    Check(panel.PlacementClear(panel.transform.position, panel.transform.rotation) ||
                        (panel.PlacementBlocked && !slate.GetComponent<Canvas>().enabled), "No permanent visible clipping after end/cancel");
                }
                var path = Path.Combine(Path.GetFullPath(evidenceDirectory ??
                    ("Logs/QuestFinalIntegration/interaction-checks/drag-continuity-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture))), "drag-continuity.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(new
                {
                    passed = true, checks, freezes, jumps, hardware_verified = false,
                    input_evidence = "Synthetic direct pose pipeline; no native ray acquisition, Quest frame pacing or comfort evidence.",
                    temporary_held_overlap = true, invalid_release_behavior = "restore revalidated last clear pose, otherwise recenter or hide",
                    filter = "OneEuro(2.5 Hz, beta 6, dcutoff 1 Hz) on panel target", filterProfile, measuredSequence, headCoupling, samples
                }, Formatting.Indented));
                return "DRAG_CONTINUITY_PASS checks=" + checks + "; " + path;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(obstacle); Physics.SyncTransforms();
            }
        }
        static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };

        // Held-still jitter must shrink; slow, medium and fast drags must follow with bounded lag.
        static object FilterProfile(Action<PointerEventData, Vector3> begin, Action<Vector3, float> moveAt, ResearchDragHandle handle,
            ResearchPanelMotion panel, PointerEventData pointer, Transform head, Action<bool, string> check)
        {
            const float Frame = 1f / 72;
            var start = head.position + new Vector3(-.3f, 0, 1.2f);
            var random = new System.Random(7);
            float Gauss() => (float)(Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble()));
            panel.transform.SetPositionAndRotation(start, Quaternion.identity); begin(pointer, start);
            double inSquares = 0, outSquares = 0;
            for (int i = 0; i < 288; i++)
            {
                var noise = new Vector3(Gauss(), Gauss(), 0) * .002f;
                moveAt(start + noise, Frame);
                if (i < 72) continue;
                inSquares += noise.sqrMagnitude; outSquares += (panel.transform.position - start).sqrMagnitude;
            }
            double jitterRatio = Math.Sqrt(outSquares / inSquares);
            check(jitterRatio < .5, "Held-still 2 mm tracking jitter is at least halved: " + jitterRatio);
            var lags = new List<object>();
            foreach (float speed in new[] { .1f, .5f, 3f })
            {
                panel.transform.SetPositionAndRotation(start, Quaternion.identity); begin(pointer, start);
                var target = start; float lagAt100 = 0, lagEnd = 0;
                for (int i = 1; i <= 36; i++)
                {
                    target += Vector3.right * speed * Frame; moveAt(target, Frame);
                    float lag = Vector3.Distance(panel.transform.position, target) / speed * 1000;
                    if (i == 7) lagAt100 = lag;
                    lagEnd = lag;
                }
                check(lagAt100 < 60 && lagEnd < 50 && (speed < 1 || lagEnd < 12), "Drag at " + speed + " m/s follows without 100 ms lag: " + lagAt100 + "/" + lagEnd);
                lags.Add(new { speed_mps = speed, lag_ms_after_100ms = lagAt100, lag_ms_after_500ms = lagEnd });
            }
            return new { still_jitter_ratio = jitterRatio, lags };
        }
    }
}
#endif
