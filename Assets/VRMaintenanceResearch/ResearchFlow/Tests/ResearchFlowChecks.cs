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
    // Station anchors from the authored canonical-scene stations (parsed from VRResearch_ResearchFlow_V2.unity: Computer at
    // (0,0,5.35), Fan at (-9.7,0,4.85), Training at (9.72,0,4.95), all yaw -180). Pure geometry; not a Unity clearance/visibility test.
    static void StationAnchorChecks()
    {
        const double Tol = 1e-6;
        bool Near(double a, double b) => Math.Abs(a - b) < 1e-3;
        Check(ResearchStationAnchors.TryGet(1, "P", "task_information", 0, 0, 5.35, -180, 1.6, 0, out var mainP)
            && ResearchStationAnchors.TryGet(1, "Q", "task_information", 0, 0, 5.35, -180, 1.6, 0, out var mainQ)
            && Math.Abs(mainP.X - mainQ.X) < Tol && Math.Abs(mainP.Y - mainQ.Y) < Tol && Math.Abs(mainP.Z - mainQ.Z) < Tol && mainP.Id != mainQ.Id,
            "P and Q share one table footprint and anchor pose but keep distinct anchor ids");
        Check(Near(mainP.X, -0.900) && Near(mainP.Y, 1.7) && Near(mainP.Z, 5.35 - 1.559),
            "Computer task brief anchors 30 deg to the participant's right (world -X at yaw -180), 1.8 m out past the bench, 0.1 m above the eye");
        Check(ResearchStationAnchors.TryGet(1, "P", "reference", 0, 0, 5.35, -180, 1.6, 0, out var reader) && Near(reader.X, 0.513) && Near(reader.Z, 5.35 - 1.410),
            "Computer Reader anchors 20 deg to the participant's left at 1.5 m");
        bool sidesKept = true;
        for (int s = 0; s <= 2; s++)
            foreach (var panel in new[] { "task_information", "reference" })
                for (int c = 0; c < ResearchStationAnchors.CandidateCount(s, panel); c++)
                {
                    ResearchStationAnchors.TryGet(s, "P", panel, 0, 0, 0, -180, 1.6, c, out var any);
                    sidesKept &= panel == "reference" || (s == 1 && c >= 8) ? any.X > 0 : any.X < 0;
                }
        Check(sidesKept, "every station keeps the task brief on the right and the Reader on the left for every candidate");
        double faceX = Math.Sin(mainP.YawDegrees * Math.PI / 180), faceZ = Math.Cos(mainP.YawDegrees * Math.PI / 180);
        Check(faceX * (mainP.X - 0) + faceZ * (mainP.Z - 5.35) > 0, "anchored slate faces away from the station point (LookRotation convention)");
        Check(!ResearchStationAnchors.TryGet(1, null, "task_information", 0, 0, 5.35, -180, 1.6, 0, out _)
            && !ResearchStationAnchors.TryGet(1, "R", "task_information", 0, 0, 5.35, -180, 1.6, 0, out _),
            "Computer station anchor requires an assigned P/Q layout (falls back and logs otherwise)");
        Check(ResearchStationAnchors.TryGet(2, null, "task_information", -9.7, 0, 4.85, -180, 1.6, 0, out var fan) && fan.Id.Contains(":fan:-:")
            && ResearchStationAnchors.TryGet(0, null, "reference", 9.72, 0, 4.95, -180, 1.6, 0, out _), "Fan and Training anchors do not depend on Task A layout");
        Check(!ResearchStationAnchors.Supports(1, "recovery") && !ResearchStationAnchors.Supports(3, "task_information") && !ResearchStationAnchors.Supports(-1, "reference"),
            "recovery strip and unknown stations are never anchored");
        Check(ResearchStationAnchors.ClampEyeHeight(double.NaN) == 1.6 && ResearchStationAnchors.ClampEyeHeight(0.4) == 1.0
            && ResearchStationAnchors.ClampEyeHeight(2.4) == 1.95 && ResearchStationAnchors.ClampEyeHeight(1.22) == 1.22, "eye height is clamped to a plausible seated/standing range");
        foreach (double height in new[] { 1.0, 1.3, 1.599, 1.6, 1.601, 1.65, 1.95 })
        {
            Check(ResearchStationAnchors.TryGet(2, null, "reference", -9.7, 0, 4.85, -180, height, 0, out var fanReader)
                && Near(fanReader.X, -8.610205) && Near(fanReader.Z, 3.293611)
                && Near(fanReader.Y, Math.Max(height, 1.6)), "Fan Reader authored pose and elevation floor at calibrated eye=" + height);
            ResearchStationAnchors.TryGet(2, null, "task_information", -9.7, 0, 4.85, -180, height, 0, out var fanTask);
            Check(Near(fanTask.Y, height), "Fan Reader elevation floor does not alter Task calibration=" + height);
        }
        Check(ResearchStationAnchors.TryGet(2, null, "reference", -9.7, .4, 4.85, -180, 1.0, 0, out var raisedFanReader)
            && Near(raisedFanReader.Y, 2.0), "Fan Reader elevation floor is relative to station, not absolute world Y");
        bool fallbackExists = ResearchStationAnchors.TryGet(2, null, "task_information", -9.7, 0, 4.85, -180, 1.0, 4, out var fanFallback);
        Check(fallbackExists && Near(fanFallback.X, -10.4) && Near(fanFallback.Y, 1.6) && Near(fanFallback.Z, 3.637564),
            "Fan Task seated fallback candidate 4 expected (-10.4,1.6,3.637564); exists=" + fallbackExists + "; actual=(" + fanFallback.X + "," + fanFallback.Y + "," + fanFallback.Z + ")");
        Check(ResearchStationAnchors.CandidateCount(2, "task_information") == 8 && ResearchStationAnchors.CandidateCount(2, "reference") == 4
            && !ResearchStationAnchors.TryGet(2, null, "task_information", 0, 0, 0, 0, 1.0, 8, out _),
            "Fan Task appends one bounded fallback base; Reader count and candidate rejection remain unchanged");
        foreach (double height in new[] { 1.0, 1.3, 1.599, 1.6, 1.601, 1.65, 1.95 })
            for (int candidate = 0; candidate < 4; candidate++)
            {
                double expectedX = new[] { -10.4, -10.4, -10.505, -10.505 }[candidate];
                double expectedZ = new[] { 3.637564, 3.637564, 3.455699, 3.455699 }[candidate];
                double expectedUp = new[] { 0, .3, 0, .3 }[candidate];
                Check(ResearchStationAnchors.TryGet(2, null, "task_information", -9.7, .4, 4.85, -180, height, candidate, out var priorFanTask)
                    && Near(priorFanTask.X, expectedX) && Near(priorFanTask.Z, expectedZ) && Near(priorFanTask.Y, .4 + height + expectedUp),
                    "original Fan Task pose preserves calibration and station offset: eye=" + height + "/candidate=" + candidate);
            }
        for (int candidate = 4; candidate < 8; candidate++)
        {
            double expectedX = new[] { -10.4, -10.4, -10.505, -10.505 }[candidate - 4];
            double expectedZ = new[] { 3.637564, 3.637564, 3.455699, 3.455699 }[candidate - 4];
            double expectedY = new[] { 1.6, 1.9, 1.6, 1.9 }[candidate - 4];
            Check(ResearchStationAnchors.TryGet(2, null, "task_information", -9.7, 0, 4.85, -180, 1.0, candidate, out var extraFanTask)
                && Near(extraFanTask.X, expectedX) && Near(extraFanTask.Y, expectedY) && Near(extraFanTask.Z, expectedZ),
                "Fan Task fallback retains native-swept low-eye pose candidate=" + candidate);
        }
        Check(ResearchStationAnchors.TryGet(1, "Q", "task_information", 0, 0, 5.35, -180, 1.0, 8, out var seatedComputer)
            && Near(seatedComputer.X, Math.Sin(20 * Math.PI / 180) * 2.2) && Near(seatedComputer.Y, 1.8)
            && Near(seatedComputer.Z, 5.35 - Math.Cos(20 * Math.PI / 180) * 2.2),
            "seated Computer fallback uses the measured clear side without moving the calibrated head");
        foreach (double eye in new[] { 1.0, 1.65 })
        {
            ResearchStationAnchors.TryGet(1, "P", "reference", 0, 0, 5.6, 180, eye, 4, out var readerP);
            ResearchStationAnchors.TryGet(1, "Q", "reference", 0, 0, 5.6, 180, eye, 4, out var readerQ);
            Check(Near(readerP.X, .6) && Near(readerP.Y, eye + .6) && Near(readerP.Z, 4.5607695)
                && Near(readerP.X, readerQ.X) && Near(readerP.Y, readerQ.Y) && Near(readerP.Z, readerQ.Z),
                "Computer Reader clearance candidate preserves calibrated P/Q equivalence eye=" + eye);
        }
        Check(ResearchStationAnchors.CandidateCount(1, "reference") == 12, "Computer Reader has bounded authored clearance candidates");
        bool alternativesSafe = true;
        int count = ResearchStationAnchors.CandidateCount(1, "task_information");
        for (int c = 0; c < count; c++)
        {
            ResearchStationAnchors.TryGet(1, "P", "task_information", 0, 0, 5.35, -180, 1.6, c, out var alt);
            double distance = Math.Sqrt(alt.X * alt.X + (alt.Z - 5.35) * (alt.Z - 5.35));
            alternativesSafe &= (c < 8 ? alt.X < 0 : alt.X > 0) && distance >= 1.8 - Tol && alt.Y >= mainP.Y - Tol;
        }
        Check(count == 12 && alternativesSafe && !ResearchStationAnchors.TryGet(1, "P", "task_information", 0, 0, 5.35, -180, 1.6, count, out _)
            && ResearchStationAnchors.CandidateCount(1, "recovery") == 0,
            "clearance alternatives only raise, step back or widen on the same side, never approach the head");
    }

    // Illustrated sub-pages over the committed S1 packages: exact text identity and TH/JA parity (presentation only).
    static void CaptionPartChecks([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        string root = Path.Combine(Path.GetDirectoryName(here), "..", "Resources", "ResearchReaderS1");
        int images = 0, split = 0, longest = 0;
        bool identity = true, parity = true;
        foreach (string task in new[] { "A", "B", "T" })
        {
            var th = JObject.Parse(File.ReadAllText(Path.Combine(root, task + "_th", "package.json")));
            var ja = JObject.Parse(File.ReadAllText(Path.Combine(root, task + "_ja", "package.json")));
            var thImages = (JArray)th["images"]; var jaImages = (JArray)ja["images"];
            parity &= thImages.Count == jaImages.Count;
            for (int i = 0; i < thImages.Count; i++)
            {
                string a = (string)thImages[i]["caption_th"], b = (string)jaImages[i]["caption_ja"];
                var sa = ResearchCaptionParts.Split(a); var sb = ResearchCaptionParts.Split(b);
                identity &= ResearchCaptionParts.Join(sa.heading, sa.parts) == a && ResearchCaptionParts.Join(sb.heading, sb.parts) == b;
                parity &= sa.parts.Length == sb.parts.Length && (sa.heading == null) == (sb.heading == null);
                images++; if (sa.parts.Length > 1) split++;
                if (sa.parts.Length > 1) foreach (var part in sa.parts) longest = Math.Max(longest, part.Length);
            }
        }
        Check(identity, "Illustrated sub-pages reproduce every reviewed caption byte-for-byte (no rewording, reordering or loss)");
        Check(parity, "TH and JA get the same number of sub-pages and headings for every image");
        Check(images == 21 && split == 16, "the 16 long A/B captions are split; the 5 short Training captions stay whole");
        Check(longest <= 240, "longest Thai sub-page paragraph is at most 240 characters (was up to 738 as one caption; whole Training captions are at most 258)");
        Check(ResearchCaptionParts.Split("one\n\ntwo\n\nthree").parts.Length == 1 && ResearchCaptionParts.Split("h\n\na\n\n\n\nb\n\nc").parts.Length == 1,
            "short or irregular captions are never split");
    }

    // Mirror of ParticipantRecord/Document as compiled at a6a6cf8 (Q4/Q5 era): strict reader, unknown members are errors.
    sealed class LegacyRecord
    {
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)] public string participant_id, created_at, session_id = "", run_id = "";
        public string started_at = "", ended_at = "";
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)] public string group, language, session_state, task_order, input_mode, flow_state;
        public string training_status, completion_state; public bool test_data, task_order_overridden, language_overridden;
        public string task_a_layout; public bool task_a_layout_overridden;
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)] public int tutorial_completed;
        public bool paused; public bool? inspect_passed;
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)] public long revision;
        public string termination_reason; public System.Collections.Generic.List<JObject> termination_amendments;
    }
    sealed class LegacyDocument { public int schema; public System.Collections.Generic.List<LegacyRecord> participants; }

    static void RegistryCompatibilityChecks(ResearchParticipantRegistry r, string root, ParticipantRecord th)
    {
        string registryText = File.ReadAllText(r.RegistryPath);
        Check(!registryText.Contains("ui_policy") && !registryText.Contains("protocol_version") && !registryText.Contains("training_version")
            && !registryText.Contains("layout_version"), "registry.json keeps the pre-policy schema (versions live in the sidecar)");
        var strict = new Newtonsoft.Json.JsonSerializerSettings { MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error };
        bool legacyReads = true;
        try { legacyReads = Newtonsoft.Json.JsonConvert.DeserializeObject<LegacyDocument>(registryText, strict).participants.Count > 0; }
        catch (Newtonsoft.Json.JsonException) { legacyReads = false; }
        Check(legacyReads, "a Q4/Q5-era strict reader still opens the registry after new-build sessions (rollback stays possible)");
        var sidecar = JObject.Parse(File.ReadAllText(r.ProtocolPath));
        Check((string)sidecar["sessions"][th.session_id]["ui_policy"] == ResearchUIPolicy.FixedDemo
            && new ResearchParticipantRegistry(root).List(ParticipantGroup.Thai).Single(p => p.participant_id == th.participant_id).ui_policy == ResearchUIPolicy.FixedDemo,
            "sidecar keyed by session_id restores the recorded policy on read");
        Check(!((JObject)sidecar["sessions"]).Properties().Any(p => r.List(ParticipantGroup.Thai).Concat(r.List(ParticipantGroup.Japanese)).Any(x => x.session_id == p.Name && x.session_state == RegistryState.Available)),
            "available IDs carry no policy");
        string sidecarText = File.ReadAllText(r.ProtocolPath);
        var tampered = (JObject)sidecar.DeepClone(); tampered["sessions"][th.session_id]["ui_policy"] = "S1_FREE_DRAG";
        File.WriteAllText(r.ProtocolPath, tampered.ToString());
        Reject(() => new ResearchParticipantRegistry(root).List(ParticipantGroup.Thai), "unknown recorded UI policy in the sidecar fails closed");
        File.WriteAllText(r.ProtocolPath, sidecarText);
        var orphan = (JObject)sidecar.DeepClone(); orphan["sessions"][Guid.NewGuid().ToString("N")] = sidecar["sessions"][th.session_id].DeepClone();
        File.WriteAllText(r.ProtocolPath, orphan.ToString());
        Check(new ResearchParticipantRegistry(root).List(ParticipantGroup.Thai).Count > 0, "crash residue (sidecar entry for an uncommitted session) is ignored");
        File.WriteAllText(r.ProtocolPath, sidecarText);
        // Intermediate builds (d2256f6..be6612a) wrote the fields into registry.json: still readable, moved to the sidecar on the next save.
        var intermediate = JObject.Parse(registryText);
        var row = (JObject)intermediate["participants"].First(p => (string)p["session_id"] == th.session_id);
        row["ui_policy"] = ResearchUIPolicy.FixedDemo; row["protocol_version"] = ResearchUIPolicy.ProtocolFixedDemo;
        row["training_version"] = "S1_TRAINING_FIXED_UI_20261014"; row["layout_version"] = ResearchUIPolicy.LayoutVersion;
        File.WriteAllText(r.RegistryPath, intermediate.ToString()); File.Delete(r.ProtocolPath);
        var reread = new ResearchParticipantRegistry(root);
        Check(reread.List(ParticipantGroup.Thai).Single(p => p.session_id == th.session_id).training_version == "S1_TRAINING_FIXED_UI_20261014",
            "registry written by an intermediate build keeps its recorded versions (not overwritten by current constants)");
        reread.CreateNext(ParticipantGroup.Pilot);
        Check(!File.ReadAllText(r.RegistryPath).Contains("ui_policy") && (string)JObject.Parse(File.ReadAllText(r.ProtocolPath))["sessions"][th.session_id]["training_version"] == "S1_TRAINING_FIXED_UI_20261014",
            "next save moves intermediate-build fields to the sidecar unchanged");
        File.WriteAllText(r.RegistryPath, registryText); File.WriteAllText(r.ProtocolPath, sidecarText);
        // Sidecar lost: the record reads as unrecorded (runtime recovers from the session event log; export reports unknown).
        File.Move(r.ProtocolPath, r.ProtocolPath + ".moved");
        Check(new ResearchParticipantRegistry(root).List(ParticipantGroup.Thai).Single(p => p.session_id == th.session_id).ui_policy == null,
            "missing sidecar never invents a policy");
        File.Move(r.ProtocolPath + ".moved", r.ProtocolPath);
    }

    // C02 loading lifecycle rules (pure). Device-side root cause of the Android stall is not covered.
    static void VideoLoadRuleChecks()
    {
        Check(!ResearchVideoLoadRules.AcceptAsync(3, 4, true), "stale callback from a superseded load (format switch/retry/hide advanced generation) is ignored");
        Check(!ResearchVideoLoadRules.AcceptAsync(4, 4, false) && ResearchVideoLoadRules.AcceptAsync(4, 4, true), "closed or hidden Reader ignores even its own callback");
        Check(!ResearchVideoLoadRules.StallDue(true, true, true, false, 9.9f) && ResearchVideoLoadRules.StallDue(true, true, true, false, 10.1f),
            "stall notice appears only after 10 s of visible loading");
        Check(!ResearchVideoLoadRules.StallDue(true, true, true, true, 60f), "stall notice fires once per attempt");
        Check(!ResearchVideoLoadRules.StallDue(false, true, true, false, 60f) && !ResearchVideoLoadRules.StallDue(true, false, true, false, 60f)
            && !ResearchVideoLoadRules.StallDue(true, true, false, false, 60f), "no stall notice for other formats, hidden Reader or a ready video");
        Check(ResearchVideoLoadRules.RetryAllowed(true, false, false) && ResearchVideoLoadRules.RetryAllowed(false, true, true)
            && !ResearchVideoLoadRules.RetryAllowed(false, true, false) && !ResearchVideoLoadRules.RetryAllowed(false, false, true),
            "Retry only after an error or a shown stall; an immediate second press on the fresh attempt is refused");
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
            StationAnchorChecks();
            CaptionPartChecks();
            VideoLoadRuleChecks();
            RegistryCompatibilityChecks(r, root, th);
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
