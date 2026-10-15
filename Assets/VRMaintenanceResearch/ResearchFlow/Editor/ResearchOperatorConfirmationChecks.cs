#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchOperatorConfirmationChecks
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, All).Invoke(o, args);
        static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, All).GetValue(o);
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();
        static string Hash(string p) { using (var s = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)); }
        static string[] CallerFiles(ResearchSessionFlowController c) => !c ? Array.Empty<string>() : new[] { c.Registry?.RegistryPath, c.Registry?.ProtocolPath, c.Logger?.EventsPath, c.Logger?.MovementPath, c.Logger?.MetadataPath }.Where(File.Exists).Select(p => p + ":" + Hash(p)).ToArray();
        static string State(ResearchSessionFlowController c) => JsonConvert.SerializeObject(new { Record = c?.Record, Flow = c?.Flow, Error = c?.Error });
        static Rect Bounds(Button button, Transform panel)
        {
            var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
            var points = corners.Select(panel.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(points.Min(v => v.x), points.Min(v => v.y), points.Max(v => v.x), points.Max(v => v.y));
        }
        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("Stopped Editor with XR inactive required.");
            string directory = Path.GetFullPath(evidenceDirectory), project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.IsPathRooted(evidenceDirectory) || Directory.Exists(directory) || (directory + Path.DirectorySeparatorChar).StartsWith(project, StringComparison.OrdinalIgnoreCase)) throw new IOException("Fresh absolute external directory required.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Authored three-font view required.");
            var type = typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned", BindingFlags.NonPublic);
            var before = Scenes(); var active = SceneManager.GetActiveScene(); int previews = EditorSceneManager.previewSceneCount;
            var previousEventSystem = EventSystem.current; var files = CallerFiles(donor.controller); string caller = State(donor.controller);
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var rows = new List<object>(); var errors = new List<string>(); int failures = 0;
            void Check(bool pass, string name, object data) { rows.Add(new { pass, name, data }); if (!pass) failures++; }
            Application.LogCallback onLog = (message, stack, kind) => { if (kind == LogType.Error || kind == LogType.Exception) errors.Add(message + "\n" + stack); };
            Directory.CreateDirectory(directory); Application.logMessageReceived += onLog;
            try
            {
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                foreach (var mode in new[] { "abort", "invalid" })
                foreach (var stage in new[] { "double", "cancel", "reason0", "reason1", "reason2", "reason3" })
                {
                    object fixture = null; string name = language + "/" + mode + "/" + stage;
                    try
                    {
                        string owned = Path.Combine(directory, language + "_" + mode + "_" + stage);
                        fixture = Activator.CreateInstance(type, All, null, new object[] { owned, donor, language }, null);
                        var c = Field<ResearchSessionFlowController>(fixture, "Controller"); var v = Field<ResearchFlowView>(fixture, "View");
                        var events = Field<EventSystem>(fixture, "eventSystem");
                        Call(fixture, "Click", (ResearchUIAction)Call(fixture, "Action", "task_start", null), 1);
                        Call(fixture, "Click", (ResearchUIAction)Call(fixture, "Action", "task_start_confirm", null), 1);
                        string identity = c.Record.participant_id + "/" + c.Record.session_id + "/" + c.Record.run_id; int credits = c.Flow.TutorialCompleted;
                        Check(c.Flow.IsTaskActive && !Field<ResearchTaskTiming>(c, "taskTiming").Ended, name + "/actual owned active task", State(c));
                        var setup = ResearcherSetupView.Build(v.transform, language == ParticipantLanguage.Thai ? donor.thaiFont : donor.japaneseFont);
                        setup.controller = c; setup.OperatorDirectoryOverride = Path.Combine(owned, "operator");
                        var canvas = setup.GetComponent<Canvas>(); canvas.enabled = true; canvas.targetDisplay = 0;
                        void Flush() { setup.Refresh(); Canvas.ForceUpdateCanvases(); }
                        void Click(Button b, int count = 1)
                        {
                            var data = new PointerEventData(events) { pointerId = 75201, button = PointerEventData.InputButton.Left, clickCount = count };
                            ExecuteEvents.Execute(b.gameObject, data, ExecuteEvents.pointerDownHandler);
                            ExecuteEvents.Execute(b.gameObject, data, ExecuteEvents.pointerUpHandler);
                            ExecuteEvents.Execute(b.gameObject, data, ExecuteEvents.pointerClickHandler); Flush();
                        }
                        Flush(); var request = Field<Button>(setup, mode == "abort" ? "abort" : "invalid"); var panel = request.transform.parent;
                        Vector2 firstCenter = Bounds(request, panel).center;
                        Click(request); var reasons = Field<Button[]>(setup, "reasonButtons");
                        Check(c.Record.session_state == RegistryState.InProgress, name + "/first request never ends", State(c));
                        if (stage == "double")
                        {
                            var hit = panel.GetComponentsInChildren<Button>(false).Where(b => b.IsInteractable() && Bounds(b, panel).Contains(firstCenter)).ToArray();
                            Check(hit.Length == 1, name + "/original coordinate has one button", hit.Select(b => b.name).ToArray());
                            if (hit.Length == 1) Click(hit[0], 2);
                            Check(c.Record.session_state == RegistryState.InProgress, name + "/same-coordinate second click never ends", State(c));
                        }
                        else if (stage == "cancel")
                        {
                            Click(request);
                            Check(c.Record.session_state == RegistryState.InProgress && !Field<bool>(setup, "confirmAbort") && !Field<bool>(setup, "confirmInvalid"), name + "/same control cancels both requests", State(c));
                        }
                        else
                        {
                            int index = int.Parse(stage.Substring(6)); var reason = reasons[index];
                            var requestBounds = Bounds(request, panel); var reasonBounds = Bounds(reason, panel);
                            Check(reason.gameObject.activeInHierarchy && reason.IsInteractable() && !requestBounds.Overlaps(reasonBounds), name + "/fresh reason control distinct and nonoverlapping", new { request = new[] { requestBounds.x, requestBounds.y, requestBounds.width, requestBounds.height }, reason = new[] { reasonBounds.x, reasonBounds.y, reasonBounds.width, reasonBounds.height } });
                            if (reason.gameObject.activeInHierarchy) Click(reason);
                            var saved = c.Registry.List(ParticipantGroup.Test).Single(); string expectedReason = ResearchParticipantRegistry.ResearcherTerminationReasons[index];
                            Check(saved.session_state == (mode == "invalid" ? RegistryState.Invalid : RegistryState.Aborted) && saved.termination_reason == expectedReason, name + "/terminal state and chosen reason persisted", saved);
                            string eventsPath = c.Logger.EventsPath;
                            JObject[] logged;
                            using (var reader = new StreamReader(new FileStream(eventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))) logged = reader.ReadToEnd().Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Select(JObject.Parse).ToArray();
                            Check(logged.Count(e => (string)e["event"] == "SESSION_ABORTED" && (string)e["reason"] == expectedReason) == 1 && logged.Any(e => (string)e["event"] == "TASK_TERMINATED" && (string)e["reason"] == expectedReason), name + "/actual terminal logger preserves chosen reason", eventsPath);
                            Check(c.taskPorts.All(p => !p.InteractionAllowed) && Field<ResearchTaskTiming>(c, "taskTiming").Ended, name + "/actual terminal timing and world gates", State(c));
                            c.WaitForExports(60000);
                            Check(!string.IsNullOrEmpty(c.LastExportPath) && Path.GetFullPath(c.LastExportPath).StartsWith(Path.GetFullPath(owned) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(c.LastExportPath, "COMPLETE.json")), name + "/owned export completed", new { c.LastExportPath, c.ExportDeliveryError });
                        }
                        Check(identity == c.Record.participant_id + "/" + c.Record.session_id + "/" + c.Record.run_id && credits == c.Flow.TutorialCompleted && c.Registry.List(ParticipantGroup.Test).Count == 1, name + "/identity credits and single owned record retained", State(c));
                    }
                    catch (Exception ex) { Check(false, name + "/execution", (ex.InnerException ?? ex).ToString()); }
                    finally { (fixture as IDisposable)?.Dispose(); }
                }
            }
            finally { Application.logMessageReceived -= onLog; foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value; }
            Check(errors.Count == 0, "no swallowed Unity callback failures", errors);
            Check(before.SequenceEqual(Scenes()) && active == SceneManager.GetActiveScene() && previews == EditorSceneManager.previewSceneCount && previousEventSystem == EventSystem.current, "caller scenes and EventSystem preserved", Scenes());
            Check(files.SequenceEqual(CallerFiles(donor.controller)) && caller == State(donor.controller), "caller registry logs and state preserved", null);
            string output = Path.Combine(directory, "operator-confirmation.json");
            File.WriteAllText(output, JsonConvert.SerializeObject(new { status = failures == 0 ? "PASS" : "FAIL", failures, rows, scope = "Stopped Editor owned Safety fixture, actual ResearcherSetupView Build/Refresh/Button ExecuteEvents down-up-click, actual local operator authorization, terminal registry/logger/timing/export. Second-click target selected from actual panel-local Button rects, not SDK ray acquisition. No real human input, Quest, DEV281 mutation or native Editor IMGUI event-loop coverage." }, Formatting.Indented));
            if (failures != 0) throw new InvalidOperationException("OPERATOR_CONFIRMATION_FAIL " + output);
            return "OPERATOR_CONFIRMATION_PASS " + output;
        }
    }
}
#endif
