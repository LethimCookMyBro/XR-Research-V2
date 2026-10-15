using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Explicit isolated test route. Native readiness and all ordinary study commands keep their guards.
    public sealed class ResearchTechnicalScenario : MonoBehaviour
    {
        ResearchSessionFlowController owner;
        VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabObject heldFixture;
        bool fixtureKinematic;
        public string Status { get; private set; } = "idle";
        public string EvidencePath { get; private set; }
        public string Error { get; private set; }
        public bool Running { get; private set; }
        public bool SimulatedPresence { get; private set; }
        public string Step { get; private set; }
        string renderMode = "msaa";
        object renderResult;
        public bool Owns(ResearchSessionFlowController controller) => Running && owner == controller &&
            controller.Record?.group == ParticipantGroup.Test && controller.Record.test_data;
        public object State => new { status = Status, Running, Step, Error, EvidencePath, SimulatedPresence,
            render_mode = renderMode, render_comparison = renderResult,
            run_id = owner?.Record?.run_id, session_id = owner?.Record?.session_id,
            participant = owner?.Record?.participant_id, export = owner?.LastExportPath,
            test_data = true, synthetic_commands = true, native_tracking_not_injected = true, hardware_verified = false };

        public bool Begin(ResearchSessionFlowController controller, bool simulatedPresence, bool orderAB, out string error, string requestedRenderMode = "msaa")
        {
            error = null;
            if (requestedRenderMode != "msaa" && requestedRenderMode != "resolution")
            { error = "render_mode must be msaa or resolution."; return false; }
            if (!ResearchSessionFlowController.TechnicalBuild || Running || controller.Record != null || controller.Flow != null)
            { error = "Isolated scenario requires an idle .technical package with no existing session."; return false; }
            renderMode = requestedRenderMode; renderResult = null;
            owner = controller; SimulatedPresence = simulatedPresence;
            EvidencePath = Path.Combine(Application.persistentDataPath, "TechnicalScenarios", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(EvidencePath);
            // A fresh registry is isolated from DEV018 and every study identity; native input is never spoofed.
            owner.ConfigureTechnicalRegistry(Path.Combine(EvidencePath, "registry"));
            var record = owner.Registry.CreateNext(ParticipantGroup.Test);
            owner.StartSession(record.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller, orderAB ? TaskOrder.AB : TaskOrder.BA);
            if (owner.Record == null || !string.IsNullOrEmpty(owner.Error))
            {
                error = string.IsNullOrEmpty(owner.Error) ? "Technical session did not start." : owner.Error;
                if (owner.Record?.session_state == RegistryState.InProgress) owner.ResearcherAbort(terminationReason: "technical_failure");
                Status = "failed"; Error = error; return false;
            }
            owner.Log("TECHNICAL_SCENARIO_STARTED", "technical.scenario", "SyntheticCommands",
                after: new { simulated_presence = SimulatedPresence, synthetic_commands = true, native_tracking_not_injected = true }, source: "Tester", inputSource: "SimulatedController");
            Running = true; Status = "running";
            StartCoroutine(Drive());
            return true;
        }
        IEnumerator Drive()
        {
            var routines = new System.Collections.Generic.Stack<IEnumerator>();
            routines.Push(Scenario());
            while (routines.Count != 0)
            {
                object yielded;
                try
                {
                    var routine = routines.Peek();
                    if (!routine.MoveNext()) { routines.Pop(); continue; }
                    yielded = routine.Current;
                    if (yielded is IEnumerator nested) { routines.Push(nested); continue; }
                }
                catch (Exception ex) { Error = ex.Message; Status = "failed"; break; }
                yield return yielded;
            }
            if (Status != "failed") Status = "completed";
            FinishOwned();
        }
        IEnumerator Scenario()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Capture("welcome");
            Require(owner.Command("tutorial_start", "SimulatedController"), "tutorial_start");
            // Follow the controller's own fact order, so the scenario stays valid for both training versions and the trainer station.
            while (owner.Flow.State != ResearchFlowState.TutorialComplete)
            {
                string fact = owner.TutorialFact;
                Require(fact != null, "Tutorial has no current fact before completion.");
                Step = "tutorial." + fact;
                yield return new WaitForSecondsRealtime(.4f);
                double promptDeadline = Time.realtimeSinceStartupAsDouble + 5;
                while (!owner.view.TutorialActionReady(fact) && Time.realtimeSinceStartupAsDouble < promptDeadline)
                    yield return null;
                Require(owner.view.TutorialActionReady(fact), "Visible Tutorial prompt unavailable: " + fact);
                if (fact == "grab")
                {
                    heldFixture = Array.Find(owner.trainingObjects, item => item);
                    Require(heldFixture && heldFixture.body, "No authored training object for controlled held fixture.");
                    fixtureKinematic = heldFixture.body.isKinematic; heldFixture.body.isKinematic = true;
                    Require(heldFixture.SetIsolatedTechnicalHold(owner, true), "Isolated technical hold denied.");
                    owner.Log("DEMO_INPUT", heldFixture.stableId, "SyntheticHeldFixture", after: heldFixture.CaptureSnapshot(), source: "Tester", inputSource: "SimulatedController");
                }
                if (fact == "release") ReleaseFixture();
                if (fact == "reference") Require(owner.Command("reference", "SimulatedController"), "tutorial reference");
                else if (fact == "restore") Require(owner.Command("reference_restore", "SimulatedController"), "tutorial restore");
                else if (fact == "switch") Require(owner.Command("reference_tab.step_by_step", "SimulatedController"), "tutorial switch");
                else if (fact == "close") Require(owner.Command("reference_close", "SimulatedController"), "tutorial close");
                else owner.DemoTutorialEvidence(fact);
                Require(owner.TutorialFact != fact || owner.Flow.State == ResearchFlowState.TutorialComplete, "Tutorial fact not accepted: " + fact);
                if (fact == "select" || fact == "reference") yield return Capture(Step);
            }
            Require(owner.Flow.State == ResearchFlowState.TutorialComplete, "Tutorial did not finish: " + owner.TutorialFact);
            Require(owner.Command("tutorial_continue", "SimulatedController"), "tutorial_continue");
            yield return new WaitForSecondsRealtime(1.5f);
            for (int task = 0; task < 2; task++)
            {
                Step = "task." + owner.Flow.CurrentTask;
                Require(owner.Command("task_start", "SimulatedController"), "task_start");
                Require(!owner.Flow.IsTaskActive, "timer not started before confirmation");
                Require(owner.Command("task_start_confirm", "SimulatedController"), "task_start_confirm");
                yield return Capture(Step + ".active");
                Require(owner.Command("reference", "SimulatedController"), "reference");
                yield return Capture(Step + ".choice");
                Require(owner.Command("reference_tab.user_manual", "SimulatedController"), "deliberate manual choice");
                yield return Capture(Step + ".reference");
                yield return VideoProbe(Step);
                if (task == 0 && !Application.isEditor)
                {
                    var comparison = owner.GetComponent<ResearchRenderExperiment>() ?? owner.gameObject.AddComponent<ResearchRenderExperiment>();
                    string renderError;
                    if (comparison.Begin(owner, renderMode, out renderError, 1.10f, true))
                    {
                        Step = "native_desk_" + renderMode + "_task_and_reader";
                        while (comparison.Running) yield return new WaitForSecondsRealtime(.5f);
                        yield return new WaitForSecondsRealtime(.5f);
                        renderResult = comparison.Status;
                        File.WriteAllText(Path.Combine(EvidencePath, "render-comparison-result.json"), JsonConvert.SerializeObject(renderResult, Formatting.Indented));
                        Require(comparison.Completed, "Native render windows incomplete or baseline restore not verified.");
                    }
                    else
                    {
                        renderResult = new { status = "unavailable", error = renderError, hardware_verified = false };
                        File.WriteAllText(Path.Combine(EvidencePath, "render-comparison-result.json"), JsonConvert.SerializeObject(renderResult, Formatting.Indented));
                        owner.Log("TECHNICAL_RENDER_UNAVAILABLE", "render", "Unavailable", after: renderResult, source: "Tester");
                    }
                }
                owner.view?.OnSpatialDiscontinuity("isolated_technical_recovery");
                yield return Capture(Step + ".recovered");
                Require(owner.Command("reference_minimize", "SimulatedController"), "reference minimize");
                yield return new WaitForSecondsRealtime(.3f);
                Require(owner.Command("reference_restore", "SimulatedController"), "reference restore");
                yield return new WaitForSecondsRealtime(.3f);
                Require(owner.Command("pause", "SimulatedController"), "pause");
                yield return new WaitForSecondsRealtime(.5f);
                owner.ResearcherResume(); // Explicit technical operator resume, not a claim of native participant readiness.
                Require(owner.Command("inspect", "SimulatedController"), "inspect fail");
                yield return Capture(Step + ".fail");
                Require(owner.Command("inspect_continue", "SimulatedController"), "return after fail");
                owner.SetDemoPass(true);
                Require(owner.Command("inspect", "SimulatedController"), "explicit demo pass");
                yield return new WaitForSecondsRealtime(.4f);
                owner.Log("TECHNICAL_CHECKPOINT_RESUME", "questionnaire", "SyntheticAcknowledgementNoAnswers", source: "Tester", inputSource: "SimulatedController");
                owner.ResearcherResume();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            Require(owner.Flow.State == ResearchFlowState.ParticipantComplete && owner.Logger.DataComplete, "finalized actual lifecycle");
            Require(!string.IsNullOrEmpty(owner.ExportSession()), "export: " + owner.ExportDeliveryError);
            yield return Capture("complete");
        }
        // Reader video on the device decoder (HANDOFF R1-R3): load, play, then fast switches that must drop the stale load. The viewer
        // logs every REFERENCE_VIDEO_LOAD_PHASE; this records the outcome without failing the scenario, so a stall is evidence.
        IEnumerator VideoProbe(string step)
        {
            var viewer = owner.view.ReferenceViewer;
            IEnumerator Await(string label, double seconds)
            {
                double until = Time.realtimeSinceStartupAsDouble + seconds, started = Time.realtimeSinceStartupAsDouble;
                while (!(viewer.IsReady && viewer.BoundSection == "video") && Time.realtimeSinceStartupAsDouble < until) yield return null;
                owner.Log("TECHNICAL_VIDEO_PROBE", "reference.video", viewer.IsReady && viewer.BoundSection == "video" ? "Ready" : "NotReady",
                    after: new { label, status = viewer.Status, waited_s = Math.Round(Time.realtimeSinceStartupAsDouble - started, 2) }, source: "Tester", inputSource: "SimulatedController");
            }
            Require(owner.Command("reference_tab.video", "SimulatedController"), "video tab");
            yield return Await(step + ".video_first", 30);
            yield return Capture(step + ".video");
            if (viewer.IsReady && owner.Command("reference_media.play", "SimulatedController")) { yield return new WaitForSecondsRealtime(3f); yield return Capture(step + ".video_playing"); }
            foreach (var away in new[] { "reference_tab.user_manual", "reference_tab.step_by_step", "reference_tab.illustrated" })
            {
                Require(owner.Command(away, "SimulatedController"), away);
                yield return new WaitForSecondsRealtime(.3f);
                Require(owner.Command("reference_tab.video", "SimulatedController"), "video tab again");
                yield return new WaitForSecondsRealtime(.6f);
            }
            yield return Await(step + ".video_after_switches", 30);
            yield return Capture(step + ".video_after_switches");
            Require(owner.Command("reference_tab.user_manual", "SimulatedController"), "back to manual");
            yield return new WaitForSecondsRealtime(.5f);
        }
        IEnumerator Capture(string label)
        {
            yield return new WaitForSecondsRealtime(.5f);
            yield return new WaitForEndOfFrame();
            string state = owner.Flow.State.ToString(), reference = owner.ReferenceSection;
            string path = Path.Combine(EvidencePath, label + ".png");
            int requestedFrame = Time.frameCount;
            // Android appends filename to persistentDataPath; desktop accepts the full path.
            string captureFilename = Application.platform == RuntimePlatform.Android
                ? Path.Combine("TechnicalScenarios", Path.GetFileName(EvidencePath), label + ".png") : path;
            var camera = owner.view && owner.view.Head ? owner.view.Head.GetComponent<Camera>() : null;
            var captureMode = camera && camera.stereoEnabled ? ScreenCapture.StereoScreenCaptureMode.BothEyes : ScreenCapture.StereoScreenCaptureMode.LeftEye;
            ScreenCapture.CaptureScreenshot(captureFilename, captureMode);
            double deadline = Time.realtimeSinceStartupAsDouble + 8;
            long previousLength = -1; int stable = 0;
            while (stable < 2 && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                long length = File.Exists(path) ? new FileInfo(path).Length : -1;
                stable = length > 24 && length == previousLength ? stable + 1 : 0; previousLength = length;
            }
            Require(stable >= 2, "Capture did not finish: " + label);
            Require(state == owner.Flow.State.ToString() && reference == owner.ReferenceSection, "State changed during screenshot: " + label);
            string hash;
            using (var stream = File.OpenRead(path)) using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(Path.Combine(EvidencePath, "state_" + label + ".json"), JsonConvert.SerializeObject(new {
                label, capture_filename = captureFilename, requested_stereo_capture_mode = captureMode.ToString(), camera_stereo_enabled = camera && camera.stereoEnabled,
                utc = DateTime.UtcNow.ToString("O"), state, reference, requested_frame = requestedFrame, file_finished_frame = Time.frameCount,
                image_sha256 = hash, image_bytes = previousLength, command_advance_waited_for_image = true,
                visible = owner.view && owner.view.IsReferenceActuallyVisible, run_id = owner.Record.run_id,
                render = ResearchRenderExperiment.CurrentSettings(), test_data = true, simulated_presence = SimulatedPresence,
                synthetic_commands = true, hardware_verified = false }, Formatting.Indented));
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        public void Cancel()
        {
            if (!Running) return;
            StopAllCoroutines(); Running = false; Status = "cancelled";
            FinishOwned();
        }
        void FinishOwned()
        {
            Running = false;
            owner?.RenderComparison?.Cancel();
            try { ReleaseFixture(); }
            catch (Exception ex) { Error = "Fixture release failed: " + ex.Message; Status = "failed"; }
            try { if (owner?.Record?.session_state == RegistryState.InProgress) owner.ResearcherAbort(terminationReason: "technical_failure"); }
            catch (Exception ex) { Error = (Error ?? "") + " Session cleanup failed: " + ex.Message; Status = "failed"; }
            finally { WriteResult(); }
        }
        void WriteResult()
        {
            if (string.IsNullOrEmpty(EvidencePath)) return;
            try { File.WriteAllText(Path.Combine(EvidencePath, "scenario-result.json"), JsonConvert.SerializeObject(State, Formatting.Indented)); }
            catch (Exception ex) { Error = "Scenario result write failed: " + ex.Message; Status = "failed"; Debug.LogError(Error); }
        }
        void ReleaseFixture()
        {
            if (!heldFixture) return;
            heldFixture.SetIsolatedTechnicalHold(owner, false);
            if (heldFixture.body) heldFixture.body.isKinematic = fixtureKinematic;
            heldFixture = null;
        }
        void OnDisable() => Cancel();
    }
}
