#if UNITY_EDITOR
using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchTechnicalScenarioChecks
    {
        public static string CheckRenderModeDispatch(string evidenceFolder)
        {
            Directory.CreateDirectory(evidenceFolder);
            var root = new GameObject("RenderMode_InactiveStudyGuardFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var rows = new System.Collections.Generic.List<object>();
            try
            {
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var record = new ParticipantRecord { participant_id = "TH_RENDER_MODE_FIXTURE", group = ParticipantGroup.Thai, test_data = false };
                typeof(ResearchSessionFlowController).GetProperty("Record").GetSetMethod(true).Invoke(controller, new object[] { record });
                var channel = root.AddComponent<ResearchAdbControlChannel>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(ResearchAdbControlChannel).GetField("controller", flags).SetValue(channel, controller);
                foreach (var mode in new[] { "msaa", "resolution", null, "", "MSAA", "predicted_time", "resolution,msaa", "  resolution", "unknown" })
                {
                    var request = new Newtonsoft.Json.Linq.JObject { ["simulated_presence"] = true };
                    if (mode != null) request["render_mode"] = mode;
                    var result = typeof(ResearchAdbControlChannel).GetMethod("Execute", flags).Invoke(channel, new object[] { "technical_scenario", request });
                    var response = Newtonsoft.Json.Linq.JObject.FromObject(result);
                    var scenario = root.GetComponent<ResearchTechnicalScenario>();
                    string error = (string)response["error"] ?? "";
                    bool supported = mode == null || mode == "msaa" || mode == "resolution";
                    bool pass = (bool?)response["ok"] == false && (supported ? error.Contains("idle") : error.Contains("render_mode"))
                        && controller.Record == record && controller.Registry == null && controller.Flow == null
                        && scenario && !scenario.Running && scenario.EvidencePath == null;
                    rows.Add(new { mode = mode ?? "<omitted>", supported, pass, error, registry_uncreated = controller.Registry == null });
                }
                var json = Newtonsoft.Json.Linq.JArray.FromObject(rows);
                bool passed = System.Linq.Enumerable.All(json, r => (bool)r["pass"]);
                File.WriteAllText(Path.Combine(evidenceFolder, "render-mode-dispatch.json"), JsonConvert.SerializeObject(new {
                    status = passed ? "PASS" : "FAIL", rows, scope = "Actual inactive channel dispatch with a study DTO; no registry, session or render experiment starts", hardware_verified = false
                }, Formatting.Indented));
                if (!passed) throw new InvalidOperationException("Render mode dispatch did not reject unsupported modes before mutation.");
                return "RENDER_MODE_DISPATCH_PASS " + rows.Count;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string CheckOwnedRenderCancel(string evidenceFolder)
        {
            Directory.CreateDirectory(evidenceFolder);
            var root = new GameObject("RenderCancel_InactiveOwnedFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            try
            {
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var scenario = root.AddComponent<ResearchTechnicalScenario>();
                var comparison = root.AddComponent<ResearchRenderExperiment>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(ResearchTechnicalScenario).GetField("owner", flags).SetValue(scenario, controller);
                typeof(ResearchTechnicalScenario).GetField("<Running>k__BackingField", flags).SetValue(scenario, true);
                typeof(ResearchRenderExperiment).GetField("<Running>k__BackingField", flags).SetValue(comparison, true);
                typeof(ResearchSessionFlowController).GetProperty("RenderComparison", flags).SetValue(controller, comparison);
                scenario.Cancel();
                bool pass = !scenario.Running && !comparison.Running && !comparison.Completed && comparison.RestoreVerified == null
                    && controller.Record == null && controller.Registry == null;
                File.WriteAllText(Path.Combine(evidenceFolder, "owned-render-cancel.json"), JsonConvert.SerializeObject(new {
                    status = pass ? "PASS" : "FAIL", scenario = scenario.State, render = comparison.Status,
                    scope = "Inactive owned fixture with no allocated native target or session; cancellation only, no native restore claim", hardware_verified = false
                }, Formatting.Indented));
                if (!pass) throw new InvalidOperationException("Owned render comparison survived scenario cancellation.");
                return "OWNED_RENDER_CANCEL_PASS";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string Begin(string evidenceFolder, string requestedRenderMode = "msaa")
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Fresh Play Mode required.");
            var controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            if (!controller || controller.Record != null || controller.Flow != null) throw new InvalidOperationException("No existing session allowed.");
            if (ResearchRenderExperiment.NativeReady(true, true, false, true, true, InputTrackingState.None, 1848, 1936, 1))
                throw new InvalidOperationException("Untracked native rendering must not pass the ordinary readiness gate.");
            var scenario = controller.gameObject.AddComponent<ResearchTechnicalScenario>();
            ResearchFlowVisualInput.Begin(1.65f);
            string error;
            if (!scenario.Begin(controller, false, true, out error, requestedRenderMode)) { ResearchFlowVisualInput.Stop(); throw new InvalidOperationException(error); }
            string repeatError;
            bool rejectedRepeat = !scenario.Begin(controller, false, true, out repeatError);
            Directory.CreateDirectory(evidenceFolder);
            File.WriteAllText(Path.Combine(evidenceFolder, "scenario-start.json"), JsonConvert.SerializeObject(new {
                scenario.EvidencePath, render_mode = requestedRenderMode, repeated_start_rejected = rejectedRepeat, repeatError,
                native_untracked_gate_rejected = true, synthetic_input = true, hardware_verified = false }, Formatting.Indented));
            if (!rejectedRepeat) throw new InvalidOperationException("Scenario replaced an existing run.");
            return scenario.EvidencePath;
        }
    }
}
#endif
