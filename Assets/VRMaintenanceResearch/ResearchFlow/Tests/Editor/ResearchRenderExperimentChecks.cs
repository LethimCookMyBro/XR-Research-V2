#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchRenderExperimentChecks
    {
        [MenuItem("VR Research/Research Flow/Check Render Experiment Gates")]
        public static void Run()
        {
            int checks = 0;
            void Check(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException("Render experiment check failed: " + name);
                checks++;
            }
            Check(ResearchRenderExperiment.ValidMode("resolution"), "resolution accepted");
            Check(ResearchRenderExperiment.ValidMode("predicted_time"), "predicted time accepted");
            Check(!ResearchRenderExperiment.ValidMode("resolution,predicted_time"), "combined experiment refused");
            Check(!ResearchRenderExperiment.ValidMode(null), "missing mode refused");
            var completionHost = new GameObject("RenderCompletion_InactiveFixture") { hideFlags = HideFlags.HideAndDontSave };
            completionHost.SetActive(false);
            try
            {
                var experiment = completionHost.AddComponent<ResearchRenderExperiment>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                void Set(string name, object value) => typeof(ResearchRenderExperiment).GetField(name, flags).SetValue(experiment, value);
                bool Complete() => (bool)(typeof(ResearchRenderExperiment).GetProperty("Completed")?.GetValue(experiment) ?? false);
                Set("mode", "msaa"); Set("completedWindows", 3); Set("restoreVerified", true); Set("outcome", "aborted");
                Check(!Complete(), "aborted capture with successful restore is not complete");
                Set("outcome", "completed_windows");
                Check(Complete(), "all MSAA windows plus confirmed restore complete");
                Set("restoreVerified", null); Check(!Complete(), "requested restore without readback is incomplete");
                Set("restoreVerified", true); Set("mode", "resolution"); Check(!Complete(), "three windows are incomplete for resolution ladder");
                Set("completedWindows", 4); Check(Complete(), "four resolution windows plus confirmed restore complete");
                Set("mode", "unknown"); Check(!Complete(), "unknown mode cannot complete");
            }
            finally { UnityEngine.Object.DestroyImmediate(completionHost); }
            Check(ResearchRenderExperiment.OperationalReady(true, null), "ready floor and no session error accepted");
            Check(!ResearchRenderExperiment.OperationalReady(false, null), "floor loss invalidates capture");
            Check(!ResearchRenderExperiment.OperationalReady(true, "Inspect unavailable"), "session error invalidates capture even without a flow state change");
            Check(ResearchRenderExperiment.IsBaseline(1, false), "baseline accepted");
            Check(!ResearchRenderExperiment.IsBaseline(1.05f, false), "nonbaseline scale refused");
            Check(!ResearchRenderExperiment.IsBaseline(1, true), "nonbaseline timing refused");
            Check(!ResearchRenderExperiment.IsBaseline(float.NaN, false), "NaN scale refused");
            Check(ResearchRenderExperiment.SourceSettingsSupported(1.10f, false, 2) && ResearchRenderExperiment.SourceSettingsSupported(1.15f, false, 4),
                "supported persisted candidates can begin controlled comparisons");
            Check(!ResearchRenderExperiment.SourceSettingsSupported(1.20f, false, 2) && !ResearchRenderExperiment.SourceSettingsSupported(1.10f, true, 2)
                && !ResearchRenderExperiment.SourceSettingsSupported(1.10f, false, 8), "unsupported source scale, timing or MSAA refused");
            Check(ResearchRenderExperiment.ValidMode("msaa"), "msaa accepted");
            Check(!ResearchRenderExperiment.IsBaseline(1, false, 4), "nonbaseline MSAA refused");
            Check(ResearchRenderExperiment.PhaseMsaa("msaa", 1) == 4, "MSAA condition B is 4x");
            foreach (string mode in new[] { "resolution", "predicted_time", "msaa" })
                for (int phase = 0; phase < ResearchRenderExperiment.PhaseCount(mode); phase++)
                {
                    float scale = ResearchRenderExperiment.PhaseScale(mode, phase);
                    bool predicted = ResearchRenderExperiment.PhasePredicted(mode, phase);
                    int msaa = ResearchRenderExperiment.PhaseMsaa(mode, phase);
                    int changed = (scale > 1 ? 1 : 0) + (predicted ? 1 : 0) + (msaa != ResearchRenderExperiment.BaselineMsaa ? 1 : 0);
                    Check(changed <= 1, "resolution, timing and MSAA never combined");
                    bool comparison = mode == "resolution" ? phase > 0 && phase < ResearchRenderExperiment.PhaseCount(mode) - 1 : phase == 1;
                    Check(comparison ? changed == 1 : ResearchRenderExperiment.IsBaseline(scale, predicted, msaa),
                        "isolated A/B/C/D/restore or A/B/A conditions");
                }
            Check(ResearchRenderExperiment.PhaseCount("resolution") == 4 && ResearchRenderExperiment.PhaseScale("resolution", 0) == 1f &&
                ResearchRenderExperiment.PhaseScale("resolution", 1) == 1.10f && ResearchRenderExperiment.PhaseScale("resolution", 2) == 1.15f &&
                ResearchRenderExperiment.PhaseScale("resolution", 3) == 1f, "ladder 1.00/1.10/1.15/restore 1.00");
            Check(ResearchRenderExperiment.SupportedScale(1.10f) && ResearchRenderExperiment.SupportedScale(1.15f) && !ResearchRenderExperiment.SupportedScale(1.20f),
                "MSAA scale comes from the ladder");
            Check(ResearchRenderExperiment.SupportedScale((float)1.1d) && ResearchRenderExperiment.SupportedScale((float)1.15d), "JSON selected_scale doubles map onto ladder floats");
            Check(!ResearchRenderExperiment.SupportedScale(0) && !ResearchRenderExperiment.SupportedScale(float.NaN), "missing or invalid selected resolution refused");
            foreach (float selected in new[] { 1f, 1.10f, 1.15f })
                for (int phase = 0; phase < 3; phase++)
                    Check(ResearchRenderExperiment.PhaseScale("msaa", phase, selected) == selected && !ResearchRenderExperiment.PhasePredicted("msaa", phase),
                        "MSAA comparison holds selected scale and timing constant");
            // Actual eye size must follow the requested scale; 1.10 and 1.15 cannot share one size.
            Check(ResearchRenderExperiment.DimensionsMatch(1680, 1848, 1.10f) && ResearchRenderExperiment.DimensionsMatch(1760, 2024, 1.15f), "scaled eye size accepted");
            Check(ResearchRenderExperiment.DimensionsMatch(1680, 1856, 1.10f), "runtime alignment (32 px) tolerated");
            Check(!ResearchRenderExperiment.DimensionsMatch(1680, 1848, 1.15f) && !ResearchRenderExperiment.DimensionsMatch(1680, 1932, 1.10f), "1.10 and 1.15 sizes are distinguishable");
            Check(!ResearchRenderExperiment.DimensionsMatch(1680, 1680, 1.10f), "unchanged size at 1.10 aborts");
            // Restore counts only when the native readback is back at the baseline.
            Check(ResearchRenderExperiment.Restored(1, 1, 2, 2, false, 1680, 1760, 1680, 1760), "native baseline readback accepted");
            Check(!ResearchRenderExperiment.Restored(1, 1.15f, 2, 2, false, 1932, 2024, 1680, 1760), "stale native target scale refused");
            Check(!ResearchRenderExperiment.Restored(1, 1, 2, 4, false, 1680, 1760, 1680, 1760), "stale native MSAA refused");
            Check(!ResearchRenderExperiment.Restored(1, 1, 2, 2, true, 1680, 1760, 1680, 1760), "predicted time left on refused");
            Check(!ResearchRenderExperiment.Restored(1, 1, 2, 2, false, 1848, 1936, 1680, 1760), "eye size not back to baseline refused");
            Check(ResearchRenderExperiment.Restored(1.10f, 1.10f, 2, 2, false, 1848, 1936, 1848, 1936, 1.10f, 2), "persisted candidate restored exactly");
            Check(!ResearchRenderExperiment.Restored(1, 1, 2, 2, false, 1680, 1760, 1848, 1936, 1.10f, 2), "hardcoded 1.00 is not restoration of a 1.10 source");
            Check(ResearchRenderExperiment.DimensionsMatch(1848, 1680, 1f / 1.10f), "resolution ladder can start from a persisted candidate");
            var baselinePasses = new[] { 1680, 1760, 2, 1680, 1760, 2 };
            Check(ResearchRenderExperiment.PassesRestored(new[] { 1680, 1760, 2, 1680, 1760, 2 }, baselinePasses), "every render pass back at baseline accepted");
            Check(!ResearchRenderExperiment.PassesRestored(new[] { 1680, 1760, 2, 1932, 2024, 2 }, baselinePasses), "second pass still scaled refused");
            Check(!ResearchRenderExperiment.PassesRestored(new[] { 1680, 1760, 2, 1680, 1760, 4 }, baselinePasses), "second pass MSAA refused");
            Check(!ResearchRenderExperiment.PassesRestored(new[] { 1680, 1760, 2 }, baselinePasses), "render pass count change refused");
            Check(!ResearchRenderExperiment.PassesRestored(null, baselinePasses) && !ResearchRenderExperiment.PassesRestored(baselinePasses, null), "missing readback is not a restore");
            Check(ResearchRenderExperiment.PassDimensionsMatch(new[] { 1680, 1760, 4, 1680, 1760, 4 }, baselinePasses), "MSAA comparison keeps every pass dimension while varying sample count");
            Check(!ResearchRenderExperiment.PassDimensionsMatch(new[] { 1712, 1760, 2, 1680, 1760, 2 }, baselinePasses), "repeated A alignment drift is refused despite scale tolerance");
            Check(!ResearchRenderExperiment.PassDimensionsMatch(new[] { 1680, 1760, 2, 1680, 1792, 2 }, baselinePasses), "matched windows require second-pass dimensions too");
            Check(!ResearchRenderExperiment.PassDimensionsMatch(null, baselinePasses) && !ResearchRenderExperiment.PassDimensionsMatch(new[] { 1680, 1760 }, baselinePasses), "missing or malformed pass dimensions refused");
            Check(ResearchRenderExperiment.DisplayRestored(1, 72, 0, 0, "None", "None"), "baseline viewport/refresh/foveation accepted");
            Check(!ResearchRenderExperiment.DisplayRestored(.9f, 72, 0, 0, "None", "None"), "viewport scale refused");
            Check(!ResearchRenderExperiment.DisplayRestored(1, 90, 0, 0, "None", "None"), "refresh change refused");
            Check(!ResearchRenderExperiment.DisplayRestored(1, 72, 100, 0, "None", "None"), "foveation level change refused");
            Check(!ResearchRenderExperiment.DisplayRestored(1, 72, 0, 0, "GazeAllowed", "None"), "foveation flags change refused");
            Check(ResearchRenderExperiment.MayBegin(false, false, 0) && !ResearchRenderExperiment.MayBegin(false, true, 0)
                && !ResearchRenderExperiment.MayBegin(false, false, 3) && !ResearchRenderExperiment.MayBegin(true, false, 0), "latched, pending or running experiment blocks Begin");
            // Restore latch: a finished experiment whose native readback fails (or never happens) must block the next benchmark.
            var host = new GameObject("RenderRestoreLatchFixture") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var experiment = host.AddComponent<ResearchRenderExperiment>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                void Set(string field, object value) => typeof(ResearchRenderExperiment).GetField(field, flags).SetValue(experiment, value);
                void Call(string method, params object[] args) => typeof(ResearchRenderExperiment).GetMethod(method, flags).Invoke(experiment, args);
                Set("restoreRequired", true);
                Call("Finish", "completed_windows", "fixture");           // requests the baseline, schedules the readback
                Set("restoreCheckFrames", 0); Call("VerifyRestore");       // readback cannot confirm the baseline (no native display)
                Check(experiment.Begin(null, "resolution", out string latchError) == false && latchError != null && latchError.Contains("restore"),
                    "failed native restore readback latches and blocks a new benchmark: " + latchError);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
            Check(!ResearchRenderExperiment.CaptureReady(4.999), "warmup cannot finish early");
            Check(ResearchRenderExperiment.CaptureReady(5), "warmup boundary");
            Check(!ResearchRenderExperiment.WindowComplete(29.999), "capture cannot finish early");
            Check(ResearchRenderExperiment.WindowComplete(30), "capture boundary");
            var tracked = InputTrackingState.Position | InputTrackingState.Rotation;
            bool Ready(bool focus = true, bool valid = true, bool tracking = true, bool known = true,
                bool present = true, InputTrackingState? flags = null, int width = 1680, int height = 1760, int passes = 1) =>
                ResearchRenderExperiment.NativeReady(focus, valid, tracking, known, present, flags ?? tracked, width, height, passes);
            Check(Ready(), "valid native eye rendering accepted");
            Check(!Ready(focus: false), "focus required");
            Check(!Ready(valid: false), "native device required");
            Check(!Ready(tracking: false), "tracking required");
            Check(!Ready(known: false), "unknown presence refused");
            Check(!Ready(present: false), "unworn refused");
            Check(!Ready(flags: InputTrackingState.Rotation), "position tracking required");
            Check(!Ready(flags: InputTrackingState.Position), "rotation tracking required");
            Check(!Ready(width: 0), "eye width required");
            Check(!Ready(height: 0), "eye height required");
            Check(!Ready(passes: 0), "render pass required");

            // 1.10 / 4x MSAA chosen on Quest 3 (view_probe 20261014_091431: shimmer 0.288 -> 0.241 vs 2x, app GPU 9.0 -> 9.5 ms idle);
            // controlled experiments keep their independent 1.00 / 2x A condition.
            int android = int.Parse(Regex.Match(File.ReadAllText("ProjectSettings/QualitySettings.asset"), @"\n    Android: (\d+)").Groups[1].Value);
            var urp = QualitySettings.GetRenderPipelineAssetAt(android) as UniversalRenderPipelineAsset;
            Check(urp && Mathf.Abs(urp.renderScale - 1.10f) < .0001f && urp.msaaSampleCount == 4, "Android URP asset is the device-measured 1.10/4x candidate");
            Check(!urp.supportsHDR && !urp.supportsCameraDepthTexture && !urp.supportsCameraOpaqueTexture, "Android HDR, depth and opaque textures off");
            Check(((UniversalRendererData)urp.rendererDataList[0]).intermediateTextureMode == IntermediateTextureMode.Auto, "intermediate texture Auto");
            foreach (string name in new[] { "V2_Floor_BaseColor_1K", "V2_Wood_BaseColor_1K" })
            {
                var texture = (TextureImporter)AssetImporter.GetAtPath("Assets/VRMaintenanceResearch/Assets/V2/Environment/textures/" + name + ".png");
                Check(texture.mipmapEnabled && texture.filterMode == FilterMode.Trilinear && texture.anisoLevel >= 4, name + " trilinear aniso 4");
            }
            var renderReadback = Newtonsoft.Json.Linq.JObject.FromObject(ResearchRenderExperiment.CurrentSettings());
            Check(renderReadback.Property("xr_render_viewport_scale") != null && renderReadback.Property("xr_applied_viewport_scale") != null &&
                renderReadback.Property("viewport_sample_timing") != null, "requested and applied viewport evidence are distinct and frame scoped");
            if (!(bool)renderReadback["xr_enabled"] || (int)renderReadback["running_displays"] != 1)
                Check(renderReadback["xr_render_viewport_scale"].Type == Newtonsoft.Json.Linq.JTokenType.Null &&
                    renderReadback["xr_applied_viewport_scale"].Type == Newtonsoft.Json.Linq.JTokenType.Null &&
                    (string)renderReadback["viewport_sample_timing"] == "unavailable", "inactive XR has no native viewport evidence");
            Debug.Log("RESEARCH_RENDER_EXPERIMENT_CHECKS_PASS " + checks + "; offline gates only; hardware_verified=false");
        }
    }
}
#endif
