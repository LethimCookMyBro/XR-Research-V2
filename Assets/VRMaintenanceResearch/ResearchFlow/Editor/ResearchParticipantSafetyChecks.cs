#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchParticipantSafetyChecks
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Type FixtureType = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor with XR subsystem inactive is required.");
            string directory = Path.GetFullPath(evidenceDirectory);
            if (Directory.Exists(directory)) throw new IOException("Use a fresh external evidence directory.");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if ((directory + Path.DirectorySeparatorChar).StartsWith(project, StringComparison.OrdinalIgnoreCase)) throw new IOException("Evidence must be external to project.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Authored three-font view is required.");
            var scenes = SceneStates(); var active = SceneManager.GetActiveScene(); var previousEventSystem = EventSystem.current;
            string caller = Caller(donor.controller); string[] callerFiles = CallerFiles(donor.controller);
            Directory.CreateDirectory(directory);
            var rows = new List<object>(); var captures = new List<object>(); var failures = new List<string>();
            var callbackErrors = new List<string>(); Application.LogCallback onLog = (message, stack, type) => { if(type == LogType.Exception || type == LogType.Error) callbackErrors.Add(message + "\n" + stack); };
            Application.logMessageReceived += onLog;
            Action<bool,string,object> check = (pass, name, data) => { rows.Add(new { pass, name, data }); if (!pass) failures.Add(name); };
            foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
            foreach (string stage in new[] { "confirmation", "task_B", "stop_main", "stop_reader", "stop_error", "stop_writer_fault", "checkpoint", "guard_tutorial_start", "guard_tutorial_continue", "guard_task_start", "guard_task_start_confirm", "guard_inspect", "guard_inspect_continue", "guard_technical_resume", "stop_tester_resume", "tutorial_not_counted" })
            {
                try
                {
                    using (var f = new Owned(Path.Combine(directory, language + "_" + stage), donor, language))
                    {
                        string name = language + "/" + stage; var c = f.Controller;
                        if(stage.StartsWith("guard_", StringComparison.Ordinal)) PrepareGuard(f, stage.Substring(6));
                        string identity = Identity(c); int credits = c.Flow.TutorialCompleted;
                        if(stage.StartsWith("guard_", StringComparison.Ordinal)) CheckGuard(f, stage.Substring(6), name, check);
                        else if (stage == "confirmation")
                        {
                            var start = f.Action("task_start"); f.Click(start, 2);
                            check(!c.TaskStartConfirmation && c.Flow.State == ResearchFlowState.Task1Ready, name + "/double start rejected", State(c));
                            f.Click(start);
                            check(c.TaskStartConfirmation && c.Flow.State == ResearchFlowState.Task1Ready && !Timing(c).Started, name + "/first click confirms without starting clock", State(c));
                            f.Capture("confirm", f.View.mainPanel, captures, rows);
                            f.Click(f.Action("task_start_confirm"), 2);
                            check(c.TaskStartConfirmation && c.Flow.State == ResearchFlowState.Task1Ready, name + "/double confirm rejected", State(c));
                            f.Click(f.Action("task_start_back"));
                            check(!c.TaskStartConfirmation && c.Flow.State == ResearchFlowState.Task1Ready, name + "/back remains ready", State(c));
                            start = f.Action("task_start"); f.Down(start); Set(c, "TaskStartConfirmation", true); f.UpClick(start);
                            check(c.Flow.State == ResearchFlowState.Task1Ready && !Timing(c).Started, name + "/changed context rejects release", State(c));
                            f.Refresh(); f.Click(f.Action("task_start_back")); f.Click(f.Action("task_start")); f.Click(f.Action("task_start_confirm"));
                            check(c.Flow.State == ResearchFlowState.Task1Active && Timing(c).Started && !Timing(c).Ended && string.IsNullOrEmpty(c.Error), name + "/fresh confirm starts actual task", State(c));
                            var inspect = f.Action("inspect"); f.Click(inspect, 2);
                            check(!c.InspectionChecking && !c.Flow.InspectPassed.HasValue, name + "/double inspect rejected", State(c));
                            f.Down(inspect); Set(c, "TaskStartConfirmation", true); f.UpClick(inspect); Set(c, "TaskStartConfirmation", false);
                            check(!c.InspectionChecking && !c.Flow.InspectPassed.HasValue, name + "/changed context inspect rejected", State(c));
                            string raw = Read(f.Writer.EventsPath);
                            check(Count(raw, "TASK_STARTED") == 1 && Count(raw, "TASK_START_CANCELLED") == 2 && Count(raw, "INSPECT_PASS") == 0 && Count(raw, "INSPECT_FAIL") == 0,
                                name + "/actual writer one start and no accidental inspect result", new { f.Writer.EventsPath, starts = Count(raw,"TASK_STARTED"), cancels = Count(raw,"TASK_START_CANCELLED") });
                            f.Capture("task_A", f.View.mainPanel, captures, rows);
                        }
                        else if (stage == "task_B")
                        {
                            f.Seed(ResearchFlowState.Task2Ready); f.Click(f.Action("task_start")); f.Click(f.Action("task_start_confirm"));
                            check(c.Flow.State == ResearchFlowState.Task2Active && !Timing(c).Ended && string.IsNullOrEmpty(c.Error), name + "/actual task B starts", State(c));
                            f.Capture("task_B", f.View.mainPanel, captures, rows);
                        }
                        else if (stage == "checkpoint")
                        {
                            f.Seed(ResearchFlowState.PostTask1Checkpoint); Set(c, "TechnicalSelfTest", true); f.Refresh();
                            var resume = f.Action("technical_resume"); f.Click(resume, 2);
                            check(c.Flow.State == ResearchFlowState.PostTask1Checkpoint, name + "/double checkpoint rejected", State(c));
                            f.Down(resume); Set(c, "Error", "fixture context changed"); f.UpClick(resume);
                            check(c.Flow.State == ResearchFlowState.PostTask1Checkpoint && Count(Read(f.Writer.EventsPath), "CHECKPOINT_COMPLETED") == 0, name + "/context change cannot confirm checkpoint", State(c));
                        }
                        else if (stage == "tutorial_not_counted")
                        {
                            f.Seed(ResearchFlowState.TutorialInteractions, (int)TutorialStep.GrabRotateRelease);
                            int Notices() => Count(Read(f.Writer.EventsPath), "TUTORIAL_NOT_COUNTED_FEEDBACK");
                            var success = f.View.transform.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "TutorialSuccess");
                            string Shown() => success.Find("Label").GetComponent<TMP_Text>().text;
                            // Stopped Editor has no tracked headset: the real controller path rejects as safety_or_tracking_invalid, which stays silent.
                            c.TutorialActionCommitted("grab", 0, null, "Controller");
                            check(Read(f.Writer.EventsPath).Contains("\"safety_or_tracking_invalid\"") && Notices() == 0, name + "/tracking-invalid rejection adds no notice", null);
                            // The two reasons logged on Quest 16 Oct, rendered by the real view.
                            f.View.TutorialActionNotCounted("grab", "instruction_not_armed_at_attempt");
                            check(Notices() == 1 && success.gameObject.activeInHierarchy && !success.Find("Checkmark").gameObject.activeSelf
                                && Shown() == ResearchStrings.Get("tutorial.notice.not_yet", language.ToString()), name + "/unarmed grab shows not-counted notice without checkmark", Shown());
                            f.Capture("not_counted", f.View.mainPanel, captures, rows);
                            f.View.TutorialActionNotCounted("grab", "instruction_not_armed_at_attempt");
                            check(Notices() == 1, name + "/repeat within the notice window does not re-log", null);
                            // Quest 16 Oct 18:02: grab/release duplicates while inserting the module read as "do not grip".
                            foreach (var silent in new[] { ("release", "already_credited"), ("grab", "already_credited"), ("inspect", "instruction_not_armed_at_attempt"),
                                ("teleport", "instruction_not_armed_at_attempt"), ("snap", "already_credited"), ("switch", "already_credited"), ("grab", "safety_or_tracking_invalid") })
                                f.View.TutorialActionNotCounted(silent.Item1, silent.Item2);
                            check(Notices() == 1, name + "/duplicates, other groups' skills, locomotion and tracking rejections stay silent", null);
                            check(Count(Read(f.Writer.EventsPath), "TUTORIAL_SKILL_CREDIT") == 0 && c.Flow.TutorialCompleted == (int)TutorialStep.GrabRotateRelease, name + "/notices grant no credit", State(c));
                            credits = c.Flow.TutorialCompleted;
                        }
                        else
                        {
                            if (stage == "stop_tester_resume") Set(c, "TechnicalSelfTest", true);
                            f.Click(f.Action("task_start")); f.Click(f.Action("task_start_confirm"));
                            check(c.Flow.State == ResearchFlowState.Task1Active && !Timing(c).Ended && string.IsNullOrEmpty(c.Error), name + "/actual task active precondition", State(c));
                            if (stage == "stop_reader") { Set(c, "ReferenceOpen", true); Set(c, "ReferenceMinimized", false); }
                            if (stage == "stop_error") Set(c, "Error", "fixture existing error");
                            if (stage == "stop_writer_fault")
                            {
                                Call(c, "LoggerFailed", "fixture writer fault callback");
                                check(c.TaskEndReason == "technical_failure", name + "/original logger fault termination retained", State(c));
                            }
                            f.Refresh(); Call(f.View, "RefreshRecoveryUtility");
                            var stop = f.Action("stop", stage == "stop_reader" ? f.View.referencePanel.transform : f.View.mainPanel.transform);
                            check(stop.GetComponent<Button>().IsActive() && stop.GetComponent<Button>().IsInteractable(), name + "/stop button operable", stop.name);
                            f.Capture("before", stage == "stop_reader" ? f.View.referencePanel : f.View.mainPanel, captures, rows);
                            f.Click(stop); f.Refresh();
                            check(c.Flow.Paused && c.PauseKind == "stop" && c.Record.session_state == RegistryState.InProgress && !c.ParticipantMayResume && c.taskPorts.All(p => !p.InteractionAllowed),
                                name + "/stop suspends without terminal registry or self resume", State(c));
                            if (stage == "stop_error") check(c.Error.Contains("fixture existing error"), name + "/original error retained", c.Error);
                            if (stage == "stop_writer_fault") check(c.TaskEndReason == "technical_failure", name + "/stop never undoes fault task end", State(c));
                            else check(!Timing(c).Ended, name + "/nonfailure stop does not end task", State(c));
                            check(!c.Command("session_resume", "None"), name + "/participant cannot self resume", State(c));
                            f.Capture("after", f.View.mainPanel, captures, rows);
                            bool testerRoute = f.View.GetComponentsInChildren<ResearchUIAction>(false).Any(a => a.command == "tester_resume" && a.GetComponent<Button>().IsInteractable());
                            if (stage != "stop_tester_resume")
                                check(!testerRoute && !c.TesterMayResumeStop && !c.Command("tester_resume", "None") && c.Flow.Paused, name + "/no tester resume outside a headset self-test", State(c));
                            else
                            {
                                var resume = f.Action("tester_resume"); f.Click(resume, 2);
                                check(c.Flow.Paused && c.PauseKind == "stop", name + "/double press does not resume", State(c));
                                f.Click(resume);
                                string raw = Read(f.Writer.EventsPath);
                                check(!c.Flow.Paused && c.Flow.State == ResearchFlowState.Task1Active && !Timing(c).Ended && raw.Contains("\"TesterResumedAfterStop\"") && raw.Contains("\"source\":\"Tester\""),
                                    name + "/tester resumes the same task with a Tester-attributed event", State(c));
                                f.Capture("tester_resumed", f.View.mainPanel, captures, rows);
                            }
                        }
                        check(f.TextOverflows.Count == 0, name + "/visible text overflow", f.TextOverflows);
                        check(identity == Identity(c) && c.Flow.TutorialCompleted == credits, name + "/IDs and training credits unchanged", new { before = identity, after = Identity(c), c.Flow.TutorialCompleted });
                        check(c.Registry.List(ParticipantGroup.Test).Count == 1 && c.Record.session_state == RegistryState.InProgress, name + "/owned single test record remains open", State(c));
                        rows.Add(new { name = name + "/writer", f.Writer.EventsPath, f.Writer.State, bytes = new FileInfo(f.Writer.EventsPath).Length });
                    }
                }
                catch (Exception ex) { check(false, language + "/" + stage + "/execution", (ex.InnerException ?? ex).ToString()); }
            }
            Application.logMessageReceived -= onLog;
            check(callbackErrors.Count == 0, "no swallowed Unity callback errors", callbackErrors);
            check(EventSystem.current == previousEventSystem, "caller EventSystem unchanged", null);
            check(active == SceneManager.GetActiveScene() && scenes.SequenceEqual(SceneStates()), "caller scenes preserved", SceneStates());
            check(caller == Caller(donor.controller) && callerFiles.SequenceEqual(CallerFiles(donor.controller)), "caller controller registry and logs preserved", null);
            string report = Path.Combine(directory, "participant-safety.json");
            File.WriteAllText(report, JsonConvert.SerializeObject(new { status = failures.Count == 0 ? "PASS" : "FAIL", interaction_status = failures.All(f => f.EndsWith("/visible text overflow")) ? "PASS" : "FAIL", failures, rows, captures,
                scope = "Stopped Editor actual Unity UI components with explicit owned OnEnable/OnDisable lifecycle (runtime scripts do not auto-initialize outside Play), EventSystem callback dispatch, real controller/core/owned registry/logger; bound technical-demo task adapters (no study content approval), seeded technical tracking and initial flow, no native SDK acquisition. Actual Unity mono preview pixels/TMP data. Reader opening is seeded only to test its Stop surface. Writer fault calls real LoggerFailed callback, not a disk fault. Training and inspect_continue begin from explicit owned snapshots (negative inspection Visible stage is seeded; no real inspection or tutorial completion claimed). Seven progression commands test actual pointer callbacks for doubleclick, orphan up, rightclick, stale context, wrong-control release and held press across pause. Inspect/checkpoint coverage is accidental rejection only. No participant data, Quest, physical interaction, wearer or language approval." }, Formatting.Indented));
            if (failures.Count != 0) throw new InvalidOperationException("PARTICIPANT_SAFETY_FAIL " + report + " | " + string.Join(" | ", failures));
            return "PARTICIPANT_SAFETY_PASS " + report;
        }

        sealed class Owned : IDisposable
        {
            readonly object fixture; readonly GameObject events;
            public ResearchSessionFlowController Controller; public ResearchFlowView View; public HandsLabLogger Writer;
            public readonly List<string> TextOverflows = new List<string>();
            readonly EventSystem eventSystem; PointerEventData activePointer;
            readonly HashSet<ResearchUIAction> enabledActions = new HashSet<ResearchUIAction>();
            public Owned(string path, ResearchFlowView donor, ParticipantLanguage language)
            {
                fixture = Activator.CreateInstance(FixtureType, Instance, null, new object[] { path, donor, language }, null);
                Controller = Field<ResearchSessionFlowController>(fixture, "Controller"); View = Field<ResearchFlowView>(fixture, "View");
                try
                {
                    var created = Controller.Registry.CreateNext(ParticipantGroup.Test);
                    var record = Controller.Registry.StartOrResume(created.participant_id, language, ExperimentInputMode.Controller, TaskOrder.AB);
                    Set(Controller, "Record", record);
                    Field<HandsLabLogger>(fixture, "Writer").Dispose();
                    Writer = new HandsLabLogger(Path.Combine(path, "owned-writer"), "None", "Stopped Editor synthetic safety UI", executionEnvironment:"Editor", technicalTest:true, testData:true, runId:record.run_id);
                    Set(Controller, "Logger", Writer);
                    foreach (var port in Controller.taskPorts) { port.demoMode = true; port.Bind(() => new { fixture = true, task = port.taskId }, () => false, allowed => { }); }
                    Controller.guards = new[] { Controller.gameObject.AddComponent<HandsLabInputGuard>(), Controller.gameObject.AddComponent<HandsLabInputGuard>() };
                    foreach (var guard in Controller.guards) Set(guard, "TrackingValid", true);
                    events = new GameObject("ParticipantSafetyEventSystem", typeof(EventSystem)) { hideFlags = HideFlags.HideAndDontSave };
                    SceneManager.MoveGameObjectToScene(events, View.gameObject.scene); eventSystem = events.GetComponent<EventSystem>();
                    Seed(ResearchFlowState.Task1Ready);
                }
                catch { Dispose(); throw; }
            }
            public void Seed(ResearchFlowState state, int credits=5, bool? inspected=null) { Set(Controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, state, credits, inspectPassed:inspected)); Set(Controller, "Record", Controller.Registry.SaveProgress(Controller.Record, Controller.Flow)); Refresh(); }
            public void Refresh() { Call(fixture, "Refresh"); SyncActionLifecycle(); }
            void SyncActionLifecycle()
            {
                foreach(var a in enabledActions.Where(a => !a || !a.isActiveAndEnabled).ToArray())
                { if(a) Call(a,"OnDisable"); enabledActions.Remove(a); }
                foreach(var a in View.GetComponentsInChildren<ResearchUIAction>(true).Where(a => a.isActiveAndEnabled))
                    if(enabledActions.Add(a)) Call(a,"OnEnable");
            }
            public ResearchUIAction Action(string command, Transform within = null)
            {
                var actions = (within ? within : View.transform).GetComponentsInChildren<ResearchUIAction>(true).Where(a => a.command == command && a.gameObject.activeInHierarchy && a.GetComponent<Button>().IsInteractable()).ToArray();
                if (actions.Length != 1) throw new InvalidOperationException("Expected exactly one active " + command + "; found " + actions.Length);
                return actions[0];
            }
            PointerEventData Pointer(int clicks=1) => new PointerEventData(eventSystem) { pointerId = 73051, button = PointerEventData.InputButton.Left, clickCount = clicks };
            public void Down(ResearchUIAction a, int clicks=1, PointerEventData.InputButton button=PointerEventData.InputButton.Left) { activePointer = Pointer(clicks); activePointer.button = button; ExecuteEvents.Execute(a.gameObject, activePointer, ExecuteEvents.pointerDownHandler); }
            public void UpClick(ResearchUIAction a, int clicks=1) { if (activePointer == null) throw new InvalidOperationException("PointerDown required"); ExecuteEvents.Execute(a.gameObject, activePointer, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(a.gameObject, activePointer, ExecuteEvents.pointerClickHandler); activePointer = null; Refresh(); }
            public void OrphanClick(ResearchUIAction a) { activePointer = Pointer(); UpClick(a); }
            public void CancelPress(ResearchUIAction a) { Call(a,"OnDisable"); Call(a,"OnEnable"); activePointer = null; }
            public void Click(ResearchUIAction a,int clicks=1) { Down(a,clicks); UpClick(a,clicks); }
            public void Capture(string stage, ResearchPanelMotion panel, List<object> captures, List<object> rows)
            {
                Refresh(); captures.Add(Call(fixture,"Capture",stage,panel));
                foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.gameObject.activeInHierarchy))
                { text.ForceMeshUpdate(true,true); if(text.isTextOverflowing) TextOverflows.Add(stage+"/"+text.name+" height="+text.rectTransform.rect.height+" preferred="+text.preferredHeight); rows.Add(new { name=stage+"/TMP/"+text.name,text=text.text,text.isTextOverflowing,text.enableAutoSizing,text.fontSize,text.preferredHeight,height=text.rectTransform.rect.height }); }
            }
            public void Dispose() { foreach(var a in enabledActions) if(a) Call(a,"OnDisable"); enabledActions.Clear(); if(events) UnityEngine.Object.DestroyImmediate(events); Writer?.Dispose(); (fixture as IDisposable)?.Dispose(); }
        }
        static void PrepareGuard(Owned f, string command)
        {
            var c=f.Controller;
            if(command=="tutorial_start") f.Seed(ResearchFlowState.TutorialWelcome,0);
            else if(command=="tutorial_continue") f.Seed(ResearchFlowState.TutorialComplete);
            else if(command=="technical_resume") { f.Seed(ResearchFlowState.PostTask1Checkpoint); Set(c,"TechnicalSelfTest",true); }
            else if(command=="inspect" || command=="inspect_continue")
            {
                f.Seed(ResearchFlowState.Task1Active,5,command=="inspect_continue" ? (bool?)false : null);
                Timing(c).Start(f.Writer.Timestamp);
                if(command=="inspect_continue")
                {
                    Set(Timing(c),"Stage",ResearchInspectionStage.Visible); Set(Timing(c),"InspectionPassed",(bool?)false);
                }
            }
            else { f.Seed(ResearchFlowState.Task1Ready); Set(c,"TaskStartConfirmation",command=="task_start_confirm"); }
            f.Refresh();
        }
        static void CheckGuard(Owned f,string command,string name,Action<bool,string,object> check)
        {
            var c=f.Controller; string before=JsonConvert.SerializeObject(State(c));
            Action<string> unchanged = reason => check(before==JsonConvert.SerializeObject(State(c)),name+"/"+reason,State(c));
            f.Click(f.Action(command),2); unchanged("doubleclick rejects progress");
            f.OrphanClick(f.Action(command)); unchanged("up without down rejects progress");
            var a=f.Action(command); f.Down(a,1,PointerEventData.InputButton.Right); f.UpClick(a); unchanged("rightclick rejects progress");
            a=f.Action(command); f.Down(a); Set(c,"Error","fixture changed context"); f.UpClick(a); Set(c,"Error",""); f.Refresh(); unchanged("stale context rejects progress");
            a=f.Action(command); f.Down(a);
            var other=f.View.GetComponentsInChildren<ResearchUIAction>(true).FirstOrDefault(x=>x!=a&&x.gameObject.activeInHierarchy&&x.GetComponent<Button>().IsInteractable());
            if(!other) throw new InvalidOperationException("Wrong-control release requires another active button");
            f.UpClick(other); unchanged("release on different control rejects progress"); f.CancelPress(a);
            a=f.Action(command); f.Down(a); c.Flow.Pause(); var paused=JsonConvert.SerializeObject(State(c)); f.UpClick(a);
            check(paused==JsonConvert.SerializeObject(State(c)),name+"/held press cannot cross paused state",State(c));
        }
        static ResearchTaskTiming Timing(ResearchSessionFlowController c) => Field<ResearchTaskTiming>(c,"taskTiming");
        static object State(ResearchSessionFlowController c) => new { state=c.Flow.State.ToString(),c.Flow.Paused,c.PauseKind,c.Error,c.TaskStartConfirmation,c.Flow.InspectPassed,c.TaskEndReason,taskEnded=Timing(c).Ended,c.Record.session_state,c.Flow.TutorialCompleted };
        static string Identity(ResearchSessionFlowController c) => c.Record.participant_id+"/"+c.Record.session_id+"/"+c.Record.run_id;
        static object Call(object target,string name,params object[] args) => target.GetType().GetMethod(name,Instance).Invoke(target,args);
        static T Field<T>(object target,string name) => (T)target.GetType().GetField(name,Instance).GetValue(target);
        static void Set(object target,string name,object value) => target.GetType().GetProperty(name,Instance).GetSetMethod(true).Invoke(target,new[]{value});
        static int Count(string text,string token) => text.Split(new[]{"\""+token+"\""},StringSplitOptions.None).Length-1;
        static string Read(string path) { using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) using(var r=new StreamReader(s)) return r.ReadToEnd(); }
        static string[] SceneStates() => Enumerable.Range(0,SceneManager.sceneCount).Select(i=> { var s=SceneManager.GetSceneAt(i);return s.handle+":"+s.path+":"+s.isDirty; }).ToArray();
        static string Caller(ResearchSessionFlowController c) => c ? JsonConvert.SerializeObject(new { c.Record,c.Flow,c.Error,c.ReferenceOpen,c.ReferenceMinimized }) : "none";
        static string[] CallerFiles(ResearchSessionFlowController c) => !c ? Array.Empty<string>() : new[]{c.Registry?.RegistryPath,c.Registry?.ProtocolPath,c.Logger?.EventsPath,c.Logger?.MovementPath,c.Logger?.MetadataPath}.Where(File.Exists).Select(p=>p+":"+Hash(p)).ToArray();
        static string Hash(string path) { using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) using(var h=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)); }
    }
}
#endif
