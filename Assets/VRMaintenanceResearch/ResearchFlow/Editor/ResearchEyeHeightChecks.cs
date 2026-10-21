#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Passive Play-mode watcher beside the real flow driver (master §10 eye heights 1.0/1.65 m, §13 Spatial QA). At each participant
    // stage it measures every visible panel: all four corners inside [.02, .98] of each eye's viewport with the head turned to the panel,
    // eye-to-corner/centre sightlines through scene colliders, panel/collider overlap and participant buttons. Without XR stereo the
    // eyes are the Editor mono camera offset 32 mm along head.right. Editor geometry only: not Quest or wearer evidence.
    public static class ResearchEyeHeightChecks
    {
        const float HalfIpd = .032f, Margin = .02f;
        const int MaxSamples = 5;
        static readonly string[] CornerNames = { "bottom_left", "top_left", "top_right", "bottom_right", "centre" };

        sealed class Stage
        {
            public string stage, capture, worst; public double first_seen_s; public bool pass = true; public float? worst_margin;
            public readonly SortedSet<string> failures = new SortedSet<string>();
            public readonly List<object> samples = new List<object>();
        }
        struct Eye { public string name; public Vector3 offset; public Matrix4x4 projection; }

        static ResearchSessionFlowController controller;
        static string directory, label, watcherError;
        static float requestedEye;
        static double started;
        static readonly Dictionary<string, Stage> stages = new Dictionary<string, Stage>();
        static readonly Dictionary<string, double> since = new Dictionary<string, double>(), due = new Dictionary<string, double>();

        public static string Watch(string evidenceDirectory, string runLabel, float eyeHeight)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>() ?? throw new InvalidOperationException("No flow controller.");
            directory = Path.GetFullPath(evidenceDirectory);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Fresh evidence directory required: " + directory);
            Directory.CreateDirectory(directory);
            var camera = controller.view.Head.GetComponent<Camera>();
            if (!camera.stereoEnabled)
            {
                var probe = camera.transform.position + camera.transform.rotation * new Vector3(.3f, -.2f, 2);
                Vector3 expected = camera.WorldToViewportPoint(probe), actual = Viewport(camera.projectionMatrix, camera.transform.position, camera.transform.rotation, probe);
                if ((expected - actual).magnitude > 1e-3f) throw new InvalidOperationException("Eye viewport model disagrees with Camera.WorldToViewportPoint: " + expected + " vs " + actual);
            }
            label = runLabel; requestedEye = eyeHeight; started = EditorApplication.timeSinceStartup; watcherError = null;
            stages.Clear(); since.Clear(); due.Clear();
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            return "watching " + label + " in " + directory;
        }

        public static string Finish()
        {
            EditorApplication.update -= Tick;
            if (directory == null) throw new InvalidOperationException("Watch was not started.");
            var camera = controller && controller.view && controller.view.Head ? controller.view.Head.GetComponent<Camera>() : null;
            var ordered = stages.Values.OrderBy(s => s.first_seen_s).ToArray();
            string status = watcherError != null ? "ERROR" : ordered.Length == 0 ? "NO_STAGES" : ordered.All(s => s.pass) ? "PASS" : "FAIL";
            string path = Path.Combine(directory, "eye-height-" + label + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new
            {
                label, status, requested_eye_height_m = requestedEye, watcher_error = watcherError, utc = DateTime.UtcNow.ToString("o"),
                anchors_version = ResearchStationAnchors.Version, ui_policy = ResearchUIPolicy.Active, layout_version = ResearchUIPolicy.LayoutVersion,
                camera = camera ? new { camera.fieldOfView, camera.aspect, camera.pixelWidth, camera.pixelHeight, camera.stereoEnabled } : null,
                eye_model = camera && camera.stereoEnabled ? "XR stereo view/projection matrices"
                    : "Editor mono Camera.projectionMatrix; left/right eye = head -/+ 32 mm along head.right (no native stereo in Editor Play)",
                criteria = "Judged with the head turned to each panel centre: every corner inside [0.02, 0.98] for both eyes, no scene collider on any eye->corner/centre sightline, "
                    + "no scene collider inside the panel rect, every interactable participant button inside both viewports, no concealed (PlacementBlocked) panel. "
                    + "The as-is head pose is recorded but not judged. Colliders under the UI root or the XR origin are ignored.",
                capture_type = "Game View (ScreenCapture, Editor mono camera)",
                stages = ordered.Select(s => new { s.stage, s.pass, s.worst, s.worst_margin, s.failures, s.capture, s.first_seen_s, s.samples })
            }, Formatting.Indented));
            return status + " " + path + " | " + string.Join(" | ", ordered.Select(s => s.stage + ":" + (s.pass ? "pass" : "FAIL") + " worst=" + s.worst
                + (s.pass ? "" : " first_failure=" + s.failures.First())));
        }

        // Fixture head turn: a participant looks at a card before pressing it. The 60 deg Editor camera otherwise leaves the anchored
        // Task card outside the view, where production correctly withholds its Pause.
        public static string FacePanel(string panelId)
        {
            var panel = controller.view.GetComponentsInChildren<ResearchPanelMotion>(false).Single(p => p.panelId == panelId);
            var rect = (RectTransform)panel.transform;
            ResearchFlowVisualInput.SetHeadFacing(rect.TransformPoint(rect.rect.center));
            return "facing " + panelId;
        }

        public static string ResetHead() { ResearchFlowVisualInput.SetHeadMotion(Vector3.zero, 0); return "head reset"; }

        static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || !controller) { Finish(); return; }
                double now = EditorApplication.timeSinceStartup;
                var current = CurrentStages().ToList();
                foreach (var key in since.Keys.Where(k => !current.Contains(k)).ToList()) since.Remove(key);
                foreach (var key in current)
                {
                    if (!since.ContainsKey(key)) since[key] = now;
                    if (stages.TryGetValue(key, out var stage) && stage.samples.Count >= MaxSamples) continue;
                    // Panels settle within ResearchMotion.State (.24 s); Inspect feedback is dismissed by the driver within ~.3 s.
                    double settle = key.EndsWith("_inspect_feedback", StringComparison.Ordinal) ? .1 : .35;
                    if (now - since[key] < settle || (due.TryGetValue(key, out double next) && now < next)) continue;
                    due[key] = now + 1;
                    Sample(key, now);
                }
            }
            catch (Exception error) { watcherError = error.ToString(); EditorApplication.update -= Tick; Debug.LogError("ResearchEyeHeightChecks stopped: " + error); }
        }

        static IEnumerable<string> CurrentStages()
        {
            var flow = controller.Flow; var view = controller.view;
            if (flow == null || !view || controller.Transitioning || !string.IsNullOrEmpty(controller.Error)) yield break;
            string task = flow.IsTaskActive ? "task" + (flow.State == ResearchFlowState.Task1Active ? 1 : 2) + "_" + flow.CurrentTask : null;
            bool betweenTasks = flow.State == ResearchFlowState.PostTask1Checkpoint || flow.State == ResearchFlowState.PostTask2Checkpoint
                || flow.State == ResearchFlowState.Task1Ready || flow.State == ResearchFlowState.Task2Ready;
            string prefix = task ?? flow.State.ToString();
            // The feedback flag stays set through the checkpoint that follows it; only an active task shows the feedback card.
            if (task != null && controller.InspectionFeedbackVisible) yield return task + "_inspect_feedback";
            else if (task != null)
                yield return task + (flow.Paused ? "_pause_" + controller.PauseKind : !controller.ReferenceOpen ? "_card"
                    : controller.ReferenceMinimized ? "_reader_minimized" : "_reader");
            else if (betweenTasks) yield return prefix;
            if ((task != null || betweenTasks) && view.RecoveryCanvas && view.RecoveryCanvas.gameObject.activeInHierarchy) yield return prefix + "_offview_strip";
        }

        static void Sample(string key, double now)
        {
            if (!stages.TryGetValue(key, out var stage))
            {
                stages[key] = stage = new Stage { stage = key, first_seen_s = now - started };
                stage.capture = Path.Combine(directory, stages.Count.ToString("00") + "_" + key + ".png");
                ScreenCapture.CaptureScreenshot(stage.capture);
            }
            var view = controller.view; var head = view.Head; var camera = head.GetComponent<Camera>();
            var eyes = Eyes(camera);
            var panels = view.GetComponentsInChildren<ResearchPanelMotion>(false)
                .Where(p => p.PlacementBlocked || (p.TryGetComponent<Canvas>(out var c) && c.enabled && (!p.TryGetComponent<CanvasGroup>(out var g) || g.alpha > .01f)))
                .Select(p => Measure(p, head, eyes, stage)).ToArray();
            stage.samples.Add(new
            {
                t_s = now - started, state = controller.Flow.State.ToString(), paused = controller.Flow.Paused, pause_kind = controller.PauseKind,
                head = XYZ(head.position), head_forward = XYZ(head.forward), station = controller.CurrentStationIndex,
                station_eye_height_m = controller.StationEyeHeight, applied_anchor_eye_height_m = ResearchStationAnchors.ClampEyeHeight(controller.StationEyeHeight),
                station_floor_y = controller.CurrentStationIndex >= 0 && controller.CurrentStationIndex < controller.stations.Length ? controller.stations[controller.CurrentStationIndex].position.y : float.NaN,
                panels
            });
        }

        static object Measure(ResearchPanelMotion panel, Transform head, Eye[] eyes, Stage stage)
        {
            string id = panel.panelId;
            if (panel.PlacementBlocked)
            {
                Fail(stage, id + ": concealed, no clear placement (" + panel.LastRejection + ")");
                return new { panel = id, concealed = true, last_rejection = panel.LastRejection, anchor_candidates = AnchorCandidates(panel),
                    head_relative_candidates = HeadRelativeCandidates(panel, head, eyes) };
            }
            var rect = (RectTransform)panel.transform; var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Concat(new[] { (corners[0] + corners[2]) / 2 }).ToArray();
            var centre = points[4];
            var facing = Quaternion.LookRotation(centre - head.position, Vector3.up);
            var viewports = new List<object>();
            foreach (var (pose, rotation) in new[] { ("as_is", head.rotation), ("facing", facing) })
                foreach (var eye in eyes)
                {
                    var origin = head.position + rotation * eye.offset;
                    var projected = corners.Select(c => Viewport(eye.projection, origin, rotation, c)).ToArray();
                    var margins = projected.Select(MarginOf).ToArray();
                    int worst = Array.IndexOf(margins, margins.Min());
                    if (pose == "facing")
                    {
                        string where = id + " " + eye.name + " " + CornerNames[worst] + " (" + projected[worst].x.ToString("0.000") + "," + projected[worst].y.ToString("0.000") + ")";
                        if (stage.worst_margin == null || margins[worst] < stage.worst_margin) { stage.worst_margin = margins[worst]; stage.worst = where; }
                        if (margins[worst] < 0) Fail(stage, where + " outside viewport");
                    }
                    viewports.Add(new { pose, eye = eye.name, eye_position = XYZ(origin), corners = projected.Select(XYZ).ToArray(), min_margin = margins[worst],
                        worst_corner = CornerNames[worst], inside = margins[worst] >= 0 });
                }
            var blocked = new List<object>();
            foreach (var eye in eyes)
            {
                var origin = head.position + facing * eye.offset;
                for (int i = 0; i < points.Length; i++)
                {
                    string blocker = Blocker(origin, points[i], panel.view.transform);
                    if (blocker == null) continue;
                    blocked.Add(new { eye = eye.name, point = CornerNames[i], blocker });
                    Fail(stage, id + " " + eye.name + " sightline to " + CornerNames[i] + " blocked by " + blocker);
                }
            }
            var size = new Vector2(Vector3.Distance(corners[0], corners[3]), Vector3.Distance(corners[0], corners[1]));
            var overlaps = Physics.OverlapBox(centre, new Vector3(size.x / 2, size.y / 2, .01f), rect.rotation, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => SceneCollider(c, panel.view.transform)).Select(c => PathOf(c.transform)).ToArray();
            foreach (var overlap in overlaps) Fail(stage, id + " rect intersects " + overlap);
            var buttons = panel.GetComponentsInChildren<ResearchUIAction>(false).Where(a => a.TryGetComponent<Button>(out var b) && b.IsInteractable()).Select(a =>
            {
                var buttonCorners = new Vector3[4]; ((RectTransform)a.transform).GetWorldCorners(buttonCorners);
                var perEye = eyes.Select(e => buttonCorners.Min(c => MarginOf(Viewport(e.projection, head.position + facing * e.offset, facing, c)))).ToArray();
                for (int e = 0; e < eyes.Length; e++) if (perEye[e] < 0) Fail(stage, id + " button " + a.command + " outside " + eyes[e].name + " viewport");
                return new { a.command, min_margin_by_eye = perEye };
            }).ToArray();
            float distance = Vector3.Distance(head.position, centre);
            return new
            {
                panel = id, size_m = new[] { size.x, size.y }, world_corners = corners.Select(XYZ).ToArray(), centre = XYZ(centre), distance_m = distance,
                elevation_deg = Mathf.Asin(Mathf.Clamp((centre.y - head.position.y) / distance, -1, 1)) * Mathf.Rad2Deg, bottom_edge_y = Mathf.Min(corners[0].y, corners[3].y),
                production_readable_from_head = panel.IsReadableFromHead, viewports, sightlines_tested = eyes.Length * points.Length, blocked_sightlines = blocked,
                rect_overlaps = overlaps, buttons
            };
        }

        // Why a concealed working slate found no station anchor: every candidate through the production clearance query.
        static object[] AnchorCandidates(ResearchPanelMotion panel)
        {
            int index = controller.CurrentStationIndex;
            if (panel.centered || !ResearchStationAnchors.Supports(index, panel.panelId) || index >= controller.stations.Length) return null;
            var station = controller.stations[index]; var rows = new List<object>();
            for (int i = 0; i < ResearchStationAnchors.CandidateCount(index, panel.panelId); i++)
            {
                if (!ResearchStationAnchors.TryGet(index, controller.Record?.task_a_layout, panel.panelId, station.position.x, station.position.y, station.position.z,
                    station.eulerAngles.y, controller.StationEyeHeight, i, out var pose)) continue;
                var position = new Vector3((float)pose.X, (float)pose.Y, (float)pose.Z);
                bool clear = panel.PlacementClear(position, Quaternion.Euler(0, (float)pose.YawDegrees, 0));
                rows.Add(new { pose.Id, position = XYZ(position), yaw_deg = pose.YawDegrees, clear, rejection = clear ? null : panel.LastRejection });
            }
            return rows.ToArray();
        }

        // Why a concealed centred card found no pose: the head-relative grid of ResearchPanelMotion.ApplyRecenter (UI/ResearchPanelMotion.cs:206-228,
        // centred branch, participant's right) through the production clearance query, with each pose's Editor-viewport margin and angular extent.
        static object[] HeadRelativeCandidates(ResearchPanelMotion panel, Transform head, Eye[] eyes)
        {
            if (!panel.centered) return null;
            bool recovery = panel.panelId == "recovery";
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            var rect = ((RectTransform)panel.transform).rect;
            var scale = (panel.transform.parent ? panel.transform.parent.lossyScale : Vector3.one) * .001f;
            var rows = new List<object>();
            foreach (float angle in new[] { 0f, 28f, 32f, 35f, 55f, 75f })
                foreach (float distance in recovery ? new[] { .9f, 1.05f, 1.2f } : new[] { 1.2f, 1.4f, 1.6f, 1.8f, 2f, 2.2f })
                    foreach (float height in recovery ? new[] { -.25f, -.4f, .2f } : new[] { -.1f, .2f })
                    {
                        var direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                        var rotation = Quaternion.LookRotation(direction, Vector3.up);
                        var position = head.position + direction * distance + Vector3.up * height;
                        var corners = new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin) }
                            .Select(c => position + rotation * Vector3.Scale(c, scale)).ToArray();
                        var local = corners.Select(c => Quaternion.Inverse(head.rotation) * (c - head.position)).ToArray();
                        bool clear = panel.PlacementClear(position, rotation);
                        rows.Add(new
                        {
                            angle_deg = angle, distance_m = distance, height_m = height, clear, rejection = clear ? null : panel.LastRejection,
                            editor_viewport_margin = eyes.Min(e => corners.Min(c => MarginOf(Viewport(e.projection, head.position + head.rotation * e.offset, head.rotation, c)))),
                            max_abs_vertical_deg = local.Max(l => Mathf.Abs(Mathf.Atan2(l.y, l.z) * Mathf.Rad2Deg)),
                            max_abs_horizontal_deg = local.Max(l => Mathf.Abs(Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg))
                        });
                    }
            return rows.ToArray();
        }

        static Eye[] Eyes(Camera camera) => camera.stereoEnabled
            ? new[] { Camera.StereoscopicEye.Left, Camera.StereoscopicEye.Right }.Select(e => new Eye { name = e.ToString(),
                offset = camera.transform.InverseTransformPoint(camera.GetStereoViewMatrix(e).inverse.MultiplyPoint(Vector3.zero)), projection = camera.GetStereoProjectionMatrix(e) }).ToArray()
            : new[] { new Eye { name = "Left", offset = Vector3.left * HalfIpd, projection = camera.projectionMatrix },
                new Eye { name = "Right", offset = Vector3.right * HalfIpd, projection = camera.projectionMatrix } };

        // Same mapping as Camera.WorldToViewportPoint for an eye at `eye` looking along `rotation` (Unity view space looks down -Z).
        static Vector3 Viewport(Matrix4x4 projection, Vector3 eye, Quaternion rotation, Vector3 world)
        {
            var clip = projection * (Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(eye, rotation, Vector3.one).inverse) * new Vector4(world.x, world.y, world.z, 1);
            return new Vector3(clip.x / clip.w * .5f + .5f, clip.y / clip.w * .5f + .5f, clip.w);
        }

        static float MarginOf(Vector3 v) => float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z) || v.z <= 0 ? -1
            : Mathf.Min(Mathf.Min(v.x - Margin, 1 - Margin - v.x), Mathf.Min(v.y - Margin, 1 - Margin - v.y));

        static string Blocker(Vector3 from, Vector3 to, Transform ui)
        {
            var sight = to - from; float length = sight.magnitude - .02f;
            if (length <= 0) return null;
            return Physics.RaycastAll(from, sight.normalized, length, ~0, QueryTriggerInteraction.Ignore).Where(h => SceneCollider(h.collider, ui))
                .OrderBy(h => h.distance).Select(h => PathOf(h.collider.transform) + " [" + h.collider.GetType().Name + "] at " + h.distance.ToString("0.00") + " m").FirstOrDefault();
        }

        static bool SceneCollider(Collider collider, Transform ui) => !collider.transform.IsChildOf(ui) && !collider.GetComponentInParent<XROrigin>();

        static string PathOf(Transform t)
        {
            string path = t.name;
            for (var parent = t.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }

        // Sweeps head-relative poses for the current Main card with the production view and clearance predicates, restoring its pose.
        // Read-only evidence for choosing seated-eye fallbacks; writes one JSON row per pose.
        public static string SweepMainCardPoses(string path)
        {
            var c = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>() ?? throw new InvalidOperationException("No flow controller");
            var panel = c.view.mainPanel; var head = c.view.Head;
            var fits = typeof(ResearchPanelMotion).GetMethod("FitsView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            var start = new Pose(panel.transform.position, panel.transform.rotation);
            var rows = new List<object>(); int ok = 0;
            try
            {
                foreach (float angle in new[] { -75f, -55f, -35f, -20f, 0f, 20f, 35f, 55f, 75f })
                    foreach (float distance in new[] { .6f, .7f, .8f, .9f, 1f, 1.2f, 1.4f, 1.6f })
                        foreach (float height in new[] { -.1f, 0f, .1f, .2f, .3f, .4f, .6f })
                        {
                            var direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                            var rotation = Quaternion.LookRotation(direction, Vector3.up);
                            var position = head.position + direction * distance + Vector3.up * height;
                            bool view = (bool)fits.Invoke(panel, new object[] { position, rotation, 1f });
                            bool clear = panel.PlacementClear(position, rotation);
                            if (view && clear) ok++;
                            rows.Add(new { angle, distance, height, view, clear, rejection = panel.LastRejection });
                        }
            }
            finally { panel.transform.SetPositionAndRotation(start.position, start.rotation); }
            File.WriteAllText(path, JsonConvert.SerializeObject(new { state = c.Flow.State.ToString(), head = XYZ(head.position), centered = panel.centered,
                station = c.Flow.CurrentTask, ok, rows }, Formatting.Indented));
            return "SWEEP ok=" + ok + "/" + rows.Count + " " + path;
        }

        static void Fail(Stage stage, string failure) { stage.pass = false; stage.failures.Add(failure); }
        static float[] XYZ(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
#endif
