#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>
    /// Focused checks for the ADB researcher control channel's DEV/TEST/PILOT gate.
    ///
    /// These cover the defense-in-depth case that is unreachable on a standalone device:
    /// a NON-TECHNICAL current session must refuse resume / abort / demo / export.
    /// On standalone, Record is only ever set by StartSession, whose only caller is the
    /// channel itself, which refuses non-technical IDs first - so such a session cannot be
    /// created on the device. It is staged here in an Editor preview scene instead, using
    /// the same reflection fixture idiom as ResearchExamineChecks, rather than forcing the
    /// real device into an impossible production state.
    /// </summary>
    public static class ResearchAdbControlChannelChecks
    {
        [MenuItem("VR Research/Research Flow/Check ADB Control Channel Gate")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run checks in Edit Mode.");

            var checks = new List<string>();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("AdbChannel_Check_Fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var channel = root.AddComponent<ResearchAdbControlChannel>();
                // Awake() resolved whatever controller is in the open scene; bind the fixture one.
                SetField(channel, "controller", controller);
                SetProperty(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.PostTask1Checkpoint, 5));

                // ---- non-technical current session: every mutating command must refuse ----
                foreach (var group in new[] { ParticipantGroup.Thai, ParticipantGroup.Japanese })
                {
                    SetProperty(controller, "Record", Record(group, testData: false));
                    foreach (string command in new[] { "resume", "abort", "demo", "export", "benchmark", "benchmark_cancel" })
                    {
                        var result = Invoke(channel, command);
                        Check(result.Value<bool>("ok") == false,
                            group + " session refuses " + command, checks);
                        Check((result.Value<string>("error") ?? "").Contains("DEV/TEST/PILOT"),
                            group + " refusal for " + command + " names the technical-only rule", checks);
                    }
                }

                // A record that merely claims a technical group but is not test data is still refused.
                SetProperty(controller, "Record", Record(ParticipantGroup.Dev, testData: false));
                foreach (string command in new[] { "resume", "abort", "demo", "export", "benchmark", "benchmark_cancel" })
                    Check(Invoke(channel, command).Value<bool>("ok") == false,
                        "Dev group without test_data refuses " + command, checks);

                // ---- fail-closed on no session -------------------------------------------
                // controller.IsTechnical is `Record?.test_data ?? true`, which fails OPEN.
                // The channel guard must not inherit that.
                SetProperty(controller, "Record", null);
                Check(!Guard(channel, "demo", out _), "Null Record fails closed, not open", checks);

                // ---- positive: a genuine technical session passes the guard ---------------
                foreach (var group in new[] { ParticipantGroup.Dev, ParticipantGroup.Test, ParticipantGroup.Pilot })
                {
                    SetProperty(controller, "Record", Record(group, testData: true));
                    Check(Guard(channel, "resume", out _), group + " test-data session passes the guard", checks);
                }

                // Abort reason is a closed researcher choice; time_limit stays system-owned and unknown text never reaches the registry.
                foreach (string reason in new[] { "time_limit", "bogus", "" })
                {
                    var refused = JObject.Parse(JsonConvert.SerializeObject(typeof(ResearchAdbControlChannel).GetMethod("Abort", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(channel, new object[] { new JObject { ["reason"] = reason } })));
                    Check(refused.Value<bool>("ok") == false && (refused.Value<string>("error") ?? "").Contains("withdrawal, safety_stop, technical_failure, other")
                        && controller.Record.session_state == RegistryState.InProgress, "Abort refuses reason '" + reason + "' before ending the session", checks);
                }

                // Exercise the actual schema writer, not only the channel guards.
                string eventsPath;
                using (var logger = new VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger(
                    Path.GetFullPath("Logs/QuestFinalIntegration/adb-writer-regression"), "None", "ADB command schema fixture", technicalTest: true, testData: true))
                {
                    SetProperty(controller, "Logger", logger);
                    eventsPath = logger.EventsPath;
                    foreach (string command in new[] { "start", "resume", "abort", "demo", "export", "benchmark", "benchmark_cancel" })
                        typeof(ResearchAdbControlChannel).GetMethod("LogCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(channel, new object[] { command, "DEV01", "Controller", "AB", "Fixture" });
                }
                SetProperty(controller, "Logger", null);
                var events = new List<JObject>();
                foreach (string line in File.ReadAllLines(eventsPath))
                {
                    var row = JObject.Parse(line);
                    if ((string)row["event"] == "RESEARCHER_COMMAND") events.Add(row);
                }
                Check(events.Count == 7, "All active-session ADB commands reach the real logger", checks);
                Check(events.TrueForAll(row => (string)row["source"] == "Tester" && (string)row["after"]?["source"] == "ADB"),
                    "ADB technical provenance uses the supported source schema", checks);

                Write(checks, true, null);
                Debug.Log("RESEARCH_ADB_CHANNEL_GATE_PASS checks=" + checks.Count +
                          "; Editor fixture only, not hardware evidence.");
            }
            catch (Exception ex)
            {
                Write(checks, false, ex.Message);
                throw;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static ParticipantRecord Record(ParticipantGroup group, bool testData) => new ParticipantRecord
        {
            participant_id = (group == ParticipantGroup.Thai ? "TH" : group == ParticipantGroup.Japanese ? "JP" : group.ToString().ToUpperInvariant()) + "01",
            created_at = DateTime.UtcNow.ToString("O"),
            group = group,
            language = group == ParticipantGroup.Japanese ? ParticipantLanguage.Japanese : ParticipantLanguage.Thai,
            session_state = RegistryState.InProgress,
            test_data = testData
        };

        /// <summary>Invokes a private command method and normalises its anonymous result to JSON.</summary>
        static JObject Invoke(ResearchAdbControlChannel channel, string command)
        {
            var type = typeof(ResearchAdbControlChannel);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            object result;
            switch (command)
            {
                case "resume": result = type.GetMethod("Resume", flags).Invoke(channel, null); break;
                case "export": result = type.GetMethod("Export", flags).Invoke(channel, null); break;
                case "abort": result = type.GetMethod("Abort", flags).Invoke(channel, new object[] { new JObject() }); break;
                case "demo": result = type.GetMethod("Demo", flags).Invoke(channel, new object[] { new JObject { ["pass"] = true } }); break;
                case "benchmark": result = type.GetMethod("Benchmark", flags).Invoke(channel, new object[] { new JObject { ["mode"] = "resolution" } }); break;
                case "benchmark_cancel": result = type.GetMethod("CancelBenchmark", flags).Invoke(channel, null); break;
                default: throw new ArgumentException("Unknown command " + command);
            }
            return JObject.Parse(JsonConvert.SerializeObject(result));
        }

        static bool Guard(ResearchAdbControlChannel channel, string command, out string error)
        {
            var method = typeof(ResearchAdbControlChannel)
                .GetMethod("TechnicalSession", BindingFlags.NonPublic | BindingFlags.Instance);
            var args = new object[] { command, null };
            bool ok = (bool)method.Invoke(channel, args);
            error = (string)args[1];
            return ok;
        }

        static void SetProperty(object target, string property, object value) =>
            target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance).SetValue(target, value);

        static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        static void Check(bool passed, string name, List<string> checks)
        {
            if (!passed) throw new InvalidOperationException("FAILED: " + name);
            checks.Add(name);
        }

        static void Write(List<string> checks, bool passed, string error)
        {
            string dir = Directory.CreateDirectory(Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Logs", "QuestFinalQA", "final-audit"))).FullName;
            File.WriteAllText(Path.Combine(dir, "adb-control-channel-gate.json"),
                JsonConvert.SerializeObject(new
                {
                    capturedUtc = DateTime.UtcNow.ToString("O"),
                    passed,
                    error,
                    count = checks.Count,
                    checks,
                    scope = "Editor fixture only. Not hardware evidence. Covers the non-technical-session " +
                            "refusal path that is unreachable by construction on a standalone build."
                }, Formatting.Indented));
        }
    }
}
#endif
