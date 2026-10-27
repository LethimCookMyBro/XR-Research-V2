#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchExposureChecks
    {
        public static string HiddenReader(string evidence)
        {
            Directory.CreateDirectory(evidence);
            var host = new GameObject("IsolatedExposureFixture"); host.SetActive(false);
            var c = host.AddComponent<ResearchSessionFlowController>();
            var registry = new ResearchParticipantRegistry(Path.Combine(evidence, "registry"));
            var record = registry.StartOrResume(registry.CreateNext(ParticipantGroup.Dev).participant_id,
                ParticipantLanguage.Thai, ExperimentInputMode.Controller, TaskOrder.AB);
            var logger = new HandsLabLogger(Path.Combine(evidence, "logs"), "None", "fixture",
                phase: "ResearchSession", runId: record.run_id, technicalTest: true, testData: true);
            typeof(ResearchSessionFlowController).GetProperty("Record").SetValue(c, record);
            typeof(ResearchSessionFlowController).GetProperty("Registry").SetValue(c, registry);
            typeof(ResearchSessionFlowController).GetProperty("Logger").SetValue(c, logger);
            typeof(ResearchSessionFlowController).GetProperty("Flow").SetValue(c,
                new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
            try
            {
                var timing = (ResearchTaskTiming)typeof(ResearchSessionFlowController).GetField("taskTiming", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(c);
            timing.Start(logger.Timestamp);
            if (!c.FloorReady || !timing.CanManipulate) throw new System.InvalidOperationException("Hidden Reader fixture must permit the open request.");
            c.OpenReference("Controller");
            if (!c.ReferenceOpen) throw new System.InvalidOperationException("Hidden Reader test did not open the Reader.");
                logger.Dispose();
                int opens = 0, requests = 0;
                foreach (var line in File.ReadAllLines(logger.EventsPath))
                    {
                    var row = JObject.Parse(line);
                    if ((string)row["event"] == "INFORMATION_OPEN") opens++;
                    if ((string)row["event"] == "REFERENCE_OPEN") requests++;
                }
                File.WriteAllText(Path.Combine(evidence, "hidden-exposure.json"), new JObject {
                    ["expected_opens"] = 0, ["actual_opens"] = opens,
                    ["reference_open"] = c.ReferenceOpen, ["rendered_reader"] = false,
                    ["test_data"] = true }.ToString());
                if (requests != 1) throw new System.InvalidOperationException("Expected one accepted REFERENCE_OPEN request.");
            if (opens != 0) throw new InvalidOperationException("Hidden/nonexistent reader counted as information exposure: " + opens);
                if (c.ReferenceSection != "") throw new InvalidOperationException("Task silently defaults to " + c.ReferenceSection + " before deliberate choice.");
                return "EXPOSURE_PASS hidden_opens=0; task_choice=neutral";
            }
            finally { logger.Dispose(); UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
#endif
