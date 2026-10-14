#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1ControllerSnapshotChecks
    {
        public static string Run(string evidenceDirectory)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Inactive component fixture only.");
            if (Directory.Exists(evidenceDirectory)) throw new IOException("Use a fresh evidence directory.");
            Directory.CreateDirectory(evidenceDirectory);
            var observations = new List<object>();
            int failures = 0;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool dirtyBefore = scene.isDirty;
            foreach (var expectedLock in new[] { S1ThreadState.EngagedLoose, S1ThreadState.Tight, S1ThreadState.Absent })
            {
                var root = new GameObject("S1_ExplicitSyntheticControllerFixture") { hideFlags = HideFlags.HideAndDontSave };
                root.SetActive(false);
                HandsLabLogger logger = null;
                try
                {
                    string directory = Path.Combine(evidenceDirectory, expectedLock.ToString());
                    var registry = new ResearchParticipantRegistry(Path.Combine(directory, "registry_TEST"));
                    var record = registry.CreateNext(ParticipantGroup.Test);
                    record = registry.StartOrResume(record.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller, TaskOrder.BA);
                    var flow = new ResearchSessionFlow(TaskOrder.BA, ResearchFlowState.Task1Active, 5);
                    var controller = root.AddComponent<ResearchSessionFlowController>();
                    controller.registryRoot = Path.Combine(directory, "registry_TEST");
                    var port = root.AddComponent<ResearchTaskPort>();
                    port.taskId = "B"; port.demoMode = true; port.demoPassed = true; port.contentApproved = false;
                    controller.taskPorts = new[] { port };
                    logger = new HandsLabLogger(Path.Combine(directory, "raw"), "SimulatedController", "Editor synthetic S1 controller snapshot fixture",
                        phase: "ResearchSession", taskId: "B", technicalTest: true, testData: true);
                    // Reflection only arranges an inactive unit fixture, never a played workflow or physical state.
                    Set(controller, "Registry", registry); Set(controller, "Record", record);
                    Set(controller, "Flow", flow); Set(controller, "Logger", logger);
                    var timingField = typeof(ResearchSessionFlowController).GetField("taskTiming", BindingFlags.Instance | BindingFlags.NonPublic);
                    bool timedRequest = timingField != null;
                    if (timedRequest)
                    {
                        var clock = timingField.GetValue(controller);
                        clock.GetType().GetMethod("Start").Invoke(clock, new object[] { logger.Timestamp });
                    }
                    int captures = 0, evaluations = 0;
                    S1TaskBState lastCaptured = null;
                    Func<object> capture = () =>
                    {
                        captures++;
                        lastCaptured = new S1TaskBState {
                            systemIntegrityValid = true, capturedAtSeconds = captures,
                            baseId = "TEST.base", frontGuardId = "TEST.front", bladeId = "TEST.blade",
                            spinnerId = "TEST.spinner", rearGuardId = "TEST.rear", rearLockId = "TEST.lock",
                            powerIsolated = true, baseStable = true,
                            frontGuardPlacement = S1Placement.Seated, frontGuardAligned = true, frontGuardClipped = true,
                            bladePlacement = S1Placement.Seated, bladeCompatible = true, bladeKeyAligned = true,
                            spinner = S1ThreadState.Tight, rearGuardPlacement = S1Placement.Seated, rearGuardAligned = true,
                            rearLock = captures == 1 ? expectedLock : expectedLock == S1ThreadState.Tight ? S1ThreadState.EngagedLoose : S1ThreadState.Tight
                        };
                        return lastCaptured;
                    };
                    Func<object, bool> evaluate = snapshot => { evaluations++; return ResearchS1TaskEvaluator.Evaluate((S1TaskBState)snapshot).Passed; };
                    var binding = typeof(ResearchTaskPort).GetMethod("BindSnapshot");
                    if (binding == null) port.Bind(capture, () => evaluate(lastCaptured), allowed => { });
                    else binding.Invoke(port, new object[] { capture, evaluate, new Action<bool>(allowed => { }) });
                    port.SetInteractionAllowed(true);
                    bool accepted = controller.Command("inspect", "SimulatedController");
                    int inspectionCaptures = captures;
                    logger.Complete("synthetic_controller_fixture_finished", "Tester", "SimulatedController");
                    var rows = File.ReadAllLines(logger.EventsPath).Select(JObject.Parse)
                        .Where(row => new[] { "INSPECT", "INSPECT_PASS", "INSPECT_FAIL", "TASK_COMPLETED" }.Contains((string)row["event"])).ToArray();
                    bool expectedPass = expectedLock == S1ThreadState.Tight;
                    bool rawMatches = rows.Length == (expectedPass && !timedRequest ? 3 : 2) && rows.All(row =>
                        (double?)row["task_state"]?["task_state"]?["capturedAtSeconds"] == 1 &&
                        (int?)row["task_state"]?["task_state"]?["rearLock"] == (int)expectedLock &&
                        (timedRequest ? (double?)row["before"]?["task_state"]?["capturedAtSeconds"] == 1 : (double?)row["after"]?["task_state"]?["capturedAtSeconds"] == 1));
                    bool eventMatches = rows.Any(row => (string)row["event"] == (expectedPass ? "INSPECT_PASS" : "INSPECT_FAIL"));
                    bool pass = (!timedRequest || controller.InspectionChecking && !controller.InspectionFeedbackVisible) && accepted && evaluations == 1 && inspectionCaptures == 1 && rawMatches && eventMatches && !port.IsReadyForStudy && !port.contentApproved;
                    if (!pass) failures++;
                    observations.Add(new { expected_lock = expectedLock.ToString(), expected_pass = expectedPass, accepted, timed_request = timedRequest,
                        captures = inspectionCaptures, evaluations, raw_matches = rawMatches, event_matches = eventMatches,
                        event_snapshots = rows, raw_events = logger.EventsPath, error = controller.Error, pass });
                }
                catch (Exception error) { failures++; observations.Add(new { expected_lock = expectedLock.ToString(), error = error.ToString(), pass = false }); }
                finally { if (logger != null) logger.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
            }
            bool dirtyUnchanged = scene.isDirty == dirtyBefore;
            if (!dirtyUnchanged) failures++;
            var result = new { status = failures == 0 ? "PASS" : "FAIL", failures, observations, scene_dirty_unchanged = dirtyUnchanged,
                scope = "Actual compiled Unity controller Command/Inspect/Log, port, evaluator, registry and runtime writer. Timed request fixture verifies pending/no-feedback; it does not advance/render the result. Inactive synthetic DTO component fixture; not UI, physical interaction, Play Mode E2E or hardware evidence.",
                hardware_verified = false, study_approved = false };
            File.WriteAllText(Path.Combine(evidenceDirectory, "result.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
            return "S1_CONTROLLER_SNAPSHOT_" + result.status + " failures=" + failures + " " + evidenceDirectory;
        }

        static void Set(ResearchSessionFlowController controller, string property, object value)
        {
            typeof(ResearchSessionFlowController).GetProperty(property).SetValue(controller, value, null);
        }
    }
}
#endif
