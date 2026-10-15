#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchOperatorChecks
    {
        public static string CheckSummaryPauseIntervals(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var sceneBefore = SceneManager.GetActiveScene(); bool dirtyBefore = sceneBefore.isDirty;
            int previewsBefore = EditorSceneManager.previewSceneCount;
            var scenarios = new (string name, bool completed, (double resumed, double paused)[] pauses, string duration)[] {
                ("no_pause", true, Array.Empty<(double, double)>(), "01:00"),
                ("in_task", true, new[] { (40d, 10d) }, "00:50"),
                ("post_task_checkpoint", true, new[] { (200d, 100d) }, "01:00"),
                ("pre_task", true, new[] { (8d, 6d) }, "01:00"),
                ("overlap_start", true, new[] { (15d, 10d) }, "00:55"),
                ("overlap_end", true, new[] { (80d, 20d) }, "00:50"),
                ("cover_task", true, new[] { (100d, 100d) }, "00:00"),
                ("sibling_task_pause", true, new[] { (40d, 10d) }, "01:00"),
                ("incomplete", false, new[] { (40d, 10d) }, "-")
            };
            var rows = new System.Collections.Generic.List<object>();
            bool passed = true;
            foreach (string task in new[] { "A", "B" })
                foreach (var scenario in scenarios)
                {
                    var preview = EditorSceneManager.NewPreviewScene();
                    var root = new GameObject("Summary_ExplicitMockEvents_" + task + "_" + scenario.name) { hideFlags = HideFlags.HideAndDontSave };
                    SceneManager.MoveGameObjectToScene(root, preview);
                    root.SetActive(false);
                    VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger logger = null;
                    try
                    {
                        var controller = root.AddComponent<ResearchSessionFlowController>();
                        void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(controller, new[] { value });
                        Set("Record", new ParticipantRecord { participant_id = "TEST_SUMMARY_MOCK", group = ParticipantGroup.Test,
                            test_data = true, task_order = TaskOrder.AB, session_state = RegistryState.InProgress });
                        logger = new VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger(Path.Combine(evidenceDirectory, task + "_" + scenario.name),
                            "None", "ExplicitMockSummaryFixture", technicalTest: true, testData: true);
                        logger.Dispose(); // Close this isolated writer before replacing only its fixture-owned event file with explicit mock rows.
                        Set("Logger", logger);
                        var events = new System.Collections.Generic.List<Newtonsoft.Json.Linq.JObject>();
                        void Event(string kind, string eventTask, double timestamp, object after = null) => events.Add(Newtonsoft.Json.Linq.JObject.FromObject(new {
                            @event = kind, task_id = eventTask, timestamp, after, source = "Tester", test_data = true, mock_data = true, hardware_verified = false
                        }));
                        Event("TASK_STARTED", task, 10);
                        if (scenario.completed) Event("TASK_COMPLETED", task, 70);
                        foreach (var pause in scenario.pauses)
                            Event("SESSION_RESUMED", scenario.name == "sibling_task_pause" ? task == "A" ? "B" : "A" : task,
                                pause.resumed, new { paused_seconds = pause.paused });
                        File.WriteAllText(logger.EventsPath, string.Join("\n", System.Linq.Enumerable.Select(events, row => row.ToString(Formatting.None))) + "\n");
                        var before = File.ReadAllBytes(logger.EventsPath);
                        string summary = controller.SummaryText();
                        string expected = "Task " + task + "    " + (scenario.completed ? "Completed" : "In progress") + "    " + scenario.duration;
                        bool unchanged = System.Linq.Enumerable.SequenceEqual(before, File.ReadAllBytes(logger.EventsPath));
                        bool pass = summary.Contains(expected) && unchanged;
                        passed &= pass;
                        rows.Add(new { task, scenario = scenario.name, pass, expected, actual_summary = summary, raw_bytes_unchanged = unchanged,
                            mock_events = events, events_path = logger.EventsPath });
                    }
                    finally
                    {
                        logger?.Dispose(); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview);
                    }
                }
            bool scenePreserved = sceneBefore.IsValid() && SceneManager.GetActiveScene() == sceneBefore && sceneBefore.isDirty == dirtyBefore
                && EditorSceneManager.previewSceneCount == previewsBefore;
            passed &= scenePreserved;
            string path = Path.Combine(evidenceDirectory, "summary-pause-intervals.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = passed ? "PASS" : "FAIL", checks = rows.Count, rows,
                scene_before = sceneBefore.path, dirty_before = dirtyBefore, dirty_after = sceneBefore.isDirty, scene_preserved = scenePreserved,
                previews_before = previewsBefore, previews_after = EditorSceneManager.previewSceneCount,
                scope = "Actual SummaryText on isolated explicitly mock TEST event rows; no research timing/raw participant files changed", hardware_verified = false }, Formatting.Indented));
            if (!passed) throw new InvalidOperationException("Researcher summary subtracts pause time outside task boundaries; inspect " + path);
            return "SUMMARY_PAUSE_INTERVALS_PASS " + rows.Count;
        }

        public static string CheckPausedCheckpointConfirmation(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var sceneBefore = SceneManager.GetActiveScene(); bool dirtyBefore = sceneBefore.isDirty;
            int previewsBefore = EditorSceneManager.previewSceneCount;
            var rows = new System.Collections.Generic.List<object>();
            bool passed = true;
            Newtonsoft.Json.Linq.JObject[] ReadEvents(string path)
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                    return Array.ConvertAll(reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), Newtonsoft.Json.Linq.JObject.Parse);
            }
            foreach (string scenario in new[] { "active", "checkpoint1", "checkpoint2", "writer_failure", "persistence_failure" })
            {
                var preview = EditorSceneManager.NewPreviewScene();
                var root = new GameObject("PausedCheckpoint_Isolated_" + scenario) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, preview);
                root.SetActive(false);
                VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger logger = null;
                ResearchSessionFlowController controller = null;
                try
                {
                    string isolatedRoot = Path.Combine(Path.GetFullPath(evidenceDirectory), scenario);
                    var registry = new ResearchParticipantRegistry(isolatedRoot);
                    var record = registry.CreateNext(ParticipantGroup.Test);
                    record = registry.StartOrResume(record.participant_id, ParticipantLanguage.English, ExperimentInputMode.Controller, TaskOrder.AB);
                    var initialState = scenario == "active" ? ResearchFlowState.Task1Active : scenario == "checkpoint1"
                        ? ResearchFlowState.PostTask1Checkpoint : ResearchFlowState.PostTask2Checkpoint;
                    var flow = new ResearchSessionFlow(TaskOrder.AB, initialState, 5);
                    record = registry.SaveProgress(record, flow);
                    controller = root.AddComponent<ResearchSessionFlowController>(); controller.enabled = false; controller.registryRoot = isolatedRoot;
                    void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(controller, new[] { value });
                    Set("Registry", registry); Set("Record", record); Set("Flow", flow);
                    // Only this fixture's transforms move; no XR provider, controller, presence or tracking is synthesized.
                    var originRoot = new GameObject("IsolatedStationTransforms") { hideFlags = HideFlags.HideAndDontSave };
                    SceneManager.MoveGameObjectToScene(originRoot, preview); originRoot.SetActive(false); originRoot.transform.SetParent(root.transform);
                    var origin = originRoot.AddComponent<Unity.XR.CoreUtils.XROrigin>(); origin.Origin = originRoot;
                    var cameraRoot = new GameObject("IsolatedCamera") { hideFlags = HideFlags.HideAndDontSave };
                    SceneManager.MoveGameObjectToScene(cameraRoot, preview); cameraRoot.transform.SetParent(originRoot.transform);
                    origin.Camera = cameraRoot.AddComponent<Camera>(); origin.Camera.enabled = false;
                    controller.origin = origin; controller.stations = new Transform[3];
                    for (int i = 0; i < controller.stations.Length; i++)
                    {
                        var station = new GameObject("IsolatedStation_" + i) { hideFlags = HideFlags.HideAndDontSave };
                        SceneManager.MoveGameObjectToScene(station, preview); station.transform.SetParent(root.transform);
                        controller.stations[i] = station.transform;
                    }
                    logger = new VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger(Path.Combine(isolatedRoot, "raw"), "None", "PausedCheckpointFixture",
                        technicalTest: true, testData: true, runId: record.run_id);
                    Set("Logger", logger); root.SetActive(true); // Dependencies are explicit; disabled Controller never runs Start/Update.
                    logger.WriteFailed += message => typeof(ResearchSessionFlowController).GetMethod("LoggerFailed", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(controller, new object[] { message });
                    typeof(ResearchSessionFlowController).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(controller, new object[] { true });
                    if (!flow.Paused || !string.IsNullOrEmpty(controller.Error))
                        throw new InvalidOperationException("Application-pause fixture setup failed: " + controller.Error);
                    if (scenario == "writer_failure")
                        ((StreamWriter)typeof(VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger).GetField("eventsWriter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(logger)).Dispose();
                    if (scenario == "persistence_failure") registry.SaveProgress(controller.Record, flow); // A stale revision exercises the real persistence refusal.
                    controller.ResearcherResume();
                    var firstState = flow.State;
                    var events = ReadEvents(logger.EventsPath);
                    int resumed = Array.FindIndex(events, row => (string)row["event"] == "SESSION_RESUMED");
                    int checkpoint = Array.FindIndex(events, row => (string)row["event"] == "CHECKPOINT_COMPLETED");
                    int checkpointCount = Array.FindAll(events, row => (string)row["event"] == "CHECKPOINT_COMPLETED").Length;
                    bool failed = scenario == "writer_failure" || scenario == "persistence_failure";
                    var expected = failed ? initialState : scenario == "checkpoint1" ? ResearchFlowState.Task2Ready
                        : scenario == "checkpoint2" ? ResearchFlowState.ParticipantComplete : initialState;
                    bool pass = flow.State == expected && !flow.Paused && checkpointCount == (failed || scenario == "active" ? 0 : 1)
                        && (failed ? !string.IsNullOrEmpty(controller.Error) : string.IsNullOrEmpty(controller.Error) && resumed >= 0);
                    if (!failed && scenario != "active")
                        pass &= checkpoint > resumed && (string)events[checkpoint]["phase"] == "Checkpoint"
                            && (string)events[checkpoint]["task_id"] == (scenario == "checkpoint1" ? "A" : "B");
                    var saved = System.Linq.Enumerable.Single(registry.List(ParticipantGroup.Test), item => item.participant_id == record.participant_id);
                    if (scenario == "checkpoint2") pass &= logger.DataComplete && saved.session_state == RegistryState.Completed
                        && Array.FindAll(events, row => (string)row["event"] == "RUN_COMPLETED").Length == 1;
                    if (failed) pass &= saved.session_state == RegistryState.InProgress && saved.flow_state == initialState && !logger.DataComplete;
                    int eventCount = events.Length; controller.ResearcherResume();
                    pass &= ReadEvents(logger.EventsPath).Length == eventCount && flow.State == expected;
                    passed &= pass;
                    rows.Add(new { scenario, pass, initial_state = initialState.ToString(), first_state = firstState.ToString(), actual_state = flow.State.ToString(), flow.Paused,
                        checkpoint_count = checkpointCount, resumed_index = resumed, checkpoint_index = checkpoint, controller.Error,
                        logger_state = logger.State.ToString(), registry_state = saved.session_state.ToString(), events_path = logger.EventsPath });
                }
                finally
                {
                    if (controller) typeof(ResearchSessionFlowController).GetProperty("Logger").GetSetMethod(true).Invoke(controller, new object[] { null });
                    logger?.Dispose(); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview);
                }
            }
            bool scenePreserved = sceneBefore.IsValid() && SceneManager.GetActiveScene() == sceneBefore && sceneBefore.isDirty == dirtyBefore
                && EditorSceneManager.previewSceneCount == previewsBefore;
            passed &= scenePreserved;
            string path = Path.Combine(evidenceDirectory, "paused-checkpoint-confirmation.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = passed ? "PASS" : "FAIL", checks = rows.Count, rows,
                scene_before = sceneBefore.path, dirty_before = dirtyBefore, dirty_after = sceneBefore.isDirty, scene_preserved = scenePreserved,
                previews_before = previewsBefore, previews_after = EditorSceneManager.previewSceneCount,
                scope = "Actual isolated TEST Controller callbacks, application pause, registry and writer; fixture transforms only; no approvals or native/wearer evidence", hardware_verified = false }, Formatting.Indented));
            if (!passed) throw new InvalidOperationException("Paused checkpoint requires repeated confirmation or violated failure gates; inspect " + path);
            return "PAUSED_CHECKPOINT_CONFIRMATION_PASS " + rows.Count;
        }

        public static string CheckTaskTerminationDisk(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var root = new GameObject("TaskTermination_IsolatedTechnicalDiskFixture") { hideFlags = HideFlags.HideAndDontSave }; root.SetActive(false);
            VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger logger = null;
            try
            {
                string isolatedRoot = Path.Combine(Path.GetFullPath(evidenceDirectory), "isolated-registry");
                var registry = new ResearchParticipantRegistry(isolatedRoot);
                var available = registry.CreateNext(ParticipantGroup.Test);
                var record = registry.StartOrResume(available.participant_id, ParticipantLanguage.English, ExperimentInputMode.Controller, TaskOrder.AB);
                var flow = new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5);
                record = registry.SaveProgress(record, flow);
                var controller = root.AddComponent<ResearchSessionFlowController>(); controller.registryRoot = isolatedRoot;
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(controller, new[] { value });
                Set("Registry", registry); Set("Record", record); Set("Flow", flow);
                var port = root.AddComponent<ResearchTaskPort>(); port.taskId = "A"; port.demoMode = true; port.contentApproved = false;
                port.Bind(() => new { fixture = "isolated_termination", repair_completed = false }, () => false, allowed => { });
                port.SetInteractionAllowed(true); controller.taskPorts = new[] { port };
                logger = new VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger(Path.Combine(isolatedRoot, "Logs"), "None", "IsolatedTaskTerminationFixture",
                    technicalTest: true, testData: true, phase: "ResearchSession", taskId: "session", runId: record.run_id);
                Set("Logger", logger);
                controller.ResearcherAbort(terminationReason: "technical_failure");
                var events = Array.ConvertAll(File.ReadAllLines(logger.EventsPath), Newtonsoft.Json.Linq.JObject.Parse);
                int terminated = Array.FindIndex(events, row => (string)row["event"] == "TASK_TERMINATED");
                int aborted = Array.FindIndex(events, row => (string)row["event"] == "SESSION_ABORTED");
                bool terminalExact = terminated >= 0 && aborted > terminated && (string)events[terminated]["task_id"] == "A"
                    && (string)events[terminated]["target"] == "task.A" && (string)events[terminated]["reason"] == "technical_failure"
                    && (string)events[terminated]["result"] == "Aborted" && (long)events[terminated]["seq"] < (long)events[aborted]["seq"];
                var saved = System.Linq.Enumerable.Single(registry.List(ParticipantGroup.Test), item => item.participant_id == record.participant_id);
                bool recordAborted = controller.Record.session_state == RegistryState.Aborted && saved.session_state == RegistryState.Aborted;
                bool incomplete = !logger.DataComplete && logger.State == VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogState.Incomplete;
                controller.WaitForExports(60000);
                string export = controller.LastExportPath;
                bool exportOwned = !string.IsNullOrEmpty(export) && Path.GetFullPath(export).StartsWith(isolatedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                var manifest = exportOwned && File.Exists(Path.Combine(export, "manifest.json")) ? Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(export, "manifest.json"))) : null;
                bool partialExport = manifest != null && (string)manifest["lifecycle"] == "partial" && (string)manifest["reason"] == "session_aborted"
                    && (bool?)manifest["identity"]?["test_data"] == true && File.Exists(Path.Combine(export, "COMPLETE.json"));
                bool noInventedOutcome = !Array.Exists(events, row => (string)row["event"] == "TASK_COMPLETED" || (string)row["event"] == "INSPECT_PASS"
                    || (string)row["event"] == "RUN_COMPLETED" || (string)row["event"] == "CHECKPOINT_COMPLETED" || row["answers"] != null);
                bool pass = terminalExact && recordAborted && incomplete && partialExport && noInventedOutcome && !port.InteractionAllowed && string.IsNullOrEmpty(controller.Error);
                File.WriteAllText(Path.Combine(evidenceDirectory, "root-task-termination-disk.json"), JsonConvert.SerializeObject(new {
                    status = pass ? "PASS" : "FAIL", terminal_event_exact_before_abort = terminalExact, registry_and_controller_aborted = recordAborted,
                    logger_incomplete = incomplete, partial_export_reopened = partialExport, export_owned_by_fixture = exportOwned, no_pass_or_answers = noInventedOutcome,
                    interaction_closed = !port.InteractionAllowed, controller_error = controller.Error, export_error = controller.ExportDeliveryError,
                    events_path = logger.EventsPath, export_path = export, record.run_id, record.session_id, participant_id = record.participant_id,
                    terminated_event = terminated < 0 ? null : events[terminated], aborted_event = aborted < 0 ? null : events[aborted],
                    scope = "Actual inactive Controller.ResearcherAbort callback plus isolated TEST registry/logger/export disk; no native input, participant approval, repair success or wearer evidence", hardware_verified = false
                }, Formatting.Indented));
                if (!pass) throw new InvalidOperationException("Task termination disk contract failed; inspect root-task-termination-disk.json.");
                return "ROOT_TASK_TERMINATION_DISK_PASS";
            }
            finally { logger?.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string CheckTerminalCommandRefusal(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var root = new GameObject("TerminalCommand_IsolatedDTOAndWriterFixture") { hideFlags = HideFlags.HideAndDontSave }; root.SetActive(false);
            VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger logger = null;
            var rows = new System.Collections.Generic.List<object>();
            try
            {
                var controller = root.AddComponent<ResearchSessionFlowController>();
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(controller, new[] { value });
                Set("Registry", new ResearchParticipantRegistry(Path.Combine(evidenceDirectory, "isolated-empty-registry")));
                var flow = new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Ready, 5);
                var record = new ParticipantRecord { participant_id = "TEST_TERMINAL_COMMAND_FIXTURE", group = ParticipantGroup.Test, test_data = true,
                    session_id = "isolated_terminal_fixture", session_state = RegistryState.Aborted, flow_state = flow.State, tutorial_completed = 5 };
                logger = new VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger(Path.Combine(evidenceDirectory, "raw"), "None", "IsolatedTerminalFixture",
                    technicalTest: true, testData: true);
                record.run_id = logger.RunId;
                Set("Record", record); Set("Flow", flow); Set("Logger", logger);
                string[] rawPaths = { logger.EventsPath, logger.MovementPath, logger.MetadataPath };
                byte[] Read(string path) { using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var bytes = new MemoryStream()) { stream.CopyTo(bytes); return bytes.ToArray(); } }
                byte[][] Before() => Array.ConvertAll(rawPaths, Read);
                bool Stable(byte[][] before) { for (int i = 0; i < rawPaths.Length; i++) if (!System.Linq.Enumerable.SequenceEqual(before[i], Read(rawPaths[i]))) return false; return true; }
                var before = Before();
                bool accepted = controller.Command("task_start", "SimulatedController");
                bool closedRecordPass = !accepted && flow.State == ResearchFlowState.Task1Ready && Stable(before);
                rows.Add(new { name = "Aborted DTO refuses command with otherwise active writer", pass = closedRecordPass, accepted, state = flow.State.ToString(), raw_bytes_unchanged = Stable(before) });
                // Independently exercise the closed-writer gate with an otherwise active DTO. Neither DTO is saved to a registry.
                if (!logger.Complete("isolated_terminal_fixture", "Tester")) throw new InvalidOperationException("Fixture writer did not complete.");
                record.session_state = RegistryState.InProgress;
                flow = new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Ready, 5); Set("Flow", flow);
                before = Before(); accepted = controller.Command("task_start", "SimulatedController");
                bool closedWriterPass = !accepted && flow.State == ResearchFlowState.Task1Ready && Stable(before);
                rows.Add(new { name = "Completed writer refuses command with otherwise active DTO", pass = closedWriterPass, accepted, state = flow.State.ToString(), raw_bytes_unchanged = Stable(before) });
                bool pass = closedRecordPass && closedWriterPass;
                File.WriteAllText(Path.Combine(evidenceDirectory, "root-terminal-command-refusal.json"), JsonConvert.SerializeObject(new {
                    status = pass ? "PASS" : "FAIL", checks = rows.Count, rows, raw_paths = rawPaths,
                    expected = "Command returns false before Flow mutation or any events/movement/metadata byte changes",
                    scope = "Inactive actual Controller callback; isolated TEST DTOs and actual disk writer; empty registry, no participant record or approval created", hardware_verified = false
                }, Formatting.Indented));
                if (!pass) throw new InvalidOperationException("Terminal record/closed writer command changed state or raw bytes.");
                return "ROOT_TERMINAL_COMMAND_REFUSAL_PASS " + rows.Count;
            }
            finally { logger?.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string CheckScenarioOwnershipTopology(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            string runtime = Path.Combine(Application.dataPath, "VRMaintenanceResearch", "ResearchFlow", "Runtime");
            string channel = File.ReadAllText(Path.Combine(runtime, "ResearchAdbControlChannel.cs"));
            int branchStart = channel.IndexOf("case \"technical_scenario\":", StringComparison.Ordinal);
            int branchEnd = channel.IndexOf("case \"benchmark\":", branchStart, StringComparison.Ordinal);
            string branch = branchStart >= 0 && branchEnd > branchStart ? channel.Substring(branchStart, branchEnd - branchStart) : "";
            bool controllerHost = branch.Contains("controller.GetComponent<ResearchTechnicalScenario>()")
                && branch.Contains("controller.gameObject.AddComponent<ResearchTechnicalScenario>()");
            bool renderSameHost = File.ReadAllText(Path.Combine(runtime, "ResearchRenderExperiment.cs")).Contains("owner.GetComponent<ResearchTechnicalScenario>()");
            bool exportSameHost = File.ReadAllText(Path.Combine(runtime, "ResearchSessionFlowController.cs")).Contains("GetComponent<ResearchTechnicalScenario>()");
            bool siblingHost = !System.Text.RegularExpressions.Regex.IsMatch(channel, @"(?<![\w.])GetComponent<Research(?:TechnicalScenario|RenderExperiment)>\(\)");
            int filterStart = channel.IndexOf("case \"drag_filter\":", StringComparison.Ordinal);
            int filterEnd = filterStart < 0 ? -1 : channel.IndexOf("default:", filterStart, StringComparison.Ordinal);
            bool filterGuarded = filterStart >= 0 && filterEnd > filterStart
                && channel.Substring(filterStart, filterEnd - filterStart).Contains("TechnicalSession(command,");
            bool pass = controllerHost && renderSameHost && exportSameHost && siblingHost && filterGuarded;
            File.WriteAllText(Path.Combine(evidenceDirectory, "root-scenario-topology-result.json"), JsonConvert.SerializeObject(new {
                status = pass ? "PASS" : "FAIL", controller_host = controllerHost, render_same_host = renderSameHost, export_same_host = exportSameHost,
                scenario_render_sibling_host = siblingHost, drag_filter_guarded = filterGuarded,
                expected = "Diagnostic scenario attachment uses the Controller GameObject consumed by render/export ownership checks",
                scope = "Static source topology regression only; actual channel-native scenario still requires device execution", hardware_verified = false
            }, Formatting.Indented));
            if (!pass) throw new InvalidOperationException("Diagnostic scenario/render/export ownership topology differs.");
            return "ROOT_SCENARIO_TOPOLOGY_PASS";
        }

        public static string CheckChannelStudyRefusal(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var root = new GameObject("DiagnosticChannel_StudyRefusal_DTOFixture") { hideFlags = HideFlags.HideAndDontSave }; root.SetActive(false);
            bool before = ResearchDragHandle.FilterEnabled;
            try
            {
                var controller = root.AddComponent<ResearchSessionFlowController>();
                typeof(ResearchSessionFlowController).GetProperty("Record").GetSetMethod(true).Invoke(controller, new object[] {
                    new ParticipantRecord { participant_id = "TH_CHANNEL_REFUSAL_FIXTURE", group = ParticipantGroup.Thai, test_data = false } });
                var channel = root.AddComponent<ResearchAdbControlChannel>();
                typeof(ResearchAdbControlChannel).GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(channel, controller);
                var request = Newtonsoft.Json.Linq.JObject.FromObject(new { enabled = !before });
                var result = typeof(ResearchAdbControlChannel).GetMethod("Execute", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(channel, new object[] { "drag_filter", request });
                var response = Newtonsoft.Json.Linq.JObject.FromObject(result);
                bool pass = (bool?)response["ok"] == false && ResearchDragHandle.FilterEnabled == before;
                File.WriteAllText(Path.Combine(evidenceDirectory, "root-channel-study-refusal.json"), JsonConvert.SerializeObject(new {
                    status = pass ? "PASS" : "FAIL", expected = "Study DTO refused by actual diagnostic dispatch; static drag filter unchanged",
                    actual_ok = (bool?)response["ok"], filter_unchanged = ResearchDragHandle.FilterEnabled == before, error = (string)response["error"],
                    scope = "Inactive actual channel callback with isolated DTO; no participant registry, approvals, native tracking or study record created", hardware_verified = false
                }, Formatting.Indented));
                if (!pass) throw new InvalidOperationException("Diagnostic channel drag filter did not refuse the study DTO.");
                return "ROOT_CHANNEL_STUDY_REFUSAL_PASS";
            }
            finally { ResearchDragHandle.FilterEnabled = before; UnityEngine.Object.DestroyImmediate(root); }
        }

        // A missing production operator link must stop the actual callback before it mutates Flow.
        public static string CheckMissingLink(string evidenceDirectory)
        {
            var root = new GameObject("Operator_MissingLink_IsolatedCallbackFixture") { hideFlags = HideFlags.HideAndDontSave }; root.SetActive(false);
            Directory.CreateDirectory(evidenceDirectory);
            try
            {
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var flow = new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.PostTask1Checkpoint, 5);
                typeof(ResearchSessionFlowController).GetProperty("Flow").GetSetMethod(true).Invoke(controller, new object[] { flow });
                // This DTO is never written to a registry or used to create a participant run.
                typeof(ResearchSessionFlowController).GetProperty("Record").GetSetMethod(true).Invoke(controller, new object[] {
                    new ParticipantRecord { participant_id = "TH_OPERATOR_FIXTURE", group = ParticipantGroup.Thai, test_data = false, session_state = RegistryState.InProgress,
                        session_id = "operator_fixture_session", run_id = "operator_fixture_run", flow_state = ResearchFlowState.PostTask1Checkpoint, tutorial_completed = 5 } });
                var view = root.AddComponent<ResearcherSetupView>(); view.controller = controller;
                view.OperatorDirectoryOverride = Path.Combine(evidenceDirectory, "isolated-operator-control");
                typeof(ResearcherSetupView).GetMethod("ResumeSession", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                bool pass = flow.State == ResearchFlowState.PostTask1Checkpoint;
                File.WriteAllText(Path.Combine(evidenceDirectory, "operator-missing-link-result.json"), JsonConvert.SerializeObject(new {
                    status = pass ? "PASS" : "FAIL", expected = "PostTask1Checkpoint unchanged without linked operator consent/approvals",
                    actual = flow.State.ToString(), operator_status = view.OperatorStatus,
                    scope = "Actual callback on inactive isolated DTO only; no real participant registry/run", hardware_verified = false
                }, Formatting.Indented));
                if (!pass) throw new InvalidOperationException("OPERATOR_LINK_FAIL missing authorization advanced state to " + flow.State);
                return "OPERATOR_LINK_PASS missing production link blocked actual callback";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string CheckGuardedFixtures(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var results = new System.Collections.Generic.List<object>();
            void Check(bool pass, string name) { results.Add(new { name, pass }); if (!pass) throw new InvalidOperationException(name); }
            bool Denied(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
            var study = new ParticipantRecord { participant_id = "TH_OPERATOR_FIXTURE", session_id = "fixture_session", run_id = "fixture_run",
                group = ParticipantGroup.Thai, test_data = false, session_state = RegistryState.InProgress, revision = 7 };
            var auth = new ResearchOperatorAuthorization { schema = "research.operator-authorization.1", operator_id = "fixture_operator",
                participant_id = study.participant_id, session_id = study.session_id, link_status = "linked", consent_approved = true,
                content_approved = true, language_approved = true, consent_approval_ref = "fixture_external_consent", content_approval_ref = "fixture_external_content",
                language_approval_ref = "fixture_external_language" };
            Check(Denied(() => ResearchOperatorGuard.Demand("resume", study, null)), "missing link denied");
            auth.session_id = "wrong_session";
            Check(Denied(() => ResearchOperatorGuard.Demand("resume", study, auth)), "wrong session denied"); auth.session_id = study.session_id;
            auth.consent_approved = false;
            Check(Denied(() => ResearchOperatorGuard.Demand("resume", study, auth)), "missing consent denied"); auth.consent_approved = true;
            auth.content_approved = false;
            Check(Denied(() => ResearchOperatorGuard.Demand("export", study, auth)), "unapproved content denied"); auth.content_approved = true;
            auth.language_approved = null;
            Check(Denied(() => ResearchOperatorGuard.Demand("abort", study, auth)), "missing language approval denied"); auth.language_approved = true;
            Check(ResearchOperatorGuard.Demand("resume", study, auth) == "ProductionApprovedExternalAssertions", "fully explicit fixture approvals accepted by pure guard only");
            var technical = new ParticipantRecord { participant_id = "TEST_OPERATOR_FIXTURE", session_id = "test_fixture_session", group = ParticipantGroup.Test, test_data = true };
            Check(ResearchOperatorGuard.Demand("resume", technical, null) == "TechnicalTestData", "technical route explicitly test data");
            technical.test_data = false;
            Check(Denied(() => ResearchOperatorGuard.Demand("resume", technical, null)), "technical group without test-data flag denied");
            File.WriteAllText(Path.Combine(evidenceDirectory, "operator-guard-fixtures.json"), JsonConvert.SerializeObject(new {
                status = "PASS", checks = results.Count, results, scope = "Pure guard DTO fixtures only; no registry writes, study runs or actual approvals", hardware_verified = false
            }, Formatting.Indented));
            return "OPERATOR_GUARD_PASS " + results.Count;
        }

        public static string CheckHoldGuards(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var root = new GameObject("TechnicalHold_GuardFixture") { hideFlags = HideFlags.HideAndDontSave }; root.SetActive(false);
            var rows = new System.Collections.Generic.List<object>();
            void Check(bool pass, string name) { rows.Add(new { name, pass }); if (!pass) throw new InvalidOperationException(name); }
            try
            {
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var item = root.AddComponent<VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabObject>();
                controller.trainingObjects = new[] { item };
                typeof(ResearchSessionFlowController).GetProperty("Record").GetSetMethod(true).Invoke(controller, new object[] {
                    new ParticipantRecord { participant_id = "TH_HOLD_FIXTURE", group = ParticipantGroup.Thai, test_data = false } });
                Check(!item.SetIsolatedTechnicalHold(controller, true) && !item.IsHeld, "production DTO cannot acquire technical hold");
                Check(!item.SetIsolatedTechnicalHold(null, true) && !item.IsHeld, "missing controller denied");
                typeof(ResearchSessionFlowController).GetProperty("Record").GetSetMethod(true).Invoke(controller, new object[] {
                    new ParticipantRecord { participant_id = "TEST_HOLD_FIXTURE", group = ParticipantGroup.Test, test_data = true } });
                Check(!item.SetIsolatedTechnicalHold(controller, true) && !item.IsHeld, "technical DTO without owned active scenario denied");
                // Cleanup must remain possible after scenario termination and retain unrelated SDK pointer IDs.
                var objectType = item.GetType();
                objectType.GetField("technicalHoldOwner", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(item, controller);
                var pointers = (System.Collections.Generic.HashSet<int>)objectType.GetField("pointers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item);
                pointers.Add(int.MinValue); pointers.Add(777);
                Check(item.SetIsolatedTechnicalHold(controller, false) && pointers.Count == 1 && pointers.Contains(777), "cleanup removes only owned technical sentinel");
                Check(!item.SetIsolatedTechnicalHold(controller, false) && pointers.Contains(777), "unowned second cleanup cannot remove real pointer");
                File.WriteAllText(Path.Combine(evidenceDirectory, "technical-hold-guards.json"), JsonConvert.SerializeObject(new {
                    status = "PASS", checks = rows.Count, rows, scope = "Inactive DTO/held-state cleanup fixtures; no SDK grab/native tracking injected", hardware_verified = false
                }, Formatting.Indented));
                return "TECHNICAL_HOLD_GUARD_PASS " + rows.Count;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string CheckLocalTechnicalTransport(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var root = new GameObject("LocalOperator_ExplicitTechnicalTransportFixture") { hideFlags = HideFlags.HideAndDontSave }; root.SetActive(false);
            var rows = new System.Collections.Generic.List<object>();
            VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger logger = null;
            void Check(bool pass, string name) { rows.Add(new { name, pass }); if (!pass) throw new InvalidOperationException(name); }
            try
            {
                var registry = new ResearchParticipantRegistry(Path.Combine(evidenceDirectory, "isolated-registry"));
                var available = registry.CreateNext(ParticipantGroup.Test);
                var record = registry.StartOrResume(available.participant_id, ParticipantLanguage.English, ExperimentInputMode.Controller, TaskOrder.AB);
                var flow = new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5, true);
                record = registry.SaveProgress(record, flow);
                var controller = root.AddComponent<ResearchSessionFlowController>();
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(controller, new[] { value });
                Set("Registry", registry); Set("Record", record); Set("Flow", flow);
                logger = new VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger(Path.Combine(evidenceDirectory, "raw"), "None", "IsolatedOperatorFixture",
                    technicalTest: true, testData: true, runId: record.run_id);
                Set("Logger", logger);
                var view = root.AddComponent<ResearcherSetupView>(); view.controller = controller;
                view.OperatorDirectoryOverride = Path.Combine(evidenceDirectory, "OperatorControl"); Directory.CreateDirectory(view.OperatorDirectory);
                var request = new ResearchOperatorCommand { schema = "research.operator-command.1", request_id = Guid.NewGuid().ToString("N"),
                    command = "resume", participant_id = record.participant_id, session_id = record.session_id };
                string commandPath = Path.Combine(view.OperatorDirectory, "research-operator-command.json");
                string responsePath = Path.Combine(view.OperatorDirectory, "research-operator-response.json");
                var poll = typeof(ResearcherSetupView).GetMethod("PollOperatorCommand", BindingFlags.Instance | BindingFlags.NonPublic);
                File.WriteAllText(commandPath, JsonConvert.SerializeObject(request)); poll.Invoke(view, null);
                var response = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(responsePath));
                Check((string)response["status"] == "Executed" && !flow.Paused, "actual file command resumed isolated technical workflow");
                Check((bool)response["test_data"] && (string)response["scope"] == "TechnicalTestData: resume", "response remains explicit technical test data");
                Check(!File.Exists(commandPath) && Directory.GetFiles(Path.Combine(view.OperatorDirectory, "results"), "*.request.json").Length == 1,
                    "request consumed once and original preserved");
                string durablePath = Path.Combine(view.OperatorDirectory, "results", request.request_id + ".response.json");
                string durableBefore = File.ReadAllText(durablePath);
                Check(flow.Pause(), "prepare replay against an otherwise resumable state");
                File.WriteAllText(commandPath, JsonConvert.SerializeObject(request)); poll.Invoke(view, null);
                response = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(responsePath));
                Check((string)response["status"] == "Blocked" && flow.Paused && File.ReadAllText(durablePath) == durableBefore,
                    "duplicate request denied without repeating state mutation or replacing first result");
                File.WriteAllText(commandPath, "{malformed"); poll.Invoke(view, null);
                Check((string)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(responsePath))["status"] == "Blocked" && !File.Exists(commandPath),
                    "malformed command consumed and blocked");
                File.WriteAllText(commandPath, new string('x', 16385)); poll.Invoke(view, null);
                Check((string)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(responsePath))["status"] == "Blocked" && !File.Exists(commandPath),
                    "oversized command consumed and blocked");
                ((IDisposable)typeof(VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger)
                    .GetField("eventsWriter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(logger)).Dispose();
                request.request_id = Guid.NewGuid().ToString("N");
                File.WriteAllText(commandPath, JsonConvert.SerializeObject(request)); poll.Invoke(view, null);
                response = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(responsePath));
                Check((string)response["status"] == "Blocked" && flow.Paused
                    && logger.State == VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogState.WriteError,
                    "actual authorization writer failure blocks command before resume state mutation");
                logger.Dispose(); logger = null;
                // Logger path is retained through the constructor contract; inspect its actual raw result.
                var eventsFiles = Directory.GetFiles(Path.Combine(evidenceDirectory, "raw"), "events.jsonl", SearchOption.AllDirectories);
                Check(eventsFiles.Length == 1 && File.ReadAllText(eventsFiles[0]).Contains("OPERATOR_COMMAND_AUTHORIZED"), "actual raw authorization event reopened");
                File.WriteAllText(Path.Combine(evidenceDirectory, "operator-local-transport-result.json"), JsonConvert.SerializeObject(new {
                    status = "PASS", checks = rows.Count, rows, record.participant_id, record.session_id, record.run_id, raw_events_path = eventsFiles[0],
                    scope = "Actual fixed-file polling/callback/disk fixtures on an inactive isolated TEST registry; no native tracking or physical wearer evidence",
                    hardware_verified = false
                }, Formatting.Indented));
                return "OPERATOR_LOCAL_TRANSPORT_PASS " + rows.Count;
            }
            finally { logger?.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
#endif
