#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1OperatorOutcomeChecks
    {
        public static string Run(string evidenceDirectory)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Inactive component fixture only.");
            if (Directory.Exists(evidenceDirectory)) throw new IOException("Use a fresh evidence directory.");
            Directory.CreateDirectory(evidenceDirectory);
            var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty;
            int previews = EditorSceneManager.previewSceneCount, failures = 0;
            var observations = new List<object>();
            foreach (string scenario in new[] { "abort_prior", "invalidate_prior", "export_prior", "abort_clean", "authorization_write_failure",
                "registry_write_failure", "terminal_write_failure", "export_failure", "abort_export_warning", "terminal_noop", "immutable_replay" })
            {
                var preview = EditorSceneManager.NewPreviewScene();
                var root = new GameObject("S1_OperatorOutcome_TEST") { hideFlags = HideFlags.HideAndDontSave }; root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, preview);
                HandsLabLogger logger = null; FileStream registryLock = null;
                try
                {
                    string directory = Path.Combine(evidenceDirectory, scenario), data = Path.Combine(directory, "data_TEST");
                    var registry = new ResearchParticipantRegistry(data);
                    var record = registry.CreateNext(ParticipantGroup.Test);
                    record = registry.StartOrResume(record.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller, TaskOrder.AB);
                    var flow = new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5);
                    record = registry.SaveProgress(record, flow);
                    var controller = root.AddComponent<ResearchSessionFlowController>(); controller.registryRoot = data;
                    logger = new HandsLabLogger(Path.Combine(data, "Logs"), "SimulatedController", "S1 isolated operator outcome TEST",
                        executionEnvironment: "Editor", phase: "ResearchSession", taskId: "A", technicalTest: true, testData: true, runId: record.run_id);
                    Set(controller, "Registry", registry); Set(controller, "Record", record); Set(controller, "Flow", flow); Set(controller, "Logger", logger);
                    var port = root.AddComponent<ResearchTaskPort>(); port.taskId = "A"; port.demoMode = true; port.contentApproved = false;
                    bool terminalWriterClosed = false;
                    port.BindSnapshot(() => {
                        if (scenario == "terminal_write_failure" && !terminalWriterClosed && ReadEvents(logger.EventsPath).Any(row => (string)row["event"] == "OPERATOR_COMMAND_AUTHORIZED" && (string)row["target"] == "operator.abort"))
                        { terminalWriterClosed = true; CloseEventsWriter(logger); }
                        return new { fixture = "unavailable_inspection_adapter", repair_completed = false };
                    }, state => { throw new InvalidOperationException("TEST unfinished task inspection adapter"); }, allowed => { });
                    controller.taskPorts = new[] { port }; port.SetInteractionAllowed(true);
                    ((ResearchTaskTiming)typeof(ResearchSessionFlowController).GetField("taskTiming", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)).Start(logger.Timestamp);
                    bool priorExpected = scenario != "abort_clean" && scenario != "terminal_noop";
                    bool inspectAccepted = priorExpected && controller.Command("inspect", "SimulatedController");
                    string prior = controller.Error;
                    if (priorExpected && (inspectAccepted || prior != "Task inspection adapter unavailable. Contact researcher."))
                        throw new InvalidOperationException("Fixture did not produce the real unavailable Inspect error.");
                    if (scenario == "terminal_noop") { record = registry.Finish(record, RegistryState.Aborted); Set(controller, "Record", record); }
                    var view = root.AddComponent<ResearcherSetupView>(); view.controller = controller; view.OperatorDirectoryOverride = Path.Combine(directory, "operator_TEST");
                    Directory.CreateDirectory(view.OperatorDirectory);
                    string command = scenario == "invalidate_prior" ? "invalidate" : scenario == "export_prior" || scenario == "export_failure" ? "export" : "abort";
                    var request = new ResearchOperatorCommand { schema = "research.operator-command.1", request_id = Guid.NewGuid().ToString("N"), command = command,
                        participant_id = record.participant_id, session_id = record.session_id, termination_reason = "technical_failure" };
                    if (scenario == "authorization_write_failure") CloseEventsWriter(logger);
                    if (scenario == "registry_write_failure") registryLock = new FileStream(registry.RegistryPath, FileMode.Open, FileAccess.Read, FileShare.None);
                    if (scenario == "export_failure" || scenario == "abort_export_warning") File.WriteAllText(Path.Combine(data, "Exports"), "Intentional TEST filesystem obstruction");
                    string input = Path.Combine(view.OperatorDirectory, "research-operator-command.json");
                    File.WriteAllText(input, JsonConvert.SerializeObject(request));
                    Poll(view); registryLock?.Dispose(); registryLock = null;
                    var response = JObject.Parse(File.ReadAllText(Path.Combine(view.OperatorDirectory, "research-operator-response.json")));
                    var saved = registry.List(ParticipantGroup.Test).Single();
                    bool executed = !new[] { "authorization_write_failure", "registry_write_failure", "terminal_write_failure", "export_failure", "terminal_noop" }.Contains(scenario);
                    bool pass = (string)response["status"] == (executed ? "Executed" : "Blocked");
                    var raw = ReadEvents(logger.EventsPath);
                    bool terminalExpected = command != "export" && scenario != "authorization_write_failure" && scenario != "registry_write_failure";
                    if (terminalExpected) pass &= saved.session_state == (command == "invalidate" ? RegistryState.Invalid : RegistryState.Aborted);
                    if (executed && priorExpected) pass &= controller.Error == prior && response["warnings"] is JArray warnings && warnings.Any(value => (string)value == prior);
                    if (executed) pass &= (string)response["registry_state"] == saved.session_state.ToString() && (string)response["run_id"] == record.run_id;
                    if (scenario == "authorization_write_failure") pass &= saved.session_state == RegistryState.InProgress && !raw.Any(row => (string)row["event"] == "SESSION_ABORTED");
                    if (scenario == "registry_write_failure") pass &= saved.session_state == RegistryState.InProgress && !string.IsNullOrEmpty(controller.Error);
                    if (scenario == "terminal_write_failure") pass &= terminalWriterClosed && !string.IsNullOrEmpty(logger.LastError)
                        && (string)response["registry_state"] == "Aborted" && !raw.Any(row => (string)row["event"] == "SESSION_ABORTED");
                    if (scenario == "abort_export_warning") pass &= !string.IsNullOrEmpty(controller.ExportDeliveryError)
                        && response["warnings"] is JArray exportWarnings && exportWarnings.Any(value => (string)value == controller.ExportDeliveryError);
                    bool exportPresent = !string.IsNullOrEmpty(controller.LastExportPath) && File.Exists(Path.Combine(controller.LastExportPath, "COMPLETE.json"));
                    if (executed && scenario != "abort_export_warning") pass &= exportPresent;
                    if (scenario == "immutable_replay")
                    {
                        string immutablePath = Path.Combine(view.OperatorDirectory, "results", request.request_id + ".response.json");
                        byte[] immutable = File.ReadAllBytes(immutablePath); int eventCount = raw.Length;
                        File.WriteAllText(input, JsonConvert.SerializeObject(request)); Poll(view);
                        pass &= immutable.SequenceEqual(File.ReadAllBytes(immutablePath)) && ReadEvents(logger.EventsPath).Length == eventCount
                            && (string)JObject.Parse(File.ReadAllText(Path.Combine(view.OperatorDirectory, "research-operator-response.json")))["status"] == "Blocked";
                    }
                    if (!pass) failures++;
                    observations.Add(new { scenario, pass, response, prior_error = prior, controller_error = controller.Error,
                        export_error = controller.ExportDeliveryError, logger_error = logger.LastError, registry_state = saved.session_state.ToString(),
                        export_path = controller.LastExportPath, export_complete_marker = exportPresent, events_path = logger.EventsPath,
                        participant_id = record.participant_id, record.session_id, record.run_id, raw_events = raw });
                }
                catch (Exception exception) { failures++; observations.Add(new { scenario, pass = false, error = exception.ToString() }); }
                finally
                {
                    registryLock?.Dispose();
                    var controller = root.GetComponent<ResearchSessionFlowController>(); if (controller) Set(controller, "Logger", null);
                    logger?.Dispose(); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview);
                }
            }
            bool preserved = SceneManager.GetActiveScene() == scene && scene.isDirty == dirty && previews == EditorSceneManager.previewSceneCount;
            if (!preserved) failures++;
            File.WriteAllText(Path.Combine(evidenceDirectory, "result.json"), JsonConvert.SerializeObject(new { failures, checks = observations.Count, observations,
                scene_preserved = preserved, scope = "Actual isolated inactive Unity Command/Inspect and operator Poll/guards/registry/logger/export. State preparation and failure injection are TEST fixtures, not UI/Play E2E or wearer evidence.", hardware_verified = false }, Formatting.Indented));
            return "S1_OPERATOR_OUTCOME_" + (failures == 0 ? "PASS" : "FAIL") + " checks=" + observations.Count + " failures=" + failures;
        }
        static JObject[] ReadEvents(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
                return Array.ConvertAll(reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), JObject.Parse);
        }
        static void Poll(ResearcherSetupView view) => typeof(ResearcherSetupView).GetMethod("PollOperatorCommand", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        static void Set(ResearchSessionFlowController controller, string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).SetValue(controller, value, null);
        static void CloseEventsWriter(HandsLabLogger logger) => ((StreamWriter)typeof(HandsLabLogger).GetField("eventsWriter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(logger)).Dispose();
    }
}
#endif
