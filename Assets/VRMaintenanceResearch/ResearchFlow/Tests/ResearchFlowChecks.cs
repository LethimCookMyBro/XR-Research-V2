#if RESEARCH_FLOW_SELF_TEST
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VRMaintenanceResearch.ResearchFlow;

static class ResearchFlowChecks
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } catch (IOException) { rejected = true; } catch (InvalidDataException) { rejected = true; }
        Check(rejected, name);
    }
    static ResearchSessionFlow Exercise(TaskOrder order)
    {
        var f = new ResearchSessionFlow(order);
        Check(!f.StartTask() && !f.Continue() && !f.Inspect(true), order + " setup gates task interaction");
        Check(f.StartSession() && f.State == ResearchFlowState.TutorialWelcome && f.StartTutorial(), order + " welcome and interactions");
        Check(!f.CompleteTutorialStep(TutorialStep.Inspect), order + " tutorial cannot skip steps");
        Check(f.CompleteTutorialStep(TutorialStep.Movement) && f.CompleteTutorialStep(TutorialStep.PointSelect)
            && f.CompleteTutorialStep(TutorialStep.GrabRotateRelease), order + " ordered interaction checklist completes");
        Check(f.State == ResearchFlowState.TutorialReferenceInspect && f.TutorialCompleted == 3, order + " reference practice phase");
        Check(f.CompleteTutorialStep(TutorialStep.Reference) && f.CompleteTutorialStep(TutorialStep.Inspect)
            && f.State == ResearchFlowState.TutorialComplete && f.TutorialCompleted == 5, order + " reference Inspect practice completes");
        Check(f.Continue() && f.CurrentTask == (order == TaskOrder.AB ? "A" : "B") && f.StartTask() && !f.IsTutorial, order + " dynamic task1");
        Check(f.Pause() && !f.CanInteract && !f.Inspect(true) && !f.StartTask() && f.ResearcherResume(), order + " safety pause gates interaction");
        Check(f.Inspect(false) && !f.CanInteract && !f.Inspect(true), order + " fail modal blocks duplicate inspection");
        var failed = new ResearchSessionFlow(order, f.State, f.TutorialCompleted, f.Paused, f.InspectPassed);
        Check(failed.InspectPassed == false && failed.ContinueInspect() && failed.CanInteract && failed.State == f.State,
            order + " failed modal snapshot resumes same task without prescribed next action");
        Check(f.ContinueInspect() && f.CanInteract && f.Inspect(true) && f.ContinueInspect()
            && f.State == ResearchFlowState.PostTask1Checkpoint, order + " task1 pass reaches checkpoint");
        Check(!f.Continue() && !f.StartTask() && !f.CanInteract, order + " participant cannot skip questionnaire checkpoint");
        Check(f.ResearcherResumeCheckpoint() && f.CurrentTask == (order == TaskOrder.AB ? "B" : "A") && f.StartTask(), order + " researcher starts task2");
        Check(f.Inspect(true) && f.ContinueInspect() && f.State == ResearchFlowState.PostTask2Checkpoint && !f.Continue(), order + " second questionnaire required");
        Check(f.ResearcherResumeCheckpoint() && f.State == ResearchFlowState.ParticipantComplete && !f.CanInteract, order + " participant completion");
        Check(f.OpenResearcherSummary() && f.State == ResearchFlowState.ResearcherSummary, order + " separate researcher summary");
        return f;
    }
    static int Main(string[] args)
    {
        try
        {
            string root = Path.Combine(args.Length == 0 ? Path.GetTempPath() : args[0], "registry-check-" + Guid.NewGuid().ToString("N"));
            var r = new ResearchParticipantRegistry(root);
            var th = r.CreateNext(ParticipantGroup.Thai); var jp = r.CreateNext(ParticipantGroup.Japanese);
            Check(th.participant_id == "TH01" && jp.participant_id == "JP01", "TH01 and JP01 independent namespaces");
            th = r.StartOrResume(th.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller);
            Check(r.List(ParticipantGroup.Japanese).Single().session_state == RegistryState.Available, "TH01 reservation does not lock JP01");
            string freshStudyBefore = File.ReadAllText(r.RegistryPath);
            Reject(() => r.StartOrResume(jp.participant_id, ParticipantLanguage.Japanese, ExperimentInputMode.HandTracking), "new Japanese study refuses hand input");
            Check(File.ReadAllText(r.RegistryPath) == freshStudyBefore, "rejected study input does not reserve an ID or change registry bytes");
            jp = r.StartOrResume(jp.participant_id, ParticipantLanguage.Japanese, ExperimentInputMode.Controller);
            Check(jp.task_order == TaskOrder.AB && th.task_order == TaskOrder.AB, "counterbalance independently starts AB per group");
            var resumed = new ResearchParticipantRegistry(root).StartOrResume("TH01", ParticipantLanguage.Japanese, ExperimentInputMode.HandTracking, TaskOrder.BA);
            Check(resumed.session_id == th.session_id && resumed.run_id == th.run_id && resumed.task_order == th.task_order
                && resumed.language == ParticipantLanguage.Thai && resumed.input_mode == ExperimentInputMode.Controller, "resume immutable identity order language input mode");
            Check(jp.session_id != th.session_id && jp.run_id != th.run_id && th.session_id != th.run_id, "unique session and run metadata");
            string controllerStudyBefore = File.ReadAllText(r.RegistryPath);
            var legacyHandStudy = JObject.Parse(controllerStudyBefore);
            legacyHandStudy["participants"].Single(p => (string)p["participant_id"] == jp.participant_id)["input_mode"] = "HandTracking";
            File.WriteAllText(r.RegistryPath, legacyHandStudy.ToString());
            string legacyStudyBefore = File.ReadAllText(r.RegistryPath);
            Reject(() => new ResearchParticipantRegistry(root).StartOrResume(jp.participant_id, ParticipantLanguage.Japanese, ExperimentInputMode.Controller), "incompatible saved study refuses resume instead of silently converting input");
            Check(File.ReadAllText(r.RegistryPath) == legacyStudyBefore, "rejected legacy study keeps its exact saved evidence");
            File.WriteAllText(r.RegistryPath, controllerStudyBefore);
            var th2 = r.CreateNext(ParticipantGroup.Thai);
            th2 = r.StartOrResume(th2.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller);
            Check(th2.task_order == TaskOrder.BA, "second Thai assignment counterbalanced BA");
            for (int i = 3; i <= 10; i++) r.CreateNext(ParticipantGroup.Thai);
            Check(r.List(ParticipantGroup.Thai).Last().participant_id == "TH10", "IDs exceed planned eight without limit");
            var dev = r.CreateNext(ParticipantGroup.Dev); var test = r.CreateNext(ParticipantGroup.Test); var pilot = r.CreateNext(ParticipantGroup.Pilot);
            Check(dev.participant_id == "DEV001" && test.participant_id == "TEST001" && pilot.participant_id == "PILOT001", "technical namespaces independent");
            dev = r.StartOrResume(dev.participant_id, ParticipantLanguage.Japanese, ExperimentInputMode.TechnicalBoth);
            Check(dev.test_data && r.List(ParticipantGroup.Thai).Count == 10 && r.List(ParticipantGroup.Japanese).Count == 1, "technical IDs consume no study participants");
            test = r.StartOrResume(test.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.HandTracking);
            Check(test.test_data && test.input_mode == ExperimentInputMode.HandTracking, "technical diagnostic hand input remains available");
            var th3 = r.StartOrResume("TH03", ParticipantLanguage.Thai, ExperimentInputMode.Controller, TaskOrder.BA);
            Check(th3.task_order_overridden && th3.task_order == TaskOrder.BA, "explicit order override persisted");
            var th4 = r.StartOrResume("TH04", ParticipantLanguage.Thai, ExperimentInputMode.Controller);
            Check(th4.task_order == TaskOrder.AB, "automatic assignment compensates previous override imbalance");
            Reject(() => r.StartOrResume("TH05", ParticipantLanguage.Japanese, ExperimentInputMode.Controller), "study language mismatch requires technical override");
            Reject(() => r.StartOrResume("TH05", ParticipantLanguage.Thai, ExperimentInputMode.HandTracking), "new Thai study refuses hand input");
            Reject(() => r.StartOrResume("TH05", ParticipantLanguage.Thai, ExperimentInputMode.TechnicalBoth), "TechnicalBoth forbidden for real study IDs");
            Check(r.List(ParticipantGroup.Thai).Single(p => p.participant_id == "TH05").session_state == RegistryState.Available, "invalid setup never consumes ID");
            var overrideLanguage = r.StartOrResume("TH05", ParticipantLanguage.Japanese, ExperimentInputMode.Controller, technicalOverride: true);
            Check(overrideLanguage.test_data && overrideLanguage.language_overridden, "technical language override clearly marks test data");
            // ADVISOR_UI_AMENDMENT_20261014: versioned participant UI policy recorded once at session start.
            Check(th.ui_policy == ResearchUIPolicy.FixedDemo && th.protocol_version == ResearchUIPolicy.ProtocolFixedDemo
                && th.training_version == ResearchUIPolicy.TrainingFixedDemo && th.layout_version == ResearchUIPolicy.LayoutVersion,
                "new study session records fixed UI policy, protocol, training and layout versions");
            Check(resumed.ui_policy == th.ui_policy && resumed.layout_version == th.layout_version, "resume keeps the recorded UI policy");
            var legacyDev = r.CreateNext(ParticipantGroup.Dev);
            legacyDev = r.StartOrResume(legacyDev.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller, uiPolicy: ResearchUIPolicy.LegacyMovable);
            Check(legacyDev.ui_policy == ResearchUIPolicy.LegacyMovable && legacyDev.training_version == ResearchUIPolicy.TrainingBaseline
                && legacyDev.protocol_version == ResearchUIPolicy.ProtocolBaseline, "technical regression session may record the legacy movable policy explicitly");
            var policyStudy = r.CreateNext(ParticipantGroup.Japanese);
            string policyBefore = File.ReadAllText(r.RegistryPath);
            Reject(() => r.StartOrResume(policyStudy.participant_id, ParticipantLanguage.Japanese, ExperimentInputMode.Controller, uiPolicy: ResearchUIPolicy.LegacyMovable),
                "study ID refuses a non-default UI policy");
            Reject(() => r.StartOrResume(policyStudy.participant_id, ParticipantLanguage.Japanese, ExperimentInputMode.Controller, uiPolicy: "S1_FREE_DRAG"), "unknown UI policy rejected");
            Check(File.ReadAllText(r.RegistryPath) == policyBefore, "rejected UI policy reserves nothing");
            Check(ResearchUIPolicy.ForRecord(null) == ResearchUIPolicy.LegacyMovable && ResearchUIPolicy.ForRecord(ResearchUIPolicy.FixedDemo) == ResearchUIPolicy.FixedDemo,
                "records predating the policy field are attributed to the movable-panel UI");
            Check(ResearchUIPolicy.Parse(null) == ResearchUIPolicy.FixedDemo && ResearchUIPolicy.Parse("fixed") == ResearchUIPolicy.FixedDemo
                && ResearchUIPolicy.Parse("legacy_movable") == ResearchUIPolicy.LegacyMovable, "operator policy aliases parse");
            Reject(() => ResearchUIPolicy.Parse("free"), "unknown operator policy alias rejected");
            Check(ResearchUIPolicy.Active == ResearchUIPolicy.FixedDemo && !ResearchUIPolicy.PanelDragAllowed && !ResearchUIPolicy.ParticipantRecenterAllowed
                && !ResearchUIPolicy.TrainingIncludesPanelDrag && ResearchUIPolicy.ReaderPoseFixedPerTask, "default policy disables participant drag/recenter and the drag lesson");
            var policyRegistry = JObject.Parse(File.ReadAllText(r.RegistryPath));
            Check(policyRegistry["participants"].Where(p => (string)p["session_state"] == "Available").All(p => p["ui_policy"] == null),
                "available IDs carry no policy (fields are omitted, not null)");
            var tampered = (JObject)policyRegistry.DeepClone();
            tampered["participants"].First(p => p["ui_policy"] != null)["ui_policy"] = "S1_FREE_DRAG";
            string untampered = File.ReadAllText(r.RegistryPath);
            File.WriteAllText(r.RegistryPath, tampered.ToString());
            Reject(() => new ResearchParticipantRegistry(root).List(ParticipantGroup.Thai), "unknown recorded UI policy fails closed");
            File.WriteAllText(r.RegistryPath, untampered);
            var f = new ResearchSessionFlow(th.task_order, th.flow_state);
            f.StartTutorial(); f.CompleteTutorialStep(TutorialStep.Movement); f.Pause();
            var stale = th; th = r.SaveProgress(th, f);
            Check(th.tutorial_completed == 1 && th.paused && th.flow_state == ResearchFlowState.TutorialInteractions, "tutorial and pause snapshot durably persisted");
            Reject(() => r.SaveProgress(stale, f), "stale concurrent writer cannot overwrite progress");
            var completeAB = Exercise(TaskOrder.AB); Exercise(TaskOrder.BA);
            Reject(() => new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active), "inconsistent resumed task cannot bypass unfinished tutorial");
            Reject(() => new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 5), "inconsistent tutorial snapshot fails closed");
            th = r.SaveProgress(th, completeAB);
            Check(th.session_state == RegistryState.Completed && th.training_status == "Passed" && th.ended_at.Length > 0, "completed session metadata persisted");
            Reject(() => r.StartOrResume("TH01", ParticipantLanguage.Thai, ExperimentInputMode.Controller), "completed ID cannot create new session");
            Reject(() => r.AmendTermination("TH02", "withdrawal", "Console"), "an in-progress session has no ending to amend");
            r.Finish(th2, RegistryState.Aborted, "safety_stop"); r.Finish(th3, RegistryState.Invalid);
            Reject(() => r.StartOrResume("TH02", ParticipantLanguage.Thai, ExperimentInputMode.Controller), "aborted ID locked");
            Reject(() => r.StartOrResume("TH03", ParticipantLanguage.Thai, ExperimentInputMode.Controller), "invalid ID locked");
            Reject(() => r.AmendTermination("TH02", "time_limit", "Console"), "time_limit stays system-owned");
            var amended = r.AmendTermination("TH02", "withdrawal", "Console");
            amended = r.AmendTermination("TH02", "withdrawal", "Console");
            var history = new ResearchParticipantRegistry(root).List(ParticipantGroup.Thai).Single(p => p.participant_id == "TH02");
            Check(history.termination_reason == "safety_stop" && history.termination_amendments.Count == 1 && history.termination_amendments[0].from_reason == "safety_stop"
                && history.termination_amendments[0].to_reason == "withdrawal" && history.session_state == RegistryState.Aborted, "emergency stop reason kept, later category appended once");
            Check(!File.ReadAllText(Path.Combine(root, "registry.json")).Contains("\"termination_reason\": null"), "unamended records stay readable by older builds");
            var concurrent = r.CreateNext(ParticipantGroup.Test);
            var starts = Enumerable.Range(0, 8).Select(_ => Task.Run(() => new ResearchParticipantRegistry(root).StartOrResume(concurrent.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller))).ToArray();
            Task.WaitAll(starts);
            Check(starts.Select(t => t.Result.session_id).Distinct().Count() == 1, "concurrent reservation yields one durable session");
            var creates = Enumerable.Range(0, 8).Select(_ => Task.Run(() => new ResearchParticipantRegistry(root).CreateNext(ParticipantGroup.Pilot))).ToArray();
            Task.WaitAll(creates);
            Check(creates.Select(t => t.Result.participant_id).Distinct().Count() == 8, "concurrent allocation yields unique IDs");
            string before = File.ReadAllText(r.RegistryPath);
            File.WriteAllText(r.RegistryPath + ".pending-crash-fixture", "incomplete write");
            Check(new ResearchParticipantRegistry(root).List(ParticipantGroup.Thai).Count == 10 && File.ReadAllText(r.RegistryPath) == before, "uncommitted crash residue does not alter committed registry");
            File.WriteAllText(r.RegistryPath, "{broken");
            Reject(() => r.CreateNext(ParticipantGroup.Thai), "corrupt JSON fails closed");
            Check(File.ReadAllText(r.RegistryPath) == "{broken", "corrupt evidence never overwritten");
            File.WriteAllText(r.RegistryPath, "{}");
            Reject(() => r.CreateNext(ParticipantGroup.Thai), "missing registry fields cannot silently erase reservations");
            var damaged = JObject.Parse(before); ((JObject)damaged["participants"][0]).Remove("session_state");
            File.WriteAllText(r.RegistryPath, damaged.ToString());
            Reject(() => r.CreateNext(ParticipantGroup.Thai), "missing lock state cannot silently unlock ID");
            File.WriteAllText(r.RegistryPath, before);
            File.Move(r.RegistryPath, r.RegistryPath + ".removed-test-fixture");
            Reject(() => r.CreateNext(ParticipantGroup.Thai), "missing committed registry with backup requires explicit recovery");
            File.Move(r.RegistryPath + ".removed-test-fixture", r.RegistryPath);
            Console.WriteLine("EVIDENCE_DIRECTORY=" + root);
            Console.WriteLine("RESEARCH_FLOW_CHECKS=" + checks + "/" + checks + " passed; pure state/disk checks only, not Unity pixels or hardware");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
#endif
