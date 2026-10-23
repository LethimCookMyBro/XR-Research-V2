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
                    var clock = timedRequest ? (ResearchTaskTiming)timingField.GetValue(controller) : null;
                    if (timedRequest) clock.Start(logger.Timestamp);
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
                    bool expectedPass = expectedLock == S1ThreadState.Tight;
                    bool pendingBeforeAdvance = !timedRequest || controller.InspectionChecking && !controller.InspectionFeedbackVisible;
                    bool readyBeforeAdvance = false;
                    bool? noViewReceiptAccepted = null;
                    if (timedRequest)
                    {
                        var offset = typeof(HandsLabLogger).GetField("timestampOffset", BindingFlags.Instance | BindingFlags.NonPublic);
                        offset.SetValue(logger, (double)offset.GetValue(logger) + 3.1d);
                        clock.Tick(logger.Timestamp);
                        readyBeforeAdvance = clock.Stage == ResearchInspectionStage.Ready;
                        typeof(ResearchSessionFlowController).GetMethod("AdvanceTaskTiming", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                        noViewReceiptAccepted = (bool)typeof(ResearchSessionFlowController).GetMethod("InspectionFeedbackRendered", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(controller, new object[] { controller.InspectionId, "Unrendered synthetic fixture text", 0 });
                    }
                    bool activeAfterAdvance = controller.Flow.State == ResearchFlowState.Task1Active;
                    bool resultAfterAdvance = controller.Flow.InspectPassed == expectedPass;
                    bool feedbackAfterAdvance = controller.InspectionFeedbackVisible;
                    bool endedAfterAdvance = clock?.Ended == true;
                    string stageAfterAdvance = clock?.Stage.ToString();
                    logger.Complete("synthetic_controller_fixture_finished", "Tester", "SimulatedController");
                    var allRows = File.ReadAllLines(logger.EventsPath).Select(JObject.Parse).ToArray();
                    var rows = allRows.Where(row => new[] { "INSPECT", "INSPECT_PASS", "INSPECT_FAIL", "TASK_COMPLETED" }.Contains((string)row["event"])).ToArray();
                    int prematureEvents = allRows.Count(row => new[] { "TASK_COMPLETED", "TASK_ENDED", "INSPECT_FEEDBACK_VISIBLE" }.Contains((string)row["event"]));
                    bool readyBranchPass = timedRequest && readyBeforeAdvance && activeAfterAdvance && resultAfterAdvance
                        && !feedbackAfterAdvance && !endedAfterAdvance && stageAfterAdvance == "Ready" && noViewReceiptAccepted == false && prematureEvents == 0;
                    bool rawMatches = rows.Length == (expectedPass && !timedRequest ? 3 : 2) && rows.All(row =>
                        (double?)row["task_state"]?["task_state"]?["capturedAtSeconds"] == 1 &&
                        (int?)row["task_state"]?["task_state"]?["rearLock"] == (int)expectedLock &&
                        (timedRequest ? (double?)row["before"]?["task_state"]?["capturedAtSeconds"] == 1 : (double?)row["after"]?["task_state"]?["capturedAtSeconds"] == 1));
                    bool eventMatches = rows.Any(row => (string)row["event"] == (expectedPass ? "INSPECT_PASS" : "INSPECT_FAIL"));
                    bool pass = pendingBeforeAdvance && accepted && evaluations == 1 && inspectionCaptures == 1 && rawMatches && eventMatches && !port.IsReadyForStudy && !port.contentApproved && readyBranchPass;
                    if (!pass) failures++;
                    observations.Add(new { expected_lock = expectedLock.ToString(), expected_pass = expectedPass, accepted, timed_request = timedRequest,
                        captures = inspectionCaptures, evaluations, raw_matches = rawMatches, event_matches = eventMatches, pending_before_advance = pendingBeforeAdvance,
                        ready_branch = new { expected = new { stage = "Ready", flow_state = "Task1Active", inspect_passed = expectedPass,
                                feedback_visible = false, ended = false, no_view_receipt_accepted = false, premature_events = 0 },
                            actual = new { ready_before_advance = readyBeforeAdvance, stage = stageAfterAdvance, flow_state = controller.Flow.State.ToString(),
                                inspect_passed = controller.Flow.InspectPassed, feedback_visible = feedbackAfterAdvance, ended = endedAfterAdvance,
                                no_view_receipt_accepted = noViewReceiptAccepted, premature_events = prematureEvents }, pass = readyBranchPass },
                        event_snapshots = rows, raw_events = logger.EventsPath, error = controller.Error, pass });
                }
                catch (Exception error) { failures++; observations.Add(new { expected_lock = expectedLock.ToString(), error = error.ToString(), pass = false }); }
                finally { if (logger != null) logger.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
            }
            bool dirtyUnchanged = scene.isDirty == dirtyBefore;
            if (!dirtyUnchanged) failures++;
            var result = new { status = failures == 0 ? "PASS" : "FAIL", failures, observations, scene_dirty_unchanged = dirtyUnchanged,
                scope = "Actual compiled Unity controller Command/Inspect/Log, port, evaluator, registry and runtime writer. Timed request fixture verifies initial pending and the production Ready branch after 3.1 synthetic seconds with no render receipt. It rejects no-view acknowledgement and premature completion/end events; it does not render feedback. Inactive synthetic DTO component fixture; not UI, physical interaction, Play Mode E2E or hardware evidence.",
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
