#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchStopFallbackChecks
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, All).Invoke(o, args);
        static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, All).GetValue(o);
        static void Set(object o, string name, object value) => o.GetType().GetProperty(name, All).GetSetMethod(true).Invoke(o, new[] { value });
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();
        static string Hash(string p) { using (var s = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)); }
        static string[] CallerFiles(ResearchSessionFlowController c) => !c ? Array.Empty<string>() : new[] { c.Registry?.RegistryPath, c.Registry?.ProtocolPath, c.Logger?.EventsPath, c.Logger?.MovementPath, c.Logger?.MetadataPath }.Where(File.Exists).Select(p => p + ":" + Hash(p)).ToArray();
        static string State(ResearchSessionFlowController c) => JsonConvert.SerializeObject(new { Record=c?.Record, Flow=c?.Flow, Error=c?.Error });
        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("Stopped Editor with XR inactive required.");
            if (!Path.IsPathRooted(evidenceDirectory)) throw new ArgumentException("Fresh absolute external directory required.");
            string directory = Path.GetFullPath(evidenceDirectory), project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if ((directory + Path.DirectorySeparatorChar).StartsWith(project, StringComparison.OrdinalIgnoreCase) || Directory.Exists(directory)) throw new IOException("Use fresh external evidence space.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Authored view/fonts required.");
            var type = typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned", BindingFlags.NonPublic);
            var before = Scenes(); var active = SceneManager.GetActiveScene(); int previews = EditorSceneManager.previewSceneCount;
            var previousEventSystem = EventSystem.current; var files = CallerFiles(donor.controller); string caller = State(donor.controller);
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var rows = new List<object>(); var captures = new List<object>(); var errors = new List<string>(); int failures = 0;
            void Check(bool pass, string name, object data) { rows.Add(new { pass, name, data }); if (!pass) failures++; }
            Application.LogCallback onLog = (message, stack, kind) => { if (kind == LogType.Error || kind == LogType.Exception) errors.Add(message + "\n" + stack); };
            Directory.CreateDirectory(directory); Application.logMessageReceived += onLog;
            try
            {
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                foreach (var stage in new[] { "closed", "minimized_reader", "error", "writer_fault" })
                {
                    object fixture = null; string name = language + "/" + stage;
                    try
                    {
                        fixture = Activator.CreateInstance(type, All, null, new object[] { Path.Combine(directory, language + "_" + stage), donor, language }, null);
                        var c = Field<ResearchSessionFlowController>(fixture, "Controller"); var v = Field<ResearchFlowView>(fixture, "View");
                        void Flush() { Call(fixture, "Refresh"); v.RefreshRecoveryUtility(); Call(fixture, "Refresh"); }
                        ResearchUIAction Action(string command) => (ResearchUIAction)Call(fixture, "Action", command, null);
                        void Click(string command) { Flush(); Call(fixture, "Click", Action(command), 1); Flush(); }
                        string identity = c.Record.participant_id + "/" + c.Record.session_id + "/" + c.Record.run_id; int credits = c.Flow.TutorialCompleted;
                        Click("task_start"); Click("task_start_confirm");
                        var timing = Field<ResearchTaskTiming>(c, "taskTiming");
                        Check(c.Flow.IsTaskActive && timing.Started && !timing.Ended && string.IsNullOrEmpty(c.Error), name + "/actual active task", State(c));
                        Click("main_minimize");
                        Check(Field<bool>(v, "mainMinimized"), name + "/actual minimize", null);
                        if (stage == "minimized_reader") { Set(c, "ReferenceOpen", true); Set(c, "ReferenceMinimized", true); }
                        if (stage == "error") Set(c, "Error", "fixture existing error");
                        if (stage == "writer_fault") Call(c, "LoggerFailed", "fixture writer fault callback");
                        Flush();
                        var stops = v.GetComponentsInChildren<ResearchUIAction>(false).Where(a => a.command == "stop" && a.GetComponent<Button>().IsInteractable()).ToArray();
                        Check(stops.Length == 1, name + "/exactly one visible Stop", stops.Select(a => a.name).ToArray());
                        if (stops.Length != 1) continue;
                        var stop = stops[0]; var panel = stop.GetComponentInParent<ResearchPanelMotion>(); var group = stop.GetComponentInParent<CanvasGroup>();
                        Check(panel && panel.GetComponent<Canvas>().enabled && panel.IsReadableFromHead && !panel.PlacementBlocked && (!group || (group.interactable && group.blocksRaycasts && group.alpha > .01f)), name + "/Stop surface readable and raycastable", panel ? panel.name : "none");
                        Call(fixture, "Capture", "before", panel, captures, rows);
                        Call(fixture, "Click", stop, 1); Flush();
                        Check(c.Flow.Paused && c.PauseKind == "stop" && !c.ParticipantMayResume && c.taskPorts.All(p => !p.InteractionAllowed), name + "/Stop suspends without self resume or world interaction", State(c));
                        Check(stage == "writer_fault" ? c.TaskEndReason == "technical_failure" && timing.Ended : !timing.Ended, name + "/original task termination policy retained", c.TaskEndReason);
                        Check(stage != "error" || c.Error.Contains("fixture existing error"), name + "/existing error retained", c.Error);
                        Check(!c.Command("session_resume", "None"), name + "/participant cannot self resume", null);
                        Check(identity == c.Record.participant_id + "/" + c.Record.session_id + "/" + c.Record.run_id && credits == c.Flow.TutorialCompleted && c.Record.session_state == RegistryState.InProgress && c.Registry.List(ParticipantGroup.Test).Count == 1, name + "/identity credits and single open record retained", State(c));
                        Call(fixture, "Capture", "after", v.mainPanel, captures, rows);
                        Check(Field<List<string>>(fixture, "TextOverflows").Count == 0, name + "/no visible text overflow", Field<List<string>>(fixture, "TextOverflows"));
                        rows.Add(new { name = name + "/writer", c.Logger.EventsPath, sha256 = Hash(c.Logger.EventsPath) });
                    }
                    catch (Exception ex) { Check(false, name + "/execution", (ex.InnerException ?? ex).ToString()); }
                    finally { (fixture as IDisposable)?.Dispose(); }
                }
            }
            finally { Application.logMessageReceived -= onLog; foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value; }
            Check(errors.Count == 0, "no swallowed Unity callback failures", errors);
            Check(before.SequenceEqual(Scenes()) && active == SceneManager.GetActiveScene() && previews == EditorSceneManager.previewSceneCount && previousEventSystem == EventSystem.current, "caller scenes and EventSystem preserved", Scenes());
            Check(files.SequenceEqual(CallerFiles(donor.controller)) && caller == State(donor.controller), "caller registry logs and state preserved", null);
            string output = Path.Combine(directory, "stop-fallback.json");
            File.WriteAllText(output, JsonConvert.SerializeObject(new { status = failures == 0 ? "PASS" : "FAIL", failures, rows, captures, scope = "Stopped Editor actual production refresh and EventSystem callbacks through existing Safety Owned fixture. Main minimize is a real UI callback. Reader open/minimized and existing error are seeded; writer fault calls real LoggerFailed, not a disk fault. Real owned controller/registry/logger and mono capture. No SDK ray acquisition, Quest, wearer, language approval or restored legacy session." }, Formatting.Indented));
            if (failures != 0) throw new InvalidOperationException("STOP_FALLBACK_FAIL " + output);
            return "STOP_FALLBACK_PASS " + output;
        }
    }
}
#endif
