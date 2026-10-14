#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchTaskComponentChecks
    {
        public static string Run(string evidenceDirectory)
        {
            var root = new GameObject("TaskAdapter_ExplicitTechnicalComponentFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var checks = new List<object>();
            void Check(bool pass, string name, object expected, object actual)
            { checks.Add(new { name, expected, actual, pass }); if (!pass) throw new InvalidOperationException(name); }
            Directory.CreateDirectory(evidenceDirectory);
            try
            {
                var component = new GameObject("FixtureConnector", typeof(BoxCollider)); component.transform.SetParent(root.transform, false);
                var shape = component.GetComponent<BoxCollider>();
                var port = root.AddComponent<ResearchTaskPort>(); port.taskId = "A"; port.demoMode = true; port.contentApproved = false; port.demoPassed = true;
                object State() => new { component_id = "fixture.connector", position_metres = new[] { component.transform.localPosition.x, component.transform.localPosition.y, component.transform.localPosition.z },
                    collider_enabled = shape.enabled, fixture_only = true };
                bool gate = true;
                port.Bind(State, () => shape.enabled && Vector3.Distance(component.transform.localPosition, new Vector3(1.25f, 0, 0)) < .001f, enabled => gate = enabled);
                port.BindExamineInfo((target, language) => "TEST FIXTURE LABEL: connector");
                Check(!gate && !port.TryInspect(out _), "Binding starts actual fixture interaction disabled", "disabled/unavailable", gate);
                port.SetInteractionAllowed(true);
                Check(port.TryInspect(out bool passed) && !passed, "Actual zero-position component fails despite demoPassed=true", "Fail", passed);
                using (var logger = new HandsLabLogger(Path.Combine(evidenceDirectory, "raw"), "SimulatedController", "Unity actual component fixture", phase: "ResearchSession", taskId: "A", technicalTest: true, testData: true))
                {
                    port.Behavior += (name, target, before, after) => logger.Record(name, target, "SyntheticFixtureObserved", port.CaptureState(), "Right", before, after, "Tester",
                        inputSourceOverride: "SimulatedController", phaseOverride: "Experiment", taskIdOverride: "A");
                    var before = State(); component.transform.localPosition = new Vector3(2.5f, 0, 0);
                    port.Action("fixture.connector", before, State(), "SimulatedController", true);
                    Check(port.TryInspect(out passed) && !passed, "Wrong actual component relocation remains Fail", "Fail at2.5m", passed);
                    before = State(); component.transform.localPosition = new Vector3(1.25f, 0, 0);
                    port.Action("fixture.connector", before, State(), "SimulatedController", true);
                    Check(port.TryInspect(out passed) && passed, "Actual fixture geometry-state predicate passes only authored technical fixture position", "Pass at1.25m", passed);
                    Check(port.ExamineInfo("fixture.connector", "English") == "TEST FIXTURE LABEL: connector", "Neutral observation does not reveal evaluator predicate", "label only", port.ExamineInfo("fixture.connector", "English"));
                    Check(!port.IsReadyForStudy && !port.contentApproved, "Technical geometry-state check does not approve real maintenance task", "not study-ready", port.IsReadyForStudy);
                    logger.Complete("generic_actual_component_fixture_finished", "Tester", "SimulatedController");
                    var saved = File.ReadAllLines(logger.EventsPath).Select(JObject.Parse).Where(e => (string)e["event"] == "REPAIR").ToArray();
                    Check(saved.Length == 2 && (double)saved[0]["after"]["component_state"]["position_metres"][0] == 2.5 &&
                        (double)saved[1]["after"]["task_state"]["task_state"]["position_metres"][0] == 1.25,
                        "Reopened raw events preserve actual wrong/action state and later full task DTO", "2repairs; wrong2.5->fixture1.25", saved.Length);
                    File.WriteAllText(Path.Combine(evidenceDirectory, "task-component-result.json"), JsonConvert.SerializeObject(new { status = "PASS", checks, raw_events = logger.EventsPath,
                        scope = "Actual inactive Unity Transform/BoxCollider state and generic adapter contract; synthetic fixture operations, no PC/fan mechanics or hardware/content approval", hardware_verified = false }, Formatting.Indented));
                }
                return "TASK_COMPONENT_PASS checks=" + checks.Count + " " + evidenceDirectory;
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(evidenceDirectory, "task-component-result.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, error = error.ToString(), hardware_verified = false }, Formatting.Indented));
                throw;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
#endif
