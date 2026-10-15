#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchMovementChecks
    {
        static ResearchSessionFlowController active;
        static string evidence;
        static int stage;
        static double stageStarted;
        static bool ownedInput;
        static Vector3 savedOriginPosition;
        static Quaternion savedOriginRotation;
        static string savedRegistryRoot;
        static readonly Dictionary<string, object> savedProperties = new Dictionary<string, object>();
        static string firstMovement, firstEvents, secondMovement, secondEvents, firstRun;
        static long firstLength;
        static bool sawWriterError;
        static bool focusRestored;
        static readonly List<object> checks = new List<object>();
        public static bool Running => active;

        /// <summary>Explicit isolated synthetic lifecycle; caller owns entering/exiting Play Mode.</summary>
        public static string BeginLifecycle(string evidenceDirectory)
        {
            if (Running || !EditorApplication.isPlaying || EditorApplication.isPaused) throw new InvalidOperationException("Unpaused Play Mode and no running movement check required.");
            active = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            if (!active || !active.origin || active.Record != null && active.Record.session_state == RegistryState.InProgress)
            { active = null; throw new InvalidOperationException("Canonical controller must have no active session; existing user session is preserved."); }
            if (ResearchFlowVisualInput.Running) { active = null; throw new InvalidOperationException("Stop the caller-owned visual fixture before this isolated test."); }
            evidence = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(evidence);
            savedProperties.Clear(); checks.Clear();
            foreach (string property in new[] { "Registry", "Record", "Flow", "Logger", "Error", "TechnicalSelfTest", "Transitioning" })
                savedProperties[property] = active.GetType().GetProperty(property).GetValue(active);
            savedRegistryRoot = active.registryRoot; savedOriginPosition = active.origin.transform.position; savedOriginRotation = active.origin.transform.rotation;
            stage = 0; sawWriterError = false; ownedInput = false; focusRestored = false;
            try
            {
                active.registryRoot = Path.Combine(evidence, "registry");
                Set("Registry", new ResearchParticipantRegistry(active.registryRoot)); Set("Record", null); Set("Flow", null); Set("Logger", null); Set("Error", "");
                StartFresh();
                ResearchFlowVisualInput.Begin(1.65f); ownedInput = true;
                stageStarted = EditorApplication.timeSinceStartup;
                EditorApplication.update += LifecycleTick;
                AssemblyReloadEvents.beforeAssemblyReload += CancelLifecycle;
                return "MOVEMENT_LIFECYCLE_STARTED " + evidence + "; synthetic InputSystem/native SDK paths, not wearer evidence";
            }
            catch { Cleanup(); throw; }
        }

        static void Set(string name, object value) => active.GetType().GetProperty(name).GetSetMethod(true).Invoke(active, new[] { value });
        static void StartFresh()
        {
            var record = active.Registry.CreateNext(ParticipantGroup.Dev);
            active.StartSession(record.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller, TaskOrder.AB);
            if (active.Logger == null || active.Record == null || !string.IsNullOrEmpty(active.Error)) throw new InvalidOperationException("Actual StartSession failed: " + active.Error);
            if (stage == 0) { firstMovement = active.Logger.MovementPath; firstEvents = active.Logger.EventsPath; firstRun = active.Record.run_id; }
            else { secondMovement = active.Logger.MovementPath; secondEvents = active.Logger.EventsPath; }
        }
        static void Next() { stage++; stageStarted = EditorApplication.timeSinceStartup; }
        static void FixtureTask(ResearchFlowState state)
        {
            // Task progression is deliberately injected only into a fresh DEV fixture; no research approval or taught action is invented.
            Set("Flow", new ResearchSessionFlow(TaskOrder.AB, state, 5));
            active.Log("MOVEMENT_FIXTURE_STATE", "test.movement", "SyntheticTaskBoundary", after: new { state = state.ToString() }, source: "Tester");
            if (!active.Command("task_start", "SimulatedController") || !active.Command("task_start_confirm", "SimulatedController")) throw new InvalidOperationException("Real task_start did not accept isolated technical state.");
        }
        static void LifecycleTick()
        {
            if (!active || !EditorApplication.isPlaying) { CancelLifecycle(); return; }
            try
            {
                double elapsed = EditorApplication.timeSinceStartup - stageStarted;
                if (ownedInput && stage != 6)
                {
                    float x = stage == 1 ? elapsed < .25 ? 0 : elapsed < .5 ? .1f : .2f : (float)Math.Sin(elapsed * 2) * .18f;
                    ResearchFlowVisualInput.SetHeadMotion(new Vector3(x, 0, 0), stage == 1 ? 0 : (float)Math.Sin(elapsed) * 12);
                }
                switch (stage)
                {
                    case 0:
                        if (elapsed < 1.2 || active.Transitioning) return;
                        Check(Read(firstMovement).Length > 6, "Scheduled rows in actual Tutorial lifecycle", ">6", Read(firstMovement).Length - 1);
                        FixtureTask(ResearchFlowState.Task1Ready); Next();
                        active.Log("MOVEMENT_FIXTURE_TRAJECTORY_START", "test.movement", "SyntheticControlled", after: new {
                            tracking_space_x_steps_metres = new[] { 0f, .1f, .2f }, hold_seconds = .25, expected_endpoint_distance_metres = .2,
                            analysis_settle_seconds = .12, no_origin_relocation = true }, source: "Tester"); break;
                    case 1:
                        if (elapsed < .9) return;
                        active.Log("MOVEMENT_FIXTURE_TRAJECTORY_END", "test.movement", "SyntheticControlled", source: "Tester");
                        ResearchFlowVisualInput.SetControllerTracked(0, false); Next(); break;
                    case 2:
                        if (elapsed < .5) return;
                        ResearchFlowVisualInput.SetControllerTracked(0, true);
                        if (!active.Command("pause", "SimulatedController")) throw new InvalidOperationException("Pause did not accept.");
                        Next(); break;
                    case 3:
                        if (elapsed < .5) return;
                        active.ResearcherResume();
                        if (active.Flow.Paused) throw new InvalidOperationException("Resume remained paused.");
                        typeof(ResearchMovementSampler).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(active.GetComponent<ResearchMovementSampler>(), new object[] { false });
                        active.Log("MOVEMENT_FIXTURE_FOCUS", "test.movement", "SyntheticFocusLoss", source: "Tester");
                        active.GetComponent<ResearchMovementSampler>().Boundary("synthetic_snap_turn");
                        active.origin.transform.Rotate(Vector3.up, 45, Space.World);
                        active.Log("SNAP_TURN", "test.movement", "SyntheticCommitted", source: "Tester");
                        Next(); break;
                    case 4:
                        if (!focusRestored && elapsed >= .25)
                        {
                            typeof(ResearchMovementSampler).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                                .Invoke(active.GetComponent<ResearchMovementSampler>(), new object[] { true });
                            focusRestored = true;
                        }
                        if (elapsed < .5) return;
                        active.GetComponent<ResearchMovementSampler>().Boundary("synthetic_teleport");
                        active.origin.transform.position += Vector3.right * 4;
                        active.Log("TELEPORT_COMPLETED", "test.movement", "SyntheticCommitted", source: "Tester");
                        FixtureTask(ResearchFlowState.Task2Ready); Next(); break;
                    case 5:
                        if (elapsed < .7) return;
                        active.ResearcherAbort(); firstLength = new FileInfo(firstMovement).Length;
                        Set("Record", null); Set("Flow", null); Set("Logger", null); Set("Error", "");
                        stage = 6; stageStarted = EditorApplication.timeSinceStartup; StartFresh(); break;
                    case 6:
                        if (elapsed < .8 || active.Transitioning) return;
                        var writer = (StreamWriter)typeof(HandsLabLogger).GetField("movementWriter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(active.Logger);
                        writer.Dispose(); Next(); break;
                    case 7:
                        if (elapsed < .4) return;
                        sawWriterError = active.Logger.State == HandsLabLogState.WriteError && !string.IsNullOrEmpty(active.Error);
                        active.ResearcherAbort(); VerifyLifecycle(); Cleanup(); break;
                }
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(evidence, "movement-lifecycle-result.json"), JsonConvert.SerializeObject(new { status = "FAIL", stage, error = error.ToString(), checks, hardware_verified = false }, Formatting.Indented));
                Debug.LogError("MOVEMENT_LIFECYCLE_FAIL " + error); Cleanup();
            }
        }
        static string[] Read(string path)
        {
            using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                return reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
        static void Check(bool pass, string description, object expected, object actual)
        { checks.Add(new { description, expected, actual, pass }); if (!pass) throw new InvalidOperationException(description + " expected=" + expected + " actual=" + actual); }
        static void VerifyLifecycle()
        {
            var rows = Read(firstMovement).Skip(1).Select(r => r.Split(',')).ToArray();
            var events = Read(firstEvents).Select(JObject.Parse).ToArray();
            Check(rows.Length > 60 && rows.All(r => r.Length == 25 && r[1] == firstRun), "Reopened run identity/legacy columns", "rows>60,25 columns,one run", rows.Length);
            var providers = rows.Where(r => r[12] == "ProviderReported").ToArray();
            int valid = providers.Count(r => r[14] == "True" && r[7] == "SimulatedController");
            Check(valid > 9, "Moving synthetic providers remain synthetic", ">9 valid synthetic provider rows", valid);
            var start = events.Single(e => (string)e["event"] == "MOVEMENT_FIXTURE_TRAJECTORY_START");
            var end = events.Single(e => (string)e["event"] == "MOVEMENT_FIXTURE_TRAJECTORY_END");
            var controlled = providers.Where(r => r[11] == "handslab.head" && r[14] == "True" &&
                long.Parse(r[3]) > (long)start["seq"] && long.Parse(r[3]) < (long)end["seq"] && Number(r[0]) > (double)start["timestamp"] + .12).ToArray();
            double endpointDistance = controlled.Length > 1 ? Vector3.Distance(Position(controlled.First()), Position(controlled.Last())) : 0;
            Check(controlled.Length >= 3 && Math.Abs(endpointDistance - .2) <= .02, "Controlled pre-teleport tracking-space trajectory", "0.20m +/-0.02m in one stable segment", endpointDistance);
            Check(providers.Any(r => r[14] == "False" && r[18] == "" && r[11] == "handslab.left.provider"), "Tracking loss has blank coordinates", true, providers.Count(r => r[14] == "False" && r[18] == ""));
            Check(providers.Any(r => r[15] == "session_or_application_paused" && r[14] == "False"), "Pause invalidates scheduled provider rows", true, providers.Count(r => r[15] == "session_or_application_paused"));
            Check(providers.Any(r => r[15] == "application_focus_lost" && r[14] == "False"), "Focus loss invalidates scheduled provider rows", true, providers.Count(r => r[15] == "application_focus_lost"));
            var contexts = events.Where(e => (string)e["event"] == "MOVEMENT_CONTEXT").Select(e => e["after"]).ToArray();
            Check(contexts.Any(e => (string)e["task_id"] == "training") && contexts.Any(e => (string)e["task_id"] == "A") && contexts.Any(e => (string)e["task_id"] == "B"), "Explicit Tutorial/A/B context", "training,A,B", string.Join(",", contexts.Select(e => (string)e["task_id"]).Distinct()));
            Check(contexts.Any(e => ((string)e["boundary_reason"]).Contains("synthetic_snap_turn")) && contexts.Any(e => ((string)e["boundary_reason"]).Contains("synthetic_teleport")), "Locomotion discontinuity segmentation", "snap and teleport boundaries", contexts.Length);
            Check(Read(secondMovement).Length > 1 && Read(secondMovement).Skip(1).All(r => r.Split(',')[1] != firstRun) && new FileInfo(firstMovement).Length == firstLength, "Stop/start isolation", "new run rows; prior file stable", Read(secondMovement).Length - 1);
            Check(sawWriterError, "Actual disk writer failure propagates to Controller", "WriteError and nonempty Error", sawWriterError);
            var frameTimes = rows.GroupBy(r => r[4]).Select(g => double.Parse(g.First()[0], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            double hz = (frameTimes.Length - 1) / (frameTimes.Last() - frameTimes.First());
            Check(hz > 1 && hz <= 21, "Actual effective scheduled rate bounded by configured20Hz", "1<Hz<=21; actual retained", hz);
            var gaps = frameTimes.Zip(frameTimes.Skip(1), (a,b) => b-a).ToArray();
            var contextEvents = events.Where(e => (string)e["event"] == "MOVEMENT_CONTEXT").ToArray();
            long Segment(string[] row) => (long)(contextEvents.LastOrDefault(e => (long)e["seq"] < long.Parse(row[3]))?["after"]?["segment_id"] ?? -1L);
            Check(controlled.Select(Segment).Distinct().Count() == 1, "Controlled trajectory excludes world-relocation segments", "one segment", controlled.Select(Segment).Distinct().Count());
            var heads = providers.Where(r => r[11] == "handslab.head").ToArray();
            double includedDistance = 0, excludedBoundaryDistance = 0; int excludedBoundaryEdges = 0;
            for (int i=1; i<heads.Length; i++)
            {
                if (heads[i-1][14] != "True" || heads[i][14] != "True") continue;
                double distance = Vector3.Distance(Position(heads[i-1]), Position(heads[i]));
                if (Segment(heads[i-1]) != Segment(heads[i])) { excludedBoundaryDistance += distance; excludedBoundaryEdges++; }
                else if (Number(heads[i][0]) - Number(heads[i-1][0]) <= .25) includedDistance += distance;
            }
            Check(excludedBoundaryDistance > 3 && includedDistance < 2, "Teleport relocation excluded from physical-distance derivative", ">3m excluded; <2m within-segment synthetic movement", new { includedDistance, excludedBoundaryDistance, excludedBoundaryEdges });
            File.WriteAllText(Path.Combine(evidence, "movement-lifecycle-result.json"), JsonConvert.SerializeObject(new { status = "PASS", checks, first_run = firstRun,
                firstMovement, secondMovement, actual_rows = rows.Length, valid_synthetic_provider_rows = valid, effective_hz = hz, contexts = contexts.Length,
                controlled_expected_metres = .20, controlled_actual_metres = endpointDistance, configured_target_hz = 20,
                maximum_actual_gap_seconds = gaps.Length == 0 ? (double?)null : gaps.Max(), gaps_over_two_target_intervals = gaps.Count(g => g > .10),
                rate_status = hz >= 19 ? "NEAR_CONFIGURED_TARGET_IN_THIS_SYNTHETIC_WINDOW" : "CONFIGURED_TARGET_NOT_ACHIEVED_IN_THIS_SYNTHETIC_WINDOW",
                included_synthetic_distance_metres = includedDistance, excluded_boundary_distance_metres = excludedBoundaryDistance,
                observation_clock = "logger monotonic provider observations; no native sensor timestamp claim", hardware_verified = false,
                scope = "Actual ResearchFlow.StartSession/Abort lifecycle and scheduled component; synthetic InputSystem motion, injected task states; no study content/physical controller/wearer proof" }, Formatting.Indented));
            Debug.Log("MOVEMENT_LIFECYCLE_PASS reopened_rows=" + rows.Length + " effective_hz=" + hz + " " + evidence);
        }
        static double Number(string value) => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        static Vector3 Position(string[] row) => new Vector3((float)Number(row[18]), (float)Number(row[19]), (float)Number(row[20]));
        public static void CancelLifecycle()
        {
            if (active && evidence != null) File.WriteAllText(Path.Combine(evidence, "movement-lifecycle-cancelled.json"), JsonConvert.SerializeObject(new { status = "INTERRUPTED", stage, hardware_verified = false }));
            Cleanup();
        }
        static void Cleanup()
        {
            EditorApplication.update -= LifecycleTick; AssemblyReloadEvents.beforeAssemblyReload -= CancelLifecycle;
            if (ownedInput) { ResearchFlowVisualInput.Stop(); ownedInput = false; }
            if (active)
            {
                if (active.Record?.session_state == RegistryState.InProgress && active.registryRoot == Path.Combine(evidence, "registry")) active.ResearcherAbort();
                active.GetComponent<ResearchMovementSampler>()?.FlushAndStop("fixture_finished");
                active.origin.transform.SetPositionAndRotation(savedOriginPosition, savedOriginRotation);
                active.registryRoot = savedRegistryRoot;
                foreach (var item in savedProperties) Set(item.Key, item.Value);
                active.view?.Refresh();
            }
            active = null;
        }
        // The active ResearchFlow lifecycle must produce rows, rather than only a writer/header.
        public static string CheckActiveDisk(string evidenceDirectory)
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            if (!controller || controller.Record == null || controller.Logger == null || !controller.IsTechnical)
                throw new InvalidOperationException("An active isolated technical ResearchFlow session is required.");
            string[] rows;
            using (var reader = new StreamReader(new FileStream(controller.Logger.MovementPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                rows = reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int actual = Math.Max(0, rows.Length - 1);
            Directory.CreateDirectory(evidenceDirectory);
            File.WriteAllText(Path.Combine(evidenceDirectory, "movement-lifecycle-observation.json"), JsonConvert.SerializeObject(new {
                expected = "nonzero scheduled rows after at least one second in active ResearchFlow",
                actual_rows = actual, run_id = controller.Record.run_id, logger_run_id = controller.Logger.RunId,
                reopened_path = controller.Logger.MovementPath, hardware_verified = false,
                identity_matches = controller.Record.run_id == controller.Logger.RunId,
                source = "Actual active ResearchFlow lifecycle; native validity is assessed separately"
            }, Formatting.Indented));
            if (actual == 0) throw new InvalidOperationException("MOVEMENT_LIFECYCLE_FAIL expected > 0 scheduled rows, actual=0 (header only).");
            if (controller.Record.run_id != controller.Logger.RunId || rows.Skip(1).Any(r => r.Split(',')[1] != controller.Record.run_id))
                throw new InvalidOperationException("MOVEMENT_LIFECYCLE_FAIL wrong run identity.");
            return "MOVEMENT_LIFECYCLE_PASS reopened_rows=" + actual + " run=" + controller.Record.run_id;
        }
    }
}
#endif
