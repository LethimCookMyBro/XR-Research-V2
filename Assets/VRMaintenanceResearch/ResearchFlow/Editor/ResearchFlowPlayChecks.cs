#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Opt-in Editor demo evidence; never runs automatically or claims hardware input.</summary>
    public static class ResearchFlowPlayChecks
    {
        // Isolated inactive fixture: exercises actual view/gate code without starting a registry session.
        public static string CheckTutorialErrorView(string evidenceDirectory = null)
        {
            evidenceDirectory = Path.GetFullPath(evidenceDirectory ?? ("Logs/QuestFinalIntegration/tutorial-error-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture)));
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load the ResearchFlow integration scene with its fonts first.");
            var root = new GameObject("ResearchFlow_TutorialErrorFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var checks = new List<string>();
            void Check(bool value, string description) { if (!value) throw new InvalidOperationException(description); checks.Add(description); }
            void Set(object target, string property, object value) => target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
            T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var ui = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                fixture.view = ui; ui.controller = fixture;
                Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 0));
                var fixtureHead = new GameObject("IsolatedTutorialHead"); fixtureHead.transform.SetParent(root.transform);
                fixtureHead.transform.SetPositionAndRotation(new Vector3(20, 1.65f, 20), Quaternion.Euler(0, 180, 0));
                ui.Head = fixtureHead.transform; // DEV logic fixture away from scene geometry; placement is checked separately.
                foreach (var panel in new[] { ui.mainPanel, ui.referencePanel }) typeof(ResearchPanelMotion)
                    .GetField("environment", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, Array.Empty<Renderer>());
                var modeRecord = new ParticipantRecord { input_mode = ExperimentInputMode.HandTracking };
                Set(fixture, "Record", modeRecord);
                Check(fixture.TutorialInstructionKey == "tutorial.try.teleport.hand", "Hand-only tutorial does not mention a controller stick");
                var modeGuard = root.AddComponent<VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabInputGuard>();
                fixture.guards = new[] { modeGuard };
                Set(modeGuard, "Mode", VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabInputGuard.InputMode.TrackedHand);
                modeRecord.input_mode = ExperimentInputMode.TechnicalBoth;
                Check(!fixture.TutorialUsesHands, "Untracked source does not select optical instructions");
                Set(modeGuard, "TrackingValid", true);
                Check(fixture.TutorialInstructionKey == "tutorial.try.teleport.hand", "TechnicalBoth follows the tracked optical source");
                Set(modeGuard, "Mode", VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabInputGuard.InputMode.Controller);
                Check(fixture.TutorialInstructionKey == "tutorial.try.teleport.controller", "TechnicalBoth switches back to controller instructions");
                modeRecord.input_mode = ExperimentInputMode.Controller;
                Set(modeGuard, "Mode", VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabInputGuard.InputMode.TrackedHand);
                Check(!fixture.TutorialUsesHands, "Controller-only session keeps controller instructions");
                fixture.guards = Array.Empty<VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabInputGuard>();
                Set(fixture, "Record", null);
                Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                Set(fixture, "ReferenceOpen", true);
                ui.Refresh();
                var checklist = Field<RectTransform>(ui, "checklist");
                var body = Field<TMP_Text>(ui, "body");
                var main = Field<CanvasGroup>(ui, "mainGroup");
                var integratedCue = ui.referencePanel.transform.Find("Expanded/TutorialCue").GetComponent<TMP_Text>();
                Check(!checklist.gameObject.activeSelf && !ui.mainPanel.gameObject.activeSelf
                    && !Field<TMP_Text>(ui, "tutorialProgress").gameObject.activeSelf
                    && !Field<RectTransform>(ui, "tutorialDemo").gameObject.activeSelf,
                    "Tutorial Reference uses one current instruction and its real controls, without a duplicate generic controller");
                Check(ui.referencePanel.gameObject.activeSelf && integratedCue.gameObject.activeSelf && !main.interactable && !main.blocksRaycasts,
                    "Expanded Reference owns the visible current Tutorial cue");
                Check(Plain(integratedCue.text).Contains(ResearchStrings.Get(fixture.TutorialInstructionKey, fixture.Language)),
                    "Integrated Reference cue names the current localized action");
                Set(fixture, "Error", "PRIVATE_FIXTURE_ERROR_NOT_FOR_PARTICIPANT");
                ui.Refresh();
                Check(!checklist.gameObject.activeSelf, "Error hides tutorial checklist");
                Check(body.gameObject.activeSelf && !string.IsNullOrWhiteSpace(body.text), "Error exposes safety instructions");
                Check(!body.text.Contains("PRIVATE_FIXTURE"), "Participant instructions do not leak technical error");
                Check(!ui.referencePanel.gameObject.activeSelf, "Error hides reference even before gate callback");
                Check(main.alpha == 1 && main.interactable && main.blocksRaycasts, "Error restores visible safety panel");
                typeof(ResearchSessionFlowController).GetMethod("BlockInteractions", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fixture, new object[] { "fixture_error" });
                Check(!fixture.ReferenceOpen && !fixture.ReferenceMinimized, "Error gate clears reference state");
                string failureLogs = Path.Combine(evidenceDirectory, "l");
                using (var logger = new HandsLabLogger(failureLogs, "None", "Editor confirmation fixture", technicalTest: true, testData: true))
                {
                    // Isolated controller callbacks use the same held/source lifecycle; native SDK acquisition is checked separately.
                    var practiceObject = new GameObject("HandlingFixtureObject"); practiceObject.transform.SetParent(root.transform);
                    var item = practiceObject.AddComponent<VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabObject>();
                    fixture.trainingObjects = new[] { item };
                    var heldPointers = Field<HashSet<int>>(item, "pointers");
                    var objectPointer = typeof(ResearchSessionFlowController).GetMethod("ObjectPointer", BindingFlags.Instance | BindingFlags.NonPublic);
                    void Pointer(Oculus.Interaction.PointerEventType type) => objectPointer.Invoke(fixture,
                        new object[] { item, new Oculus.Interaction.PointerEvent(731, type, new Pose(item.transform.position, item.transform.rotation)) });
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 2));
                    Set(fixture, "Error", ""); Set(fixture, "Logger", logger);
                    ui.Refresh();
                    heldPointers.Add(731);
                    fixture.Evidence("grab");
                    var success = Field<RectTransform>(ui, "tutorialSuccess");
                    Check(fixture.TutorialFact == "move" && success.gameObject.activeSelf && body.gameObject.activeSelf
                        && Field<RectTransform>(ui, "tutorialDemo").gameObject.activeSelf && ui.TutorialActionReady("move"),
                        "Successful Grab keeps the next Lift instruction immediately actionable beside secondary feedback");
                    float remaining = Field<float>(ui, "successUntil") - Time.unscaledTime;
                    Check(remaining >= .2f && remaining <= .35f, "Secondary confirmation lasts 200-350 ms without withholding the next action");
                    typeof(ResearchFlowView).GetField("successUntil", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, Time.unscaledTime - 1);
                    typeof(ResearchFlowView).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
                    Check(body.gameObject.activeSelf && Plain(body.text) == ResearchStrings.Get("tutorial.try.move", fixture.Language)
                        && !success.gameObject.activeSelf, "Secondary confirmation expiry keeps the current action visible");
                    ui.LocalCommand("main_minimize", "None"); fixture.Evidence("move");
                    Check(fixture.TutorialFact == "move" && !ui.TutorialActionReady("move"), "Minimized handling prompt cannot receive Lift credit");
                    ui.LocalCommand("main_restore", "None");
                    Check(ui.TutorialActionReady("move"), "Restored handling prompt can receive the current action");
                    var promptCanvas = ui.mainPanel.GetComponent<Canvas>(); promptCanvas.enabled = false;
                    fixture.TutorialPromptShown(); fixture.Evidence("move");
                    Check(fixture.TutorialFact == "move" && Field<string>(fixture, "visibleTutorialFact") == null,
                        "Hidden placement Canvas invalidates the handling baseline and credit");
                    promptCanvas.enabled = true; fixture.TutorialPromptShown();
                    ui.TutorialFactCompleted("move");
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 2, true)); ui.Refresh();
                    Check(body.gameObject.activeSelf && !success.gameObject.activeSelf && Field<float>(ui, "successUntil") < Time.unscaledTime,
                        "Safety pause cancels confirmation and remains visible");
                    Field<HashSet<string>>(fixture, "tutorialEvidence").Clear();
                    heldPointers.Clear();
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 2)); ui.Refresh();
                    heldPointers.Add(731); Pointer(Oculus.Interaction.PointerEventType.Select);
                    heldPointers.Clear(); Pointer(Oculus.Interaction.PointerEventType.Unselect);
                    var retained = Field<HashSet<string>>(fixture, "tutorialEvidence");
                    Check(fixture.Flow.TutorialCompleted == 2 && fixture.TutorialFact == "move" && fixture.TutorialNeedsGrab
                        && retained.SetEquals(new[] { "grab" }) && !ui.TutorialActionReady("move")
                        && Plain(body.text) == ResearchStrings.Get("tutorial.try.regrab.controller", fixture.Language),
                        "Early user release preserves credited Grab and asks to regrab before the pending Lift");
                    heldPointers.Add(731); Pointer(Oculus.Interaction.PointerEventType.Select);
                    Check(fixture.TutorialFact == "move" && ui.TutorialActionReady("move"), "Regrab returns to the pending Lift without repeating credited Grab");
                    fixture.Evidence("move");
                    heldPointers.Clear(); Pointer(Oculus.Interaction.PointerEventType.Cancel);
                    Check(fixture.Flow.TutorialCompleted == 2 && fixture.TutorialFact == "rotate" && fixture.TutorialNeedsGrab
                        && retained.SetEquals(new[] { "grab", "move" }) && !retained.Contains("release"),
                        "System cancellation preserves taught facts and cannot earn participant Release credit");
                    Field<HashSet<string>>(fixture, "tutorialEvidence").Clear();
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 0)); ui.CancelTutorialConfirmation(); ui.Refresh();
                    fixture.Evidence("teleport"); fixture.Evidence("roomscale");
                    Check(fixture.Flow.TutorialCompleted == 0 && fixture.TutorialFact == "snap", "Out-of-order room-scale evidence cannot skip the visible Snap instruction");
                    promptCanvas.enabled = false; fixture.Evidence("snap");
                    Check(fixture.TutorialFact == "snap", "Hidden movement instruction remains unable to receive credit with secondary feedback");
                    promptCanvas.enabled = true;
                    Field<HashSet<string>>(fixture, "tutorialEvidence").Clear();
                    Set(fixture, "Logger", null);
                }
                using (var logger = new HandsLabLogger(failureLogs, "None", "Editor failure fixture", technicalTest: true, testData: true))
                {
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 1));
                    Set(fixture, "Error", ""); Set(fixture, "Logger", logger);
                    fixture.Evidence("grab");
                    var accepted = Field<HashSet<string>>(fixture, "tutorialEvidence");
                    Check(accepted.Contains("grab"), "Active initialized logger accepts tutorial observations");
                    logger.WriteFailed += message => typeof(ResearchSessionFlowController).GetMethod("LoggerFailed", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(fixture, new object[] { message });
                    var cycle = new Dictionary<string, object>(); cycle["self"] = cycle;
                    Check(!logger.Record("ACTION", "fixture.write_failure", "Attempted", cycle, "None", source: "Tester")
                        && logger.State == HandsLabLogState.WriteError && !string.IsNullOrEmpty(fixture.Error), "Real writer failure reaches controller safety gate");
                    fixture.Evidence("select");
                    Check(fixture.Flow.TutorialCompleted == 1 && !accepted.Contains("select"), "Failed logger callback cannot advance or accept tutorial evidence");
                    Set(fixture, "Error", ""); fixture.Evidence("select");
                    Check(fixture.Flow.TutorialCompleted == 1 && !accepted.Contains("select"), "Writer state independently blocks tutorial evidence");
                    Set(fixture, "Logger", null); fixture.Evidence("select");
                    Check(fixture.Flow.TutorialCompleted == 1 && !accepted.Contains("select"), "Uninitialized logger cannot accept tutorial evidence");
                }
                using (var logger = new HandsLabLogger(failureLogs, "None", "Editor interruption-write failure", technicalTest: true, testData: true))
                {
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 2));
                    Set(fixture, "Error", ""); Set(fixture, "Logger", logger); ui.CancelTutorialConfirmation(); ui.Refresh();
                    fixture.Evidence("grab");
                    logger.WriteFailed += message => typeof(ResearchSessionFlowController).GetMethod("LoggerFailed", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fixture, new object[] { message });
                    Field<StreamWriter>(logger, "eventsWriter").Dispose(); fixture.Evidence("release");
                    Check(logger.State == HandsLabLogState.WriteError && fixture.Flow.TutorialCompleted == 2 && !Field<bool>(fixture, "interactionAllowed") && !string.IsNullOrEmpty(fixture.Error),
                        "Interruption-event write failure stops safely without tutorial advancement");
                    Check(Field<HashSet<string>>(fixture, "tutorialEvidence").Contains("grab") && !Field<RectTransform>(ui, "tutorialSuccess").gameObject.activeSelf,
                        "Failed interruption preserves prior evidence and removes optimistic confirmation");
                    Field<HashSet<string>>(fixture, "tutorialEvidence").Clear(); Set(fixture, "Logger", null);
                }
                using (var logger = new HandsLabLogger(failureLogs, "None", "Editor completion-write failure", technicalTest: true, testData: true))
                {
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 1));
                    Set(fixture, "Error", ""); Set(fixture, "Logger", logger); ui.CancelTutorialConfirmation(); ui.Refresh();
                    logger.WriteFailed += message => typeof(ResearchSessionFlowController).GetMethod("LoggerFailed", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fixture, new object[] { message });
                    Field<StreamWriter>(logger, "eventsWriter").Dispose();
                    fixture.Evidence("select");
                    Check(logger.State == HandsLabLogState.WriteError && fixture.Flow.TutorialCompleted == 1 && fixture.Flow.State == ResearchFlowState.TutorialInteractions,
                        "Completion-event write failure restores prior flow without persisting advancement");
                    Check(body.gameObject.activeSelf && !Field<RectTransform>(ui, "tutorialSuccess").gameObject.activeSelf,
                        "Completion write failure cancels optimistic success feedback");
                    Set(fixture, "Logger", null);
                }
                var path = Path.Combine(evidenceDirectory, "tutorial-error-runtime.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(new { checks, count = checks.Count, scope = "Inactive isolated actual Unity view and gate fixture; no hardware or rendered-pixel claim" }, Formatting.Indented));
                return "TUTORIAL_ERROR_VIEW_PASS checks=" + checks.Count + " " + path;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string CheckTutorialReferenceEarlyClose(string evidenceFolder)
        {
            using var legacyPolicy = ResearchUIPolicy.LegacyRegressionScope(); // exercises the retired panel-drag lesson
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().First(v => v.thaiFont && v.japaneseFont && v.englishFont);
            var root = new GameObject("ResearchFlow_EarlyCloseFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            void Set(object target, string property, object value) => target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var ui = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                fixture.view = ui; ui.controller = fixture;
                var registry = new ResearchParticipantRegistry(Path.Combine(evidenceFolder, "registry"));
                var fixtureHead = new GameObject("IsolatedReferenceHead"); fixtureHead.transform.SetParent(root.transform);
                fixtureHead.transform.SetPositionAndRotation(new Vector3(20, 1.65f, 20), Quaternion.Euler(0, 180, 0)); ui.Head = fixtureHead.transform;
                foreach (var panel in new[] { ui.mainPanel, ui.referencePanel }) typeof(ResearchPanelMotion)
                    .GetField("environment", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, Array.Empty<Renderer>());
                void AdvanceConfirmation()
                {
                    typeof(ResearchFlowView).GetField("successUntil", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, Time.unscaledTime - 1);
                    typeof(ResearchFlowView).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
                }
                var record = registry.CreateNext(ParticipantGroup.Dev);
                record = registry.StartOrResume(record.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller);
                Set(fixture, "Registry", registry); Set(fixture, "Record", record);
                Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                using (var logger = new HandsLabLogger(evidenceFolder, "None", "Editor early-close fixture", technicalTest: true, testData: true))
                {
                    Set(fixture, "Logger", logger);
                    ui.Refresh();
                    fixture.Command("reference", "SimulatedController");
                    fixture.Command("reference_minimize", "SimulatedController");
                    if (!fixture.ReferenceMinimized || ui.referencePanel.transform.Find("Expanded").gameObject.activeSelf
                        || !ui.referencePanel.transform.Find("MinimizedReference").gameObject.activeSelf)
                        throw new InvalidOperationException("Reference minimize must render immediately during confirmation");
                    fixture.Command("reference_restore", "SimulatedController");
                    fixture.Command("reference_close", "SimulatedController");
                    if (fixture.Flow.TutorialCompleted != 3 || fixture.ReferenceOpen || fixture.TutorialFact != "reference"
                        || !((HashSet<string>)typeof(ResearchSessionFlowController).GetField("tutorialEvidence", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture)).Contains("reference"))
                        throw new InvalidOperationException("Early close must preserve completed Open and allow reopening incomplete Reference practice");
                    fixture.Command("reference", "SimulatedController");
                    AdvanceConfirmation();
                    fixture.Command("reference_tab.step_by_step", "SimulatedController");
                    AdvanceConfirmation();
                    fixture.DemoTutorialEvidence("reposition");
                    if (!fixture.ReferenceOpen || fixture.Flow.TutorialCompleted != 3 || fixture.TutorialFact != "close") throw new InvalidOperationException("Reference practice cannot finish before visible Close instruction");
                    AdvanceConfirmation(); fixture.Command("reference_close", "SimulatedController");
                    if (fixture.ReferenceOpen || fixture.ReferenceMinimized) throw new InvalidOperationException("Completed Reference closes before confirmation");
                    AdvanceConfirmation();
                    var main = (CanvasGroup)typeof(ResearchFlowView).GetField("mainGroup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
                    var inspect = ui.mainPanel.GetComponentsInChildren<ResearchUIAction>(true).Single(a => a.command == "inspect");
                    if (fixture.Flow.TutorialCompleted != 4 || fixture.ReferenceOpen || fixture.ReferenceMinimized || !main.interactable || !main.blocksRaycasts || !inspect.gameObject.activeSelf)
                        throw new InvalidOperationException("Reference completion must expose actionable Inspect; ReferenceOpen=" + fixture.ReferenceOpen);
                    ui.LocalCommand("inspect", "SimulatedController");
                    if (fixture.Flow.TutorialCompleted != 5 || !string.IsNullOrEmpty(fixture.Error)) throw new InvalidOperationException("Recovered Inspect must complete Tutorial without error");
                }
                return "EARLY_CLOSE_PASS checks=6; actual controller/view/logger integration, no hardware input";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Acceptance result is what shared UI feedback uses; rejected/no-op commands must never confirm success.
        public static string CheckAcceptedUICommands(string evidenceFolder)
        {
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().First(v => v.thaiFont && v.japaneseFont && v.englishFont);
            var root = new GameObject("ResearchFlow_UIAcceptanceFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var checks = new List<string>();
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            void Set(object target, string property, object value) => target.GetType().GetProperty(property, flags).GetSetMethod(true).Invoke(target, new[] { value });
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var ui = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                fixture.view = ui; ui.controller = fixture;
                var head = new GameObject("AcceptanceHead"); head.transform.SetParent(root.transform);
                head.transform.SetPositionAndRotation(new Vector3(20, 1.65f, 20), Quaternion.Euler(0, 180, 0)); ui.Head = head.transform;
                foreach (var panel in new[] { ui.mainPanel, ui.referencePanel }) typeof(ResearchPanelMotion).GetField("environment", flags).SetValue(panel, Array.Empty<Renderer>());
                Check(!ui.LocalCommand("unknown", "None") && !fixture.Command("technical_start"), "Setup rejects unknown/unavailable actions");
                Set(fixture, "TechnicalSelfSetup", true);
                Check(fixture.Command("technical_order") && fixture.SetupOrder == TaskOrder.BA, "Accepted setup action reports its actual changed option");
                Check(!fixture.Command("technical_start") && fixture.Record == null, "Start readiness rejection reports false without starting a session");
                Check(!ResearchUIFeedback.TryGetHapticDevice(fixture, 731, out _), "Unattributed Editor input cannot resolve a native controller for haptics");

                var registry = new ResearchParticipantRegistry(Path.Combine(evidenceFolder, "registry"));
                var record = registry.CreateNext(ParticipantGroup.Dev);
                record = registry.StartOrResume(record.participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller, TaskOrder.AB);
                Set(fixture, "Registry", registry); Set(fixture, "Record", record);
                Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Ready, 5));
                using (var logger = new HandsLabLogger(evidenceFolder, "None", "Editor accepted-command fixture", technicalTest: true, testData: true))
                {
                    Set(fixture, "Logger", logger); ui.Refresh();
                    Check(ui.LocalCommand("task_start", "None") && !fixture.Flow.IsTaskActive && fixture.TaskStartConfirmation, "Start requests confirmation without starting time");
                    Check(ui.LocalCommand("task_start_back", "None") && !fixture.TaskStartConfirmation && fixture.Flow.TutorialCompleted == 5, "Back retains Training");
                    Check(ui.LocalCommand("task_start", "None") && ui.LocalCommand("task_start_confirm", "None") && fixture.Flow.IsTaskActive && !ui.LocalCommand("task_start_confirm", "None"), "Confirmed Start accepts once and rejects a repeated state transition");
                    Check(fixture.Command("reference") && fixture.ReferenceOpen && !fixture.Command("reference"), "Reference accepts opening once and rejects reopening an open panel");
                    Check(!fixture.Command("reference_tab.video") && !fixture.Command("reference_tab." + fixture.ReferenceSection), "Unavailable/current formats are rejected without success feedback");
                    Check(fixture.Command("reference_minimize") && !fixture.Command("reference_minimize"), "Reference Minimize reports real state change only");
                    Check(fixture.Command("reference_restore") && !fixture.Command("reference_restore"), "Reference Restore reports real state change only");
                    Check(fixture.Command("reference_close") && !fixture.Command("reference_close"), "Reference Close reports real state change only");
                    Check(!ui.LocalCommand("main_restore", "None"), "Opening Reference preserves the expanded task surface");
                    Check(ui.LocalCommand("main_minimize", "None") && !ui.LocalCommand("main_minimize", "None"), "Main Minimize accepts only the visible expanded panel");
                    Check(ui.LocalCommand("main_restore", "None"), "Main Restore remains accepted after participant minimization");
                    Set(fixture, "Transitioning", true);
                    Check(!ui.LocalCommand("reference", "None") && !ui.LocalCommand("main_recenter", "None"), "Transition gate rejects both session and local panel commands");
                    Set(fixture, "Transitioning", false);
                    Set(fixture, "Error", "fixture_error");
                    Check(!ui.LocalCommand("reference", "None") && !ui.LocalCommand("main_minimize", "None"), "Error gate rejects both session and local panel commands");
                    Set(fixture, "Error", "");

                    var comparison = root.AddComponent<ResearchRenderExperiment>(); Set(comparison, "Running", true); Set(fixture, "RenderComparison", comparison);
                    Check(!ui.LocalCommand("reference", "None") && !ui.LocalCommand("main_recenter", "None"), "Controlled render comparison rejects unrelated session and panel actions");
                    Check(ui.LocalCommand("pause", "None") && fixture.Flow.Paused && !ui.LocalCommand("pause", "None"), "Safety Pause accepts once during comparison");
                    Check(ui.LocalCommand("help_request", "None") && fixture.HelpRequested && !ui.LocalCommand("help_request", "None"), "Paused Help accepts once during comparison");
                    Check(!ui.LocalCommand("session_resume", "None") && fixture.Flow.Paused, "Comparison rejects participant resume");
                    Set(comparison, "Running", false); Set(fixture, "RenderComparison", null);
                    Check(ui.LocalCommand("session_resume", "None") && !fixture.Flow.Paused && !ui.LocalCommand("session_resume", "None"), "Resume reports real accepted transition only");
                    Check(!ui.LocalCommand("inspect", "None") && !string.IsNullOrEmpty(fixture.Error), "Missing inspection adapter reports rejection without successful-action feedback");
                    Set(fixture, "Error", "");
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5, false, false)); ui.Refresh();
                    Check(ui.LocalCommand("inspect_continue", "None") && !fixture.Flow.InspectPassed.HasValue && !ui.LocalCommand("inspect_continue", "None"), "Retry accepts leaving an actual failed inspection once");
                    var port = root.AddComponent<ResearchTaskPort>(); port.taskId = "A"; port.demoMode = true; port.SetInteractionAllowed(true); fixture.taskPorts = new[] { port };
                    Check(ui.LocalCommand("inspect", "None") && fixture.Flow.InspectPassed == false,
                        "A valid failed inspection still accepts the UI request; feedback does not encode correctness");
                    Check(ui.LocalCommand("inspect_continue", "None"), "Valid failed inspection retains accepted Retry");
                    fixture.SetDemoPass(true);
                    Check(ui.LocalCommand("inspect", "None") && fixture.Flow.State == ResearchFlowState.PostTask1Checkpoint && !ui.LocalCommand("inspect", "None"),
                        "A valid passed inspection accepts once and goes straight to the checkpoint");
                    Check(ui.LocalCommand("pause", "None") && fixture.Flow.Paused && !fixture.HelpRequested,
                        "Pause before researcher abort has no outstanding Help request");
                    fixture.ResearcherAbort();
                    Check(fixture.Record.session_state == RegistryState.Aborted && !fixture.LoggerReady && string.IsNullOrEmpty(fixture.Error),
                        "Actual researcher abort closes the logger without a write error");
                    int closedRows = File.ReadAllLines(logger.EventsPath).Length;
                    Check(!ui.LocalCommand("help_request", "None") && !fixture.HelpRequested && File.ReadAllLines(logger.EventsPath).Length == closedRows,
                        "Closed logger rejects Help without optimistic confirmation or an unrecorded request");
                    Set(fixture, "Logger", null);
                }
                Directory.CreateDirectory(evidenceFolder);
                var path = Path.Combine(evidenceFolder, "accepted-commands.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(new { checks, count = checks.Count, hardware_verified = false,
                    scope = "Inactive actual controller/view dispatch; no native haptic submission or felt-vibration evidence" }, Formatting.Indented));
                return "UI_ACCEPTANCE_PASS checks=" + checks.Count + " " + path;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string Status { get; private set; } = "Idle";
        public static string LastEvidencePath { get; private set; } = "";
        private static double fixtureFocusReadyAfter;
        private static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        private static readonly List<string> assertions = new List<string>();
        private static readonly List<object> screenshots = new List<object>();
        private static readonly List<object> canvasCost = new List<object>();
        private static object lastArrivalCaptureEvidence;
        private static ResearchSessionFlowController controller;
        private static EditorWindow gameView;
        private static int oldDisplay;
        private static Canvas researcherCanvas;
        private static bool oldResearcherEnabled;
        private static string output, prefix, requestedLanguage;
        private static TaskOrder requestedOrder;
        private static float originalTimeScale;
        private static double deadline;
        private static bool technicalSetup;
        private static bool stopAtFirstTask;

        // technicalSetup: start from the in-headset technical setup and use the tester-labelled checkpoint/demo controls.
        public static string CheckWorkflowHierarchy()
        {
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load ResearchFlow integration scene fonts first.");
            var root = new GameObject("ResearchFlow_WorkflowHierarchyFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var failures = new List<string>(); int checks = 0;
            void Check(bool value, string description) { checks++; if (!value) failures.Add(description); }
            void Set(object target, string property, object value) => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetSetMethod(true).Invoke(target, new[] { value });
            T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var ui = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                fixture.view = ui; ui.controller = fixture;
                foreach (var order in new[] { TaskOrder.AB, TaskOrder.BA })
                {
                    foreach (var state in new[] { ResearchFlowState.TutorialWelcome, ResearchFlowState.TutorialComplete, ResearchFlowState.Task1Ready, ResearchFlowState.PostTask1Checkpoint, ResearchFlowState.Task2Ready, ResearchFlowState.PostTask2Checkpoint, ResearchFlowState.ParticipantComplete })
                    {
                        Set(fixture, "Flow", new ResearchSessionFlow(order, state, state >= ResearchFlowState.TutorialComplete ? 5 : 0));
                        ui.Refresh();
                        Check(ui.mainPanel.centered && !ui.mainPanel.movable, order + " " + state + " is a fixed centered transition");
                        Check(!Field<RectTransform>(ui, "checklist").gameObject.activeSelf && !Field<TMP_Text>(ui, "tutorialProgress").gameObject.activeSelf && !Field<RectTransform>(ui, "tutorialSuccess").gameObject.activeSelf, state + " has no tutorial progress/success clutter");
                        var actions = Field<Button[]>(ui, "actions").Where(b => b.gameObject.activeSelf).ToArray();
                        if (state == ResearchFlowState.TutorialComplete || state == ResearchFlowState.PostTask2Checkpoint || state == ResearchFlowState.ParticipantComplete)
                            Check(Field<GameObject>(ui, "positiveIcon").activeSelf, state + " has a visible semantic success icon");
                        if (state == ResearchFlowState.PostTask2Checkpoint || state == ResearchFlowState.ParticipantComplete)
                            Check(!ui.mainPanel.transform.Find("Expanded/WaitingResearcherIcon").gameObject.activeSelf
                                && Field<TMP_Text>(ui, "body").text == ResearchStrings.Get("completion.body", fixture.Language),
                                state + " names researcher ownership in its text without a decorative marker");
                        if (state == ResearchFlowState.PostTask1Checkpoint || state == ResearchFlowState.PostTask2Checkpoint || state == ResearchFlowState.ParticipantComplete)
                            Check(actions.Length == 0 && !Field<TMP_Text>(ui, "waitingLabel").gameObject.activeSelf
                                && Field<TMP_Text>(ui, "body").text == ResearchStrings.Get(state == ResearchFlowState.PostTask1Checkpoint ? "checkpoint.body" : "completion.body", fixture.Language),
                                state + " names researcher ownership once without participant advance");
                        else
                            Check(actions.Length == 1 && !new[] { "action.continue", "action.start" }.Select(k => ResearchStrings.Get(k, fixture.Language)).Contains(actions[0].GetComponentInChildren<TMP_Text>(true).text), state + " has one predictive action");
                        if (state == ResearchFlowState.Task1Ready || state == ResearchFlowState.Task2Ready)
                            Check(Field<TMP_Text>(ui, "title").text.StartsWith(string.Format(ResearchStrings.Get("task.progress", fixture.Language), state == ResearchFlowState.Task2Ready ? 2 : 1))
                                && Field<TMP_Text>(ui, "body").text.StartsWith(string.Format(ResearchStrings.Get("task.ready.goal", fixture.Language), "").TrimEnd()),
                                order + " " + state + " shares one Ready structure (task n of 2, goal, Start)");
                        if (state == ResearchFlowState.PostTask1Checkpoint)
                            Check(ui.mainPanel.transform.Find("Expanded/CheckpointSteps") && ui.mainPanel.transform.Find("Expanded/CheckpointSteps").gameObject.activeSelf
                                && ui.mainPanel.transform.Find("Expanded/CheckpointSteps").childCount == 3, "Checkpoint has three labeled semantic icon rows");
                    }
                }
                Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                Set(fixture, "ReferenceOpen", true); Set(fixture, "ReferenceSection", "illustrated"); ui.Refresh();
                var coach = Field<GameObject>(ui, "mainContent").GetComponent<RectTransform>();
                var tabs = ui.referencePanel.GetComponentsInChildren<RectTransform>(true).Where(t => t.name.StartsWith("Tab_") && t.gameObject.activeSelf).ToArray();
                Check(tabs.Length == 4 && tabs.Max(t => t.anchoredPosition.y) - tabs.Min(t => t.anchoredPosition.y) < 1 && tabs.Max(t => t.anchoredPosition.x) - tabs.Min(t => t.anchoredPosition.x) > 300, "Tutorial Reference preserves four equal format identities in one tab row");
                Check(Mathf.Abs(tabs.Average(t => t.anchoredPosition.x)) < 1, "Available Reference tabs stay centered after page layout");
                Check(!fixture.ReferenceAvailable("video"), "Tutorial does not offer an unavailable video");
                var integratedCue = ui.referencePanel.transform.Find("Expanded/TutorialCue").GetComponent<TMP_Text>();
                Check(!ui.mainPanel.gameObject.activeSelf && integratedCue.gameObject.activeSelf && !Field<RectTransform>(ui, "checklist").gameObject.activeSelf && !Field<TMP_Text>(ui, "tutorialProgress").gameObject.activeSelf, "Reference integrates only the current Tutorial cue into its reader");
                var evidence = Field<HashSet<string>>(fixture, "tutorialEvidence");
                evidence.UnionWith(new[] { "reference", "switch" }); ui.Refresh();
                Check(!ui.mainPanel.gameObject.activeSelf && Plain(integratedCue.text).Contains(ResearchStrings.Get(fixture.TutorialInstructionKey, fixture.Language)), "Reference drag instruction remains in the same reader surface");
                evidence.Clear(); ui.Refresh();
                foreach (var key in new[] { "Pause", "Minimize", "Close" })
                {
                    var control = (RectTransform)ui.referencePanel.transform.Find("Expanded/" + key);
                    var expanded = (RectTransform)control.parent;
                    Check(control.anchoredPosition.y > expanded.sizeDelta.y / 2 - 100 && Mathf.Abs(control.anchoredPosition.x) + control.sizeDelta.x / 2 <= 390
                        && control.sizeDelta.y >= 56, "Reference " + key + " is compact in the header with a usable hit target");
                    Check(control.Find("Label").gameObject.activeSelf == (key == "Pause") && control.GetComponentInChildren<ResearchUtilityIcon>(true)
                        && control.GetComponent<ResearchControlHint>().key == (key == "Pause" ? "action.pause_short" : "action." + key.ToLowerInvariant()),
                        "Reference " + key + " uses a mesh icon and localized tooltip; Pause keeps its caption");
                }
                Check(ui.referencePanel.transform.Find("Expanded/Pause").GetComponent<ResearchUIAction>().command == "pause", "Pause stays reachable while Reference is open");
                var illustration = ui.referencePanel.transform.Find("Expanded/IllustratedInstruction");
                Check(illustration && illustration.gameObject.activeSelf && illustration.Cast<Transform>().Count(t => t.name.StartsWith("Frame_")) == 4, "Tutorial Illustrated Instruction contains four visual panels");
                Check(illustration && illustration.Cast<Transform>().Where(t => t.name.StartsWith("Frame_")).All(t => !t.Find("Card") || !t.Find("Card").gameObject.activeSelf)
                    && illustration.Find("Order_1"), "Illustrated sequence has reading arrows without nested cards");
                Set(fixture, "ReferenceSection", "user_manual"); ui.Refresh();
                var reader = Field<TMP_Text>(ui, "referenceBody");
                var readerScroll = Field<ScrollRect>(ui, "scroll");
                reader.text = "First page<page>Second page<page>Third page"; reader.ForceMeshUpdate(true);
                Field<RectTransform>(ui, "referenceContent").sizeDelta = new Vector2(-20, readerScroll.viewport.rect.height * 3);
                readerScroll.verticalNormalizedPosition = 0; readerScroll.onValueChanged.Invoke(Vector2.zero);
                Check(reader.pageToDisplay == 3 && Field<Button>(ui, "previousPage").interactable && !Field<Button>(ui, "nextPage").interactable,
                    "Scroll selects the last actual page and disables further advance");
                readerScroll.verticalNormalizedPosition = 1; readerScroll.onValueChanged.Invoke(Vector2.one);
                Check(reader.pageToDisplay == 1 && !Field<Button>(ui, "previousPage").interactable && Field<Button>(ui, "nextPage").interactable,
                    "Scroll returns to the first page and disables backwards movement");
                readerScroll.onValueChanged.Invoke(Vector2.one);
                Check(reader.pageToDisplay == 1, "Repeated unchanged-page callback retains the current page");
                readerScroll.verticalNormalizedPosition = .8f; readerScroll.onValueChanged.Invoke(Vector2.zero);
                Check(reader.pageToDisplay == 1 && !Field<Button>(ui, "previousPage").interactable,
                    "Partial scroll within the first page keeps Previous disabled");
                Check(!ui.LocalCommand("reference_previous", "None") && Mathf.Approximately(readerScroll.verticalNormalizedPosition, .8f),
                    "Previous rejects a first-page no-op without moving the scroll position");
                Check(ui.LocalCommand("reference_next", "None") && reader.pageToDisplay == 2
                    && Mathf.Approximately(readerScroll.verticalNormalizedPosition, .5f),
                    "Next from a partial first page selects the exact second-page anchor");
                readerScroll.verticalNormalizedPosition = .2f; readerScroll.onValueChanged.Invoke(Vector2.zero);
                Check(reader.pageToDisplay == 3 && !Field<Button>(ui, "nextPage").interactable,
                    "Partial scroll within the last page keeps Next disabled");
                Check(!ui.LocalCommand("reference_next", "None") && Mathf.Approximately(readerScroll.verticalNormalizedPosition, .2f),
                    "Next rejects a last-page no-op without moving the scroll position");
                Check(ui.LocalCommand("reference_previous", "None") && reader.pageToDisplay == 2
                    && Mathf.Approximately(readerScroll.verticalNormalizedPosition, .5f),
                    "Previous from a partial last page selects the exact second-page anchor");
                var drag = ui.referencePanel.GetComponentInChildren<ResearchDragHandle>(true);
                Check(drag.GetComponent<Graphic>().color == ResearchUITheme.Elevated && drag.GetComponent<RectTransform>().rect.width >= 320
                    && drag.GetComponent<RectTransform>().rect.height >= 80
                    && drag.transform.Find("GripLine").gameObject.activeSelf, "Drag grip keeps a light opaque background and large hit target");
                Set(fixture, "Record", new ParticipantRecord { test_data = false });
                Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5)); ui.Refresh();
                Check(!illustration || !illustration.gameObject.activeSelf, "Tutorial diagrams never appear as experimental task answers");
                Check(ResearchReferenceScheme.Active.All(fixture.ReferenceAvailable), "Study retains every approved information format");
                Set(fixture, "Record", new ParticipantRecord { test_data = true });
                Set(fixture, "ReferenceSection", "user_manual"); ui.Refresh();
                Check(ui.referencePanel.GetComponentsInChildren<ResearchUIAction>(true).Count(a => a.command.StartsWith("reference_tab.") && a.gameObject.activeSelf) == 4
                    && ui.referencePanel.GetComponentsInChildren<ResearchUIAction>(true).Where(a => a.command == "reference_tab.video" || a.command == "reference_tab.illustrated").All(a => !a.GetComponent<Button>().interactable),
                    "DEV Reference preserves unavailable illustrated/video identities without enabling them");
                fixture.Command("reference_tab.video", "ProgrammaticOfflineFixture");
                Check(fixture.ReferenceSection == "user_manual", "Shared availability gate rejects a direct unavailable format command");
                Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                Set(fixture, "Record", new ParticipantRecord { test_data = true, input_mode = ExperimentInputMode.HandTracking });
                Set(fixture, "ReferenceSection", "step_by_step");
                Check(fixture.ReferenceText == ResearchStrings.Get("reference.training.step_by_step.hand", fixture.Language), "Diagnostic hand mode gets hand-only practice steps");
                fixture.view = null; // The mutation gate fixture has no UI/native input side effects.
                var benchmark = root.AddComponent<ResearchRenderExperiment>();
                Set(fixture, "RenderComparison", benchmark); Set(benchmark, "Running", true);
                string gatePath = Path.Combine(Path.GetTempPath(), "ResearchRenderMutationGate-" + Guid.NewGuid().ToString("N"));
                var registry = new ResearchParticipantRegistry(gatePath);
                Set(fixture, "Registry", registry);
                Set(fixture, "Record", registry.StartOrResume(registry.CreateNext(ParticipantGroup.Dev).participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller));
                using (var logger = new HandsLabLogger(gatePath, "None", "Editor render mutation gate fixture", technicalTest: true, testData: true))
                {
                    Set(fixture, "Logger", logger);
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Ready, 5));
                    Check(!fixture.Command("task_start", "SimulatedController") && !fixture.TaskStartConfirmation && fixture.Flow.State == ResearchFlowState.Task1Ready, "Running render comparison blocks both task request and participant advance");
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 1));
                    Field<HashSet<string>>(fixture, "tutorialEvidence").Clear();
                    typeof(ResearchSessionFlowController).GetMethod("Evidence", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(bool) }, null)
                        .Invoke(fixture, new object[] { "select", true });
                    Check(fixture.Flow.TutorialCompleted == 1 && Field<HashSet<string>>(fixture, "tutorialEvidence").Count == 0,
                        "Running render comparison does not accumulate tutorial evidence or advance flow");
                    fixture.Command("pause", "SimulatedController");
                    Check(fixture.Flow.Paused && fixture.Record.paused && string.IsNullOrEmpty(fixture.Error), "Safety Pause remains effective and persisted during a render comparison");
                    fixture.Command("session_resume", "SimulatedController");
                    Check(fixture.Flow.Paused, "Participant cannot resume until the render comparison has ended");
                    Set(benchmark, "Running", false);
                    fixture.Command("session_resume", "SimulatedController");
                    Check(!fixture.Flow.Paused && string.IsNullOrEmpty(fixture.Error), "Ending the comparison releases the flow mutation gate");
                }
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
                return "WORKFLOW_HIERARCHY_PASS " + checks + " assertions (Editor fixture only)";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string Begin(string language = "Thai", bool orderAB = true, bool technicalSetup = false, string evidenceDirectory = null,
            bool stopAtFirstTask = false)
        {
            ResearchFlowPlayChecks.technicalSetup = technicalSetup;
            // Hands the started first task to a real-input SDK runner instead of the demo-pass fixtures below.
            ResearchFlowPlayChecks.stopAtFirstTask = stopAtFirstTask;
            if (Status == "Running") throw new InvalidOperationException("A ResearchFlow check is already running.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) throw new InvalidOperationException("Enter unpaused Play Mode first.");
            if (language != "Thai" && language != "Japanese") throw new ArgumentException("Use Thai or Japanese.");
            controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            if (controller == null || controller.Record != null) throw new InvalidOperationException("Use a fresh Play Mode controller with no session open.");
            requestedLanguage = language; requestedOrder = orderAB ? TaskOrder.AB : TaskOrder.BA;
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "ResearchFlowEvidence", "current-source-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(Path.Combine(output, "final-screenshots"));
            if (!string.IsNullOrEmpty(evidenceDirectory)) output = Path.GetFullPath(evidenceDirectory);
            Directory.CreateDirectory(Path.Combine(output, "final-screenshots"));
            prefix = (technicalSetup ? "Technical_" : "") + language + "_" + requestedOrder + "_" + DateTime.UtcNow.ToString("HHmmssfff");
            LastEvidencePath = Path.Combine(output, prefix + "_play-checks.json");
            assertions.Clear(); screenshots.Clear(); routines.Clear(); canvasCost.Clear();
            lastArrivalCaptureEvidence = null;
            originalTimeScale = Time.timeScale;
            gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            gameView.Focus(); gameView.Repaint(); fixtureFocusReadyAfter = EditorApplication.timeSinceStartup + .2d;
            oldDisplay = ReadDisplay();
            var researcher = UnityEngine.Object.FindFirstObjectByType<ResearcherSetupView>();
            researcherCanvas = researcher != null ? researcher.GetComponent<Canvas>() : null;
            oldResearcherEnabled = researcherCanvas != null && researcherCanvas.enabled;
            Status = "Running"; deadline = EditorApplication.timeSinceStartup + 180;
            routines.Push(Run());
            EditorApplication.update += Tick;
            return "Running technical Editor demo: " + prefix;
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new InvalidOperationException("Play Mode ended or 180-second timeout reached.");
                // Owned callbacks require the real Game View focus; let its safety state settle after regaining it.
                // Never bypass the controller's focus/tracking gates or dispatch a simulated action in the focus-change frame.
                if (!Application.isFocused)
                { gameView.Focus(); fixtureFocusReadyAfter = EditorApplication.timeSinceStartup + .2d; return; }
                if (EditorApplication.timeSinceStartup < fixtureFocusReadyAfter) return;
                if (routines.Count == 0) { Finish(null); return; }
                var current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); return; }
                if (current.Current is IEnumerator nested) routines.Push(nested);
            }
            catch (Exception error) { Finish(error); }
        }

        private static IEnumerator Run()
        {
            Require(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, "Exactly one EventSystem");
            Require(controller.origin != null && controller.origin.Camera != null, "Existing XR Origin and camera available");
            Require(researcherCanvas != null && researcherCanvas.targetDisplay == 1, "Researcher canvas targets Display 2 only");
            ResearcherParticipantDropdown idDropdown = null;
            if (technicalSetup) { Require(controller.TechnicalSelfSetup, "Editor package identity is the technical package"); yield return TechnicalSetupStart(); goto started; }
            researcherCanvas.enabled = true; // Offline screenshot only; never retarget to headset display.
            SetDisplay(1); yield return Stable();
            yield return Capture("01_researcher_setup");
            idDropdown = researcherCanvas.GetComponentsInChildren<ResearcherParticipantDropdown>(true).Single(d => d.allowed.Length > 0);
            idDropdown.Show(); yield return Stable();
            var popupCanvases = researcherCanvas.GetComponentsInChildren<Canvas>(false).Where(c => c.isActiveAndEnabled).ToArray();
            Require(popupCanvases.All(c => c.targetDisplay == 1), "Every active researcher dropdown Canvas remains on Display 2");
            Require(popupCanvases.Any(c => c.name == "Dropdown List") && popupCanvases.Any(c => c.name == "Blocker"), "Actual dropdown popup and blocker are active on researcher display");
            var activePopup = popupCanvases.Single(c => c.name == "Dropdown List");
            var popupBackground = activePopup.GetComponent<Image>();
            Require(popupBackground != null && popupBackground.isActiveAndEnabled && Mathf.Approximately(popupBackground.color.a, 1f), "Active researcher dropdown background is opaque and occludes the underlying form");
            foreach (var toggle in idDropdown.GetComponentsInChildren<Toggle>(false))
                if (toggle.GetComponentInChildren<TMP_Text>()?.text.Contains("Completed") == true)
                    Require(!toggle.interactable, "Completed participant dropdown option is locked");
            yield return Capture("researcher_participant_dropdown");
            idDropdown.Hide(); yield return Stable();
            researcherCanvas.enabled = oldResearcherEnabled;
            SetDisplay(0); yield return Stable();

            // P12: Task A/B content is unapproved, so the scene ports must keep the study gate (StartSession) closed for TH/JP IDs.
            Require(controller.taskPorts.Length == 2 && controller.taskPorts.All(p => p && p.demoMode && !p.contentApproved && !p.IsReadyForStudy),
                "Task A/B ports are demo-only and not study-ready");
            var participant = controller.Registry.CreateNext(ParticipantGroup.Dev);
            controller.StartSession(participant.participant_id, requestedLanguage == "Thai" ? ParticipantLanguage.Thai : ParticipantLanguage.Japanese, ExperimentInputMode.TechnicalBoth, requestedOrder);
            ResearchFlowVisualInput.Begin(1.65f); // Explicit synthetic HMD, never a production rig offset.
            yield return Transition("arrival_training");
        started:
            Require(controller.Record != null && controller.Record.test_data && controller.Record.participant_id.StartsWith("DEV"), "Technical DEV record created; no study IDs consumed");
            Require(controller.Record.task_order == requestedOrder && controller.Record.language.ToString() == requestedLanguage, "Assigned order and language persisted");
            Require(controller.Flow.State == ResearchFlowState.TutorialWelcome, "Tutorial welcome entered");
            yield return Capture("02_tutorial_welcome");
            if (technicalSetup) yield return WorldAnchorChecks();
            yield return ExerciseButtonFeedback();
            Require(!controller.view.helperCanvas.gameObject.activeSelf, "Obsolete tabletop helper stays hidden");
            Click("tutorial_start"); yield return Stable();
            Require(controller.origin.GetComponentsInChildren<Oculus.Interaction.Locomotion.MovingSetting>(true).All(m => m.ControllerMovement.Value == Oculus.Interaction.Locomotion.MovingSetting.MovementStyle.Teleport), "Reloaded SDK movement setting is Teleport, not prefab default Slide");
            Require(controller.origin.GetComponentsInChildren<Oculus.Interaction.Locomotion.TurningSetting>(true).All(t => t.ControllerTurn.Value == Oculus.Interaction.Locomotion.TurningSetting.RotationStyle.Snap && Mathf.Abs(t.RotationSnapAngle.Value - 45) < .01f), "Reloaded SDK snap turn is 45 degrees");
            Require(controller.origin.GetComponentsInChildren<Oculus.Interaction.Locomotion.TurnerEventBroadcaster>(true).All(b => b.TurnMethod == Oculus.Interaction.Locomotion.TurnerEventBroadcaster.TurnMode.Snap && Mathf.Abs(b.SnapTurnDegrees - 45) < .01f), "Effective controller/hand turners are Snap 45, not serialized prefab Smooth");
            Require(controller.origin.GetComponentsInChildren<Transform>(true).Where(t => t.name == "ControllerLocomotionSlideActions").All(t => !t.gameObject.activeInHierarchy), "Smooth movement input remains unavailable in tutorial");
            var teleportInputs = controller.origin.GetComponentsInChildren<Oculus.Interaction.Locomotion.TeleportInteractor>(true).Where(t => t.name == "TeleportControllerInteractor" || t.name == "TeleportHandInteractor").ToArray();
            Require(teleportInputs.Length == 4 && teleportInputs.All(t => t.enabled && t.gameObject.activeSelf), "Four native controller/hand teleport inputs configured; tracking gates still apply");
            if (controller.guards.All(g => !g.TrackingValid)) Require(teleportInputs.All(t => !t.gameObject.activeInHierarchy), "Untracked locomotion stays blocked by the existing input guard");
            Require(controller.Flow.TutorialCompleted == 0, "Tutorial starts at movement");
            yield return ExerciseParticipantPause("tutorial");
            yield return Capture("tutorial_step_movement");
            Require(controller.view.mainPanel.transform.Find("Expanded/TutorialDemo").GetComponent<Canvas>() != null,
                "Looping control demo is on its own nested Canvas so it never rebuilds the slate batch");
            yield return MeasureCanvasCost("tutorial_demo_animating", 90);
            CheckTutorialPrompt("tutorial.try.teleport.controller");
            controller.DemoTutorialEvidence("teleport"); yield return Stable();
            CheckTutorialPrompt("tutorial.try.snap.controller");
            controller.DemoTutorialEvidence("teleport"); yield return Stable();
            CheckTutorialPrompt("tutorial.try.snap.controller");
            Require(controller.Flow.TutorialCompleted == 0, "Duplicate teleport does not skip remaining movement actions");
            yield return Capture("tutorial_movement_snap_prompt");
            controller.DemoTutorialEvidence("snap"); yield return Stable();
            CheckTutorialPrompt("tutorial.try.roomscale");
            Require(controller.Flow.TutorialCompleted == 0, "The previously hidden room-scale requirement stays explicit until performed");
            yield return Capture("tutorial_movement_roomscale_prompt");
            controller.DemoTutorialEvidence("roomscale"); yield return Stable();
            CheckTutorialPrompt("tutorial.try.select.controller");
            yield return Capture("tutorial_step_select");
            Click("tutorial_select"); yield return Stable();
            while (Time.unscaledTime < (float)typeof(ResearchFlowView).GetField("successUntil", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller.view)) { gameView.Repaint(); yield return null; }
            Require(controller.Flow.TutorialCompleted == 2, "Movement and actual selection button advance checklist");
            var rows = controller.view.GetComponentsInChildren<RectTransform>(true).Where(r => r.name.StartsWith("Step_")).OrderBy(r => r.name).ToArray();
            Require(rows.Length == 5 && rows.All(r => !r.gameObject.activeInHierarchy), "Tutorial hides decorative progress dots");
            var progress = controller.view.GetComponentsInChildren<TMP_Text>(false).Single(t => t.name == "TutorialProgress");
            progress.ForceMeshUpdate();
            Require(progress.text == string.Format(ResearchStrings.Get("tutorial.part_progress", requestedLanguage), 2, 4)
                && progress.rectTransform.rect.height <= 52 && !progress.isTextOverflowing
                && progress.preferredWidth <= progress.rectTransform.rect.width + 1 && progress.preferredHeight <= progress.rectTransform.rect.height + 1,
                "Tutorial progress shows the localized part (2 of 4) and fits its compact header area");
            Require(controller.view.mainPanel.transform.Find("Expanded/Title").GetComponent<TMP_Text>().text == ResearchStrings.Get("tutorial.fact.grab", requestedLanguage),
                "Tutorial heading is the current micro-action, not the broad step");
            Require(!controller.view.helperCanvas.transform.Find("Message").gameObject.activeSelf, "Helper message remains hidden until requested");
            Require(rows.Take(2).All(r => r.Find("Checkmark").gameObject.activeSelf), "Completed evidence retains its internal completion state");
            Require(rows.Skip(3).All(r => !r.Find("Checkmark").gameObject.activeInHierarchy
                && r.Find("Instruction").GetComponent<TMP_Text>().text == ResearchStrings.Get("tutorial.future", requestedLanguage)),
                "Future steps remain visibly pending without completion checkmarks");
            Require(rows[2].Find("Background").GetComponent<Graphic>().color != rows[3].Find("Background").GetComponent<Graphic>().color,
                "Current step is distinguished from future steps");
            yield return Capture("03_tutorial_checklist");
            Require(!rows[2].GetComponentsInChildren<Transform>(true).Any(t => t.name == "Checkmark" && t.gameObject.activeSelf), "Grab practice starts without completion checkmark");
            var handlingDetail = controller.view.mainPanel.GetComponentsInChildren<TMP_Text>(false).Single(t => t.name == "Body"
                && Plain(t.text) == ResearchStrings.Get("tutorial.try.grab.controller", requestedLanguage));
            Require(controller.view.GetComponentsInChildren<RectTransform>(false).Count(r => r.name.StartsWith("PracticeHighlight_")) == controller.trainingObjects.Count(o => o && !o.IsHeld),
                "Grab practice highlights each practice object on the table");
            handlingDetail.ForceMeshUpdate();
            Require(!handlingDetail.isTextOverflowing && handlingDetail.preferredHeight <= handlingDetail.rectTransform.rect.height + 1,
                "Current handling instruction fits its rendered detail area");
            if (requestedLanguage == "Japanese")
                Require(handlingDetail.textInfo.lineInfo.Take(handlingDetail.textInfo.lineCount).All(line => line.visibleCharacterCount <= 26),
                    "Japanese handling instruction has at most 26 visible characters per rendered line");
            // This rendered-copy sequence injects a held model explicitly; native acquisition/release is a separate fixture.
            var stagedObject = controller.trainingObjects.First(o => o);
            var stagedPointers = (HashSet<int>)typeof(VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabObject)
                .GetField("pointers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stagedObject);
            bool stagedKinematic = stagedObject.body.isKinematic;
            controller.Log("DEMO_INPUT", "training.visual_hold", "Injected", source: "Tester");
            stagedObject.body.isKinematic = true;
            try
            {
                foreach (var fact in new[] { "grab", "move", "rotate", "release" })
                {
                    yield return AwaitTutorialActionReady(fact);
                    if (fact == "grab") stagedPointers.Add(int.MinValue);
                    if (fact == "release") stagedPointers.Remove(int.MinValue);
                    controller.DemoTutorialEvidence(fact);
                    var success = controller.view.mainPanel.transform.Find("Expanded/TutorialSuccess");
                    Require(success.gameObject.activeInHierarchy && success.GetComponentInChildren<TMP_Text>().text.StartsWith(ResearchStrings.Get("tutorial.success", requestedLanguage)),
                        "Each completed action shows secondary feedback while the next action is visible: " + fact);
                    yield return Stable();
                    bool completed = fact == "release";
                    Require(controller.Flow.TutorialCompleted == (completed ? 3 : 2), "Cumulative tutorial evidence through " + fact + (completed ? " completes all four requirements" : " must not complete grab practice"));
                    if (fact == "grab") Require(!controller.view.GetComponentsInChildren<RectTransform>(false).Any(r => r.name.StartsWith("PracticeHighlight_")), "Practice highlight clears once the grab action succeeds");
                    CheckTutorialPrompt(fact == "grab" ? "tutorial.try.move" : fact == "move" ? "tutorial.try.rotate" :
                        fact == "rotate" ? "tutorial.try.release.controller" : "tutorial.try.reference");
                    yield return Capture("tutorial_grab_evidence_through_" + fact);
                    Require(rows[2].GetComponentsInChildren<Transform>(true).Any(t => t.name == "Checkmark" && t.gameObject.activeSelf) == completed, "Grab checkmark agrees with complete evidence after confirmation through " + fact);
                }
            }
            finally { stagedPointers.Remove(int.MinValue); stagedObject.body.isKinematic = stagedKinematic; }
            yield return Stable();
            Require(controller.Flow.State == ResearchFlowState.TutorialReferenceInspect, "Reference and Inspect practice entered");
            yield return Capture("tutorial_step_reference");
            Click("reference"); yield return Stable();
            yield return Capture("04_tutorial_reference_inspect");
            yield return ExerciseReference("tutorial");
            Require(controller.Flow.TutorialCompleted == 4, "Reference practice requires open, format switch, drag and close");
            yield return ToolbarClick("reference_close");
            CheckTutorialPrompt("tutorial.try.inspect");
            var inspectReadyBounds = ScreenBounds(controller.view.mainPanel.GetComponent<RectTransform>());
            Require(controller.origin.Camera.pixelRect.Contains(inspectReadyBounds.min) && controller.origin.Camera.pixelRect.Contains(inspectReadyBounds.max),
                "Expanded Inspect tutorial panel fits the viewport after closing Reference");
            yield return Capture("tutorial_checklist_inspect_ready");
            Click("inspect"); yield return Stable();
            Require(controller.Flow.State == ResearchFlowState.TutorialComplete && controller.Flow.TutorialCompleted == 5, "Five tutorial steps complete");
            yield return Capture("tutorial_complete");
            Require(rows.All(r => !r.gameObject.activeInHierarchy) && !progress.gameObject.activeInHierarchy && !controller.view.mainPanel.movable && controller.view.mainPanel.centered, "TutorialComplete is a fixed centered transition without practice progress clutter");
            var completedBounds = ScreenBounds(controller.view.mainPanel.GetComponent<RectTransform>());
            Require(controller.origin.Camera.pixelRect.Contains(completedBounds.min) && controller.origin.Camera.pixelRect.Contains(completedBounds.max),
                "Completed tutorial full panel remains inside the participant viewport after expanding");
            yield return Capture("tutorial_checklist_completed");
            Click("tutorial_continue"); yield return Transition("arrival_task_" + controller.Flow.CurrentTask);
            if (technicalSetup) WorldUnchanged("system station teleport (rig only)");

            for (int task = 0; task < 2; task++)
            {
                var expected = task == 0 ? requestedOrder == TaskOrder.AB ? "A" : "B" : requestedOrder == TaskOrder.AB ? "B" : "A";
                Require(controller.Flow.CurrentTask == expected, "Task " + (task + 1) + " resolves to " + expected);
                Click("task_start"); yield return Stable();
                Click("task_start_confirm"); yield return Stable();
                VerifyExperimentalUI();
                if (stopAtFirstTask) yield break;
                if (task == 0) yield return MeasureCanvasCost("task_static_ui", 90);
                yield return Capture((expected == "A" ? "05" : "09") + "_task_" + expected);
                if (task == 0)
                {
                    researcherCanvas.enabled = true; SetDisplay(1); yield return Stable();
                    yield return Capture("researcher_session_running");
                    researcherCanvas.enabled = oldResearcherEnabled; SetDisplay(0); yield return Stable();
                }
                if (task == 0)
                {
                    yield return ExerciseParticipantPause("task_" + expected);
                    VerifyExperimentalUI();
                }
                Click("reference"); yield return Stable();
                yield return Capture("06_reference_task_" + expected);
                yield return ExerciseReference("task_" + expected);
                yield return ExerciseContextualExamine(expected);
                Require(!ActiveCommands().Contains("main_restore"), "Reference and Examine leave an expanded task surface expanded");
                var port = controller.taskPorts.Single(p => p.taskId == expected);
                port.Action("demo.target.two", new { installed = false }, new { installed = true }, "SimulatedController", true);
                if (technicalSetup) Require(!port.demoPassed, "Technical demo fixture starts unfixed for task " + expected);
                else controller.SetDemoPass(false);
                Click("inspect"); yield return Stable();
                Require(controller.Flow.InspectPassed == false, "Explicit Inspect Fail enters modal");
                var texts = controller.view.mainPanel.GetComponentsInChildren<TMP_Text>().Select(t => t.text).ToArray();
                Require(texts.Contains(ResearchStrings.Get("inspect.fail.body", requestedLanguage)), "Fail modal contains exact neutral sentence");
                Require(ActiveCommands().Where(c => c != "helper").SequenceEqual(new[] { "inspect_continue" }), "Fail modal exposes only neutral Continue");
                yield return Capture("07_inspect_fail_task_" + expected);
                Click("inspect_continue"); yield return Stable();
                Require(controller.Flow.IsTaskActive && !controller.ReferenceOpen && !controller.Flow.InspectPassed.HasValue, "Fail Continue returns without opening Reference or hint");
                yield return ExerciseMinimizedModal();
                VerifyExperimentalUI();
                if (technicalSetup)
                {
                    // The explicit technical operator stages the demo state; participant screens never expose the fixture.
                    Click("inspect"); yield return Stable();
                    Require(controller.Flow.InspectPassed == false, "Unfixed demo fixture still fails before the tester sets it");
                    controller.Log("DEMO_INPUT", "task." + expected, "ExplicitOperatorDemoPassFixture", source: "Tester");
                    controller.SetDemoPass(true); yield return Stable();
                    Require(port.demoPassed && ActiveCommands().Where(c => c != "helper").SequenceEqual(new[] { "inspect_continue" }), "Tester stages a demo fixture without adding participant task controls");
                    Click("inspect_continue"); yield return Stable();
                }
                if (task == 0)
                {
                    Click("reference"); yield return Stable(); yield return ToolbarClick("reference_close");
                    Require(!ActiveCommands().Contains("main_restore"), "Reference close preserves expanded task state");
                    port.Action("demo.target.two", new { connected = false }, new { connected = true }, "SimulatedController", true);
                }
                else if (!technicalSetup)
                {
                    port.Action("demo.target.two", new { installed = false }, new { installed = true }, "SimulatedController", true);
                    controller.SetDemoPass(false); Click("inspect"); yield return Stable(); Click("inspect_continue");
                    port.Action("demo.target.one", new { connected = false }, new { connected = true }, "SimulatedController", true);
                }
                else port.Action("demo.target.one", new { connected = false }, new { connected = true }, "SimulatedController", true);
                if (!technicalSetup) controller.SetDemoPass(true);
                Click("inspect"); yield return Stable();
                Require(!controller.Flow.InspectPassed.HasValue && controller.Flow.State == (task == 0 ? ResearchFlowState.PostTask1Checkpoint : ResearchFlowState.PostTask2Checkpoint),
                    "Successful Inspect goes directly to the required checkpoint without a redundant result screen");
                Require(ActiveCommands().SequenceEqual(technicalSetup ? new[] { "technical_resume" } : Array.Empty<string>()),
                    "Successful Inspect retains researcher ownership and only the explicit technical checkpoint fixture");
                yield return Capture("inspect_pass_task_" + expected);
                Require(controller.Flow.State == (task == 0 ? ResearchFlowState.PostTask1Checkpoint : ResearchFlowState.PostTask2Checkpoint), "NASA checkpoint after task " + (task + 1));
                RequireUiOnlySafety("Checkpoint");
                if (technicalSetup) Require(ActiveCommands().SequenceEqual(new[] { "technical_resume" }), "Technical checkpoint offers only the tester-labelled continue; no dead-end wait");
                else Require(!ActiveCommands().Any(), "Checkpoint offers no participant skip");
                yield return Capture("08_nasa_checkpoint_" + (task + 1));
                if (technicalSetup) Click("technical_resume"); else controller.ResearcherResume();
                yield return Transition(task == 0 ? "arrival_task_" + controller.Flow.CurrentTask : null);
            }
            Require(controller.Flow.State == ResearchFlowState.ParticipantComplete && controller.Record.session_state == RegistryState.Completed, "Both tasks and both researcher checkpoints complete");
            yield return Capture("10_completion");
            VerifyLog();
            Require(controller.Logger.DataComplete, "Existing logger completed the test run");
            string summary = controller.SummaryText();
            Require(summary.Contains("TEST / DEMO DATA") && summary.Contains(controller.Record.participant_id), "Researcher summary identifies actual DEV data");
            Require(controller.ExportSession(), "Operator export requested: " + controller.ExportDeliveryError);
            for (float waited = 0; controller.ExportPending && waited < 60; waited += Time.unscaledDeltaTime) yield return null;
            Require(!controller.ExportPending && string.IsNullOrEmpty(controller.ExportDeliveryError), "Background export finished without error: " + controller.ExportDeliveryError);
            CheckExportBundle(controller.LastExportPath);
            if (technicalSetup) yield break; // No researcher-console selection exists in the headset-started path.
            researcherCanvas.enabled = true; SetDisplay(1); yield return Stable();
            Require(idDropdown.captionText != null && idDropdown.captionText.text.Contains(controller.Record.participant_id), "Completed researcher summary dropdown identifies the current session participant, not the prior selection");
            var summarySelectors = researcherCanvas.GetComponentsInChildren<TMP_Dropdown>(true);
            Require(summarySelectors.Length == 5 && summarySelectors.All(d => !d.interactable), "Completed researcher summary locks all five session selectors");
            var setupView = researcherCanvas.GetComponent<ResearcherSetupView>();
            foreach (string field in new[] { "start", "resume", "abort" })
            {
                var button = (Button)typeof(ResearcherSetupView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(setupView);
                Require(button != null && !button.gameObject.activeInHierarchy, "Ended researcher summary hides " + field + " action");
            }
            var exportButton = (Button)typeof(ResearcherSetupView).GetField("export", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(setupView);
            Require(exportButton != null && exportButton.gameObject.activeInHierarchy && exportButton.interactable, "Ended researcher summary offers Export as an available action");
            Require(researcherCanvas.GetComponentsInChildren<Button>(false).Where(b => b.isActiveAndEnabled).SequenceEqual(new[] { exportButton }), "Export is the only visible ended-session researcher button");
            Require(setupView.SummaryContentVisible(), "Researcher summary text renders inside its viewport");
            yield return Capture("researcher_summary");
            researcherCanvas.enabled = oldResearcherEnabled; SetDisplay(0);
        }

        private static IEnumerator ExerciseMinimizedModal()
        {
            controller.Log("DEMO_INPUT", "minimized.modal.regression", "ProgrammaticCommandFixture", source: "Tester");
            Require(!ActiveCommands().Contains("main_minimize") && !ActiveCommands().Contains("main_recenter"), "Primary slate has no pop-up utility buttons");
            // Explicit participant minimization remains independent from Reference and survives modal interruption.
            controller.view.LocalCommand("main_minimize", "SimulatedController"); yield return Stable();
            yield return Capture("task_" + controller.Flow.CurrentTask + "_minimized");
            controller.Command("inspect", "SimulatedController"); yield return Stable();
            Require(controller.Flow.InspectPassed == false, "Minimized task may enter neutral fail modal");
            Click("inspect_continue"); yield return Stable();
            Require(controller.view.mainPanel.GetComponent<RectTransform>().sizeDelta == controller.view.mainPanel.transform.Find("MinimizedTask").GetComponent<RectTransform>().sizeDelta && ActiveCommands().Contains("main_restore"), "Fail continue restores tiny task pill and its exact root hit bounds: size=" + controller.view.mainPanel.GetComponent<RectTransform>().sizeDelta + "; commands=" + string.Join(",",ActiveCommands()) + "; error=" + controller.Error);
            controller.Command("pause", "SimulatedController"); yield return Stable();
            controller.ResearcherResume(); yield return Stable();
            Require(controller.view.mainPanel.GetComponent<RectTransform>().sizeDelta == controller.view.mainPanel.transform.Find("MinimizedTask").GetComponent<RectTransform>().sizeDelta && ActiveCommands().Contains("main_restore"), "Safety resume restores tiny task pill and its exact root hit bounds");
            Click("main_restore"); yield return Stable();
        }

        private static IEnumerator ExerciseParticipantPause(string label)
        {
            var state = controller.Flow.State;
            int completed = controller.Flow.TutorialCompleted;
            string task = controller.Flow.CurrentTask;
            bool taskPoseOwned = controller.Flow.IsTaskActive;
            var taskPosition = controller.view.mainPanel.transform.position;
            var taskRotation = controller.view.mainPanel.transform.rotation;
            Click("pause"); yield return Stable();
            Require(controller.Flow.Paused && !controller.Flow.CanInteract && controller.ParticipantMayResume,
                "Technical participant pause stops interaction and permits Continue: " + label);
            RequireUiOnlySafety("Pause " + label);
            Require(ActiveCommands().SequenceEqual(new[] { "session_resume" }),
                "Eligible pause exposes Continue only: " + label);
            Require(!controller.view.mainPanel.GetComponentsInChildren<ResearchUIAction>(true)
                .Any(a => (a.command == "help_request" || a.command == "panel_recovery") && a.gameObject.activeInHierarchy),
                "Removed Pause utilities have no active ray targets: " + label);
            yield return Capture("pause_participant_" + label);
            Require(controller.Command("help_request", "SimulatedController"), "Existing Help command remains available: " + label);
            yield return Stable();
            Require(controller.HelpRequested && controller.Flow.Paused, "Help command preserves pause: " + label);
            Require(controller.view.mainPanel.GetComponentsInChildren<TMP_Text>(false)
                .Any(t => t.text.Contains(ResearchStrings.Get("pause.safety", requestedLanguage))),
                "Pause retains the instruction to tell the researcher: " + label);
            var pausePanel = controller.view.mainPanel;
            Require(pausePanel.IsReadableFromHead && pausePanel.PlacementClear(pausePanel.transform.position, pausePanel.transform.rotation),
                "One-button Pause remains in view and clear of equipment: " + label);
            yield return Capture("pause_help_requested_" + label);
            Click("session_resume"); yield return Stable();
            Require(!controller.Flow.Paused && !controller.HelpRequested && controller.Flow.State == state
                && controller.Flow.TutorialCompleted == completed && controller.Flow.CurrentTask == task,
                "Participant Continue retains the tutorial or task state: " + label);
            if (taskPoseOwned)
                Require(Vector3.Distance(taskPosition, controller.view.mainPanel.transform.position) < .001f
                    && Quaternion.Angle(taskRotation, controller.view.mainPanel.transform.rotation) < .01f,
                    "Pause and expanded Help preserve the participant's task panel pose: " + label);
        }

        private static void RequireUiOnlySafety(string label)
        {
            var gates = controller.guards.SelectMany(g => g.GetComponents<ResearchWorldInteractionGate>()).Distinct().ToArray();
            Require(!controller.Flow.CanInteract && gates.Length == controller.guards.Length && gates.All(g => !g.Allowed),
                label + " blocks every physical interaction gate");
            Require(controller.guards.All(g => !g.Suspended), label + " keeps UI input permission available subject to tracking safety");
            Require(controller.trainingObjects.All(item => !item.IsHeld && (!item.grabbable || !item.grabbable.enabled))
                && controller.taskPorts.All(port => !port.InteractionAllowed), label + " releases held practice objects and blocks practice/task ports");
            Require(controller.locomotionInputRoots.All(g => !g.activeInHierarchy), label + " disables locomotion roots");
        }

        private static IEnumerator ExerciseContextualExamine(string taskId)
        {
            var targets = controller.GetComponentsInChildren<ResearchExamineTarget>(true);
            var module = targets.Single(t => t.stableTarget == taskId + ".module");
            var reserved = targets.Where(t => t.stableTarget.EndsWith(".connection", StringComparison.Ordinal)
                || t.stableTarget.EndsWith(".mount", StringComparison.Ordinal)).ToArray();
            Require(module.Available && targets.Count(t => t.Available) == 1, "Only the current real assembly is available for examination");
            Require(reserved.Length == 4 && reserved.All(t => !t.enabled && !t.gameObject.activeInHierarchy && !t.Available
                && !t.stationCanvas && t.interactionRoots.Length == 0), "Connection and mount IDs remain reserved without live UI or interaction roots");
            var port = controller.taskPorts.Single(p => p.taskId == taskId);
            var emitted = new List<string>();
            Action<string, string, object, object> observe = (name, target, before, after) => { if (name == "EXAMINE") emitted.Add(target); };
            var focusMethod = typeof(ResearchExamineTarget).GetMethod("SetFocus", BindingFlags.Static | BindingFlags.NonPublic);
            var focusTime = typeof(ResearchExamineTarget).GetField("lastFocusTime", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(focusMethod != null && focusTime != null, "Explicit technical context-focus staging callbacks exist");
            EditorApplication.CallbackFunction stageFocus = () => { focusMethod.Invoke(null, new object[] { module }); focusTime.SetValue(module, Time.unscaledTime); };
            var camera = controller.origin.Camera;
            var oldPosition = camera.transform.localPosition; var oldRotation = camera.transform.localRotation;
            bool ownsFixture = false;
            PointerEventData pointer = null;
            port.Behavior += observe;
            try
            {
                if (!ResearchFlowVisualInput.Running) { ResearchFlowVisualInput.Begin(1.65f); ownsFixture = true; }
                yield return Stable();
                var pointerInteractor = controller.guards.Where(g => g && g.interactionsRoot)
                    .SelectMany(g => g.interactionsRoot.GetComponentsInChildren<Oculus.Interaction.RayInteractor>(true))
                    .FirstOrDefault(r => r.isActiveAndEnabled && controller.InputSourceForPointer(r.Identifier) == "SimulatedController");
                Require(pointerInteractor != null, "Owned simulated controller supplies an attributed SDK pointer; no physical-input claim");
                pointer = new PointerEventData(EventSystem.current) { pointerId = pointerInteractor.Identifier };
                controller.Log("DEMO_INPUT", module.stableTarget, "StagedContextFocusAndPointerCallbacks_NotPhysicalRaySelection", source: "Tester");
                EditorApplication.update += stageFocus; stageFocus();
                // Keep the owned staged hover occupied while Editor callbacks wait; an exited pointer correctly expires after .35s.
                module.OnPointerEnter(pointer);
                yield return Stable();
                Require(emitted.Count == 0, "Context focus and hover emit no examination event");
                Require(controller.ReferenceOpen && targets.Count(t => t.ChipVisible) == 1 && module.ChipVisible,
                    "Expanded Reference permits exactly one neutral component-local chip");
                Require(!ScreenBounds(controller.view.referencePanel.GetComponent<RectTransform>()).Overlaps(ScreenBounds((RectTransform)module.transform)),
                    "Reference and the real assembly Examine chip do not overlap in the participant view");
                Require(!ScreenBounds(controller.view.mainPanel.GetComponent<RectTransform>()).Overlaps(ScreenBounds((RectTransform)module.transform)),
                    "Task instruction and the real assembly Examine chip do not overlap in the participant view");
                yield return Capture("context_examine_reference_open_task_" + taskId + "_staged");
                stageFocus();
                Require(module.Available && module.ChipVisible && controller.InputSourceForPointer(pointer.pointerId) == "SimulatedController",
                    "Explicit staged callback has current focus and owned simulated input after capture");
                module.OnPointerDown(pointer); module.OnPointerUp(pointer); module.GetComponent<Button>().onClick.Invoke();
                Require(emitted.SequenceEqual(new[] { taskId + ".module" }), "Actual available chip callback emits one attributed assembly EXAMINE with Reference open");
                foreach (var target in reserved) { target.OnPointerDown(pointer); target.OnPointerUp(pointer); target.GetComponent<Button>().onClick.Invoke(); }
                Require(emitted.Count == 1, "Reserved connection/mount callbacks cannot emit events");
                yield return ToolbarClick("reference_close");
                Require(!controller.ReferenceOpen && targets.Count(t => t.ChipVisible) == 1, "Reference close leaves only one focused chip");
                Require(!ScreenBounds(controller.view.mainPanel.GetComponent<RectTransform>()).Overlaps(ScreenBounds((RectTransform)module.transform)),
                    "Main panel after Reference close and the real assembly Examine chip do not overlap in the participant view");
                yield return Capture("context_examine_task_" + taskId + "_staged");
                stageFocus();
                Require(module.Available && module.ChipVisible && controller.InputSourceForPointer(pointer.pointerId) == "SimulatedController",
                    "Second staged callback reacquires current focus after capture");
                module.OnPointerDown(pointer); module.OnPointerUp(pointer); module.GetComponent<Button>().onClick.Invoke();
                Require(emitted.SequenceEqual(new[] { taskId + ".module", taskId + ".module" }), "Second deliberate callback records the same real assembly without inventing subcomponent identity");
                ResearchFlowVisualInput.SetPose("workstation"); yield return Stable();
                var audit = JObject.FromObject(ResearchFlowVisualInput.Audit());
                Require(audit.Value<bool>("running") && audit.Value<bool>("no_extra_cameras") && audit.Value<bool>("no_extra_origins")
                    && audit.Value<bool>("no_extra_renderers"), "Hands-near-task fixture uses only the existing Meta rig and visuals");
                yield return Capture("hands_near_task_" + taskId + "_simulated");
                File.WriteAllText(Path.Combine(output, prefix + "_context_examine_" + taskId + ".json"), JsonConvert.SerializeObject(new {
                    task = taskId, technical_test = true, test_data = true, hardware_verified = false,
                    scope = "Focus is staged explicitly; actual chip pointer/button callbacks use an owned SimulatedController ID. Pixel captures are real GameView renders, not physical ray-acquisition evidence.",
                    available_target = module.stableTarget, unavailable_targets = reserved.Select(t => t.stableTarget).ToArray(),
                    chip_text = module.GetComponentInChildren<TMP_Text>(true).text,
                    emitted_targets = emitted, simulated_visuals = audit
                }, Formatting.Indented));
            }
            finally
            {
                EditorApplication.update -= stageFocus;
                port.Behavior -= observe;
                module.OnPointerExit(pointer);
                try { focusMethod?.Invoke(null, new object[] { null }); }
                finally
                {
                    if (ownsFixture) ResearchFlowVisualInput.Stop();
                    else if (ResearchFlowVisualInput.Running)
                    {
                        ResearchFlowVisualInput.SetPose("nearUi");
                        for (int side = 0; side < 2; side++)
                        {
                            var pose = ResearchFlowVisualInput.ControllerWorldPose(side);
                            ResearchFlowVisualInput.SetControllerWorldPose(side, new Pose(pose.position, Quaternion.LookRotation(Vector3.up, Vector3.forward)));
                        }
                    }
                    if (camera) { camera.transform.localPosition = oldPosition; camera.transform.localRotation = oldRotation; }
                }
            }
            yield return Stable();
            Require((!ownsFixture || !ResearchFlowVisualInput.Running) && targets.All(t => !t.ChipVisible), "Owned context fixture stops and idle examination labels disappear");
        }

        private static Rect ScreenBounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Select(p => controller.origin.Camera.WorldToScreenPoint(p)).ToArray();
            return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
        }

        static void CheckTutorialPrompt(string expected)
        {
            Require(controller.TutorialInstructionKey == expected, "Next tutorial action is " + expected);
            bool integrated = controller.ReferenceOpen;
            if (integrated && controller.ReferenceMinimized)
            {
                var pill = controller.view.referencePanel.transform.Find("MinimizedReference");
                Require(pill && pill.gameObject.activeInHierarchy && ActiveCommands().Contains("reference_restore"), "Minimized Tutorial reader has an accessible named restore control");
                Require(!controller.view.mainPanel.gameObject.activeInHierarchy, "Minimized Tutorial Reference does not introduce a detached instruction slate");
                return;
            }
            if (!controller.ReferenceOpen && Time.unscaledTime < (float)typeof(ResearchFlowView).GetField("successUntil", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller.view))
            {
                var slate = controller.view.mainPanel.transform.Find("Expanded");
                Require(slate.Find("TutorialSuccess").gameObject.activeInHierarchy && slate.Find("Body").gameObject.activeInHierarchy,
                    "Secondary confirmation keeps the current instruction visible: " + expected);
                Require(slate.Find("TutorialSuccess/Label").GetComponent<TMP_Text>().text.StartsWith(ResearchStrings.Get("tutorial.success", requestedLanguage)),
                    "Secondary feedback names the completed action");
            }
            var body = integrated ? controller.view.referencePanel.transform.Find("Expanded/TutorialCue").GetComponent<TMP_Text>()
                : controller.view.mainPanel.GetComponentsInChildren<TMP_Text>(false).Single(t => t.name == "Body");
            body.ForceMeshUpdate();
            string prompt = ResearchStrings.Get(expected, requestedLanguage);
            if (!integrated && controller.TutorialFact == "inspect") prompt += "\n\n" + ResearchStrings.Get("tutorial.examine_difference", requestedLanguage);
            Require(integrated ? Plain(body.text).Contains(prompt) : Plain(body.text) == prompt, "Visible current action matches the current localized instruction");
            Require(!body.isTextOverflowing && body.preferredHeight <= body.rectTransform.rect.height + 1, "Current action fits its text area");
            if (requestedLanguage == "Japanese")
                Require(body.textInfo.characterInfo.Take(body.textInfo.characterCount).Where(ch => ch.isVisible)
                    .GroupBy(ch => ch.lineNumber).All(line => line.Count(ch => char.IsLetterOrDigit(ch.character)) >= 2),
                    "Japanese coach and Reader instructions have no isolated final letter masked by punctuation");
            if (!integrated)
            {
                if (controller.TutorialFact == "inspect")
                {
                    // The current action retains its authored lines; the new neutral definition paragraph may wrap.
                    int actionLength = ResearchStrings.Get(expected, requestedLanguage).Length;
                    int actionLines = body.textInfo.characterInfo.Take(Mathf.Min(actionLength, body.textInfo.characterCount)).Where(ch => ch.isVisible).Select(ch => ch.lineNumber).Distinct().Count();
                    Require(actionLines == ResearchStrings.Get(expected, requestedLanguage).Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)),
                        "Current Inspect instruction keeps its authored lines above the neutral Examine/Inspect definition");
                }
                else Require(body.textInfo.lineCount <= 3 && body.textInfo.lineCount >= prompt.Split('\n').Length,
                    "Short current instruction stays within three readable lines: " + expected + " lines=" + body.textInfo.lineCount);
            }
            if (integrated)
            {
                Require(!controller.view.mainPanel.gameObject.activeInHierarchy, "Tutorial cue occupies the single Reference reader");
                Require(body.gameObject.activeInHierarchy && body.transform.IsChildOf(controller.view.referencePanel.transform), "Current Tutorial cue is actually visible inside Reference");
                var readerRoot = controller.view.referencePanel.transform;
                var cueBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(readerRoot, body.transform);
                foreach (var format in readerRoot.GetComponentsInChildren<ResearchUIAction>(false).Where(a => a.command.StartsWith("reference_tab.")))
                {
                    var formatBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(readerRoot, format.transform);
                    Require(cueBounds.min.y - formatBounds.max.y >= ResearchUITheme.SpaceL - 1,
                        "Integrated current action stays clear above the format row");
                }
                var readerDemo = readerRoot.Find("Expanded/TutorialDemo");
                if (readerDemo && readerDemo.gameObject.activeInHierarchy)
                    Require(cueBounds.min.x - RectTransformUtility.CalculateRelativeRectTransformBounds(readerRoot, readerDemo).max.x >= ResearchUITheme.SpaceL - 1,
                        "Integrated control demonstration stays beside the current action without overlap");
                return;
            }
            var root = controller.view.mainPanel.transform;
            Bounds Box(Transform t) => RectTransformUtility.CalculateRelativeRectTransformBounds(root, t);
            var bodyBounds = Box(body.transform);
            var progress = root.Find("Expanded/TutorialProgress");
            var success = root.Find("Expanded/TutorialSuccess");
            if (progress.gameObject.activeInHierarchy)
            {
                // Progress is quiet header context; the current instruction occupies the diagram band below it.
                Require(Box(progress).min.y - bodyBounds.max.y >= ResearchUITheme.SpaceL - 1,
                    "Labeled progress retains the compact 16-unit gap above the current action");
                Require(RenderedGlyphBounds(progress.GetComponent<TMP_Text>(), root).min.y - RenderedGlyphBounds(body, root).max.y >= ResearchUITheme.SpaceM - 1,
                    "Rendered progress glyphs stay separated from the current instruction glyphs");
            }
            if (success.gameObject.activeInHierarchy)
                Require(bodyBounds.min.y - Box(success).max.y >= ResearchUITheme.SectionGap - 1, "Visible success feedback stays clear below the action");
            var demo = root.Find("Expanded/TutorialDemo");
            if (demo.gameObject.activeInHierarchy)
            {
                Require(Box(demo).max.x + ResearchUITheme.SpaceL - 1 <= bodyBounds.min.x,
                    "Control demonstration sits beside the instruction without overlap");
            }
            foreach (var button in root.Find("Expanded/Actions").GetComponentsInChildren<UnityEngine.UI.Button>(false))
            {
                var contentBottom = success.gameObject.activeInHierarchy ? Box(success).min.y : bodyBounds.min.y;
                Require(contentBottom - Box(button.transform).max.y >= ResearchUITheme.ActionGap - 1, "Tutorial action button stays clear below visible content");
            }
        }

        static string Plain(string richText) => System.Text.RegularExpressions.Regex.Replace(richText, "<[^>]+>", "");

        static string Sha256(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        }

        static void CheckExportBundle(string exported)
        {
            byte[] manifestBytes = File.ReadAllBytes(Path.Combine(exported, "manifest.json"));
            var manifest = JObject.Parse(System.Text.Encoding.UTF8.GetString(manifestBytes));
            var complete = JObject.Parse(File.ReadAllText(Path.Combine(exported, "COMPLETE.json")));
            Require((string)manifest["schema_version"] == "researchflow.device-bundle.1"
                && (string)complete["schema_version"] == "researchflow.bundle-complete.1"
                && (string)manifest["bundle_id"] == Path.GetFileName(exported)
                && (string)complete["bundle_id"] == (string)manifest["bundle_id"]
                && (string)complete["manifest_sha256"] == Sha256(manifestBytes),
                "Immutable export completion marker matches the reopened exact manifest bytes");
            var identity = manifest["identity"];
            Require((string)identity["participant_code"] == controller.Record.participant_id
                && (string)identity["session_id"] == controller.Record.session_id
                && (string)identity["run_id"] == controller.Record.run_id && controller.Record.run_id == controller.Logger.RunId
                && (string)identity["group"] == controller.Record.group.ToString() && (bool)identity["test_data"],
                "Export manifest identifies the actual completed technical participant/session/run");
            Require((string)manifest["lifecycle"] == "finalized" && (string)manifest["delivery_status"] == "queued_for_host",
                "Finalized local snapshot remains queued without claiming PC delivery");
            var files = (JObject)manifest["files"];
            var expected = new[] { "raw/events.jsonl", "raw/movement.csv", "raw/session_metadata.json", "raw/research-session.json" };
            Require(files.Properties().Select(property => property.Name).OrderBy(path => path).SequenceEqual(expected.OrderBy(path => path)),
                "Manifest declares the exact four raw files in the current immutable bundle schema");
            foreach (string relative in expected)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(exported, relative.Replace('/', Path.DirectorySeparatorChar)));
                Require(bytes.LongLength == (long)files[relative]["bytes"] && Sha256(bytes) == (string)files[relative]["sha256"],
                    "Reopened raw file matches manifest length and SHA256: " + relative);
            }
            string eventPath = Path.Combine(exported, "raw", "events.jsonl");
            Require(File.ReadAllBytes(controller.Logger.EventsPath).SequenceEqual(File.ReadAllBytes(eventPath)), "Export preserves exact existing JSONL bytes");
            Require(File.ReadAllLines(eventPath).Where(line => !string.IsNullOrWhiteSpace(line)).All(line =>
                (string)JObject.Parse(line)["run_id"] == controller.Logger.RunId), "Export raw events preserve the actual run identity");
            var record = JObject.Parse(File.ReadAllText(Path.Combine(exported, "raw", "research-session.json")));
            var session = JObject.Parse(File.ReadAllText(Path.Combine(exported, "raw", "session_metadata.json")));
            Require((string)record["participant_id"] == controller.Record.participant_id
                && (string)record["session_id"] == controller.Record.session_id && (string)record["run_id"] == controller.Logger.RunId
                && (string)session["run_id"] == controller.Logger.RunId && (bool)record["test_data"] && (bool)session["test_data"],
                "Reopened session record and logger metadata agree with the exported actual test run");
        }

        static Bounds RenderedGlyphBounds(TMP_Text text, Transform relativeRoot)
        {
            text.ForceMeshUpdate(true);
            var glyphs = text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(ch => ch.isVisible).ToArray();
            Require(glyphs.Length > 0, "Visible instruction/progress has actual rendered glyphs: " + text.name);
            var bounds = new Bounds(relativeRoot.InverseTransformPoint(text.transform.TransformPoint(glyphs[0].bottomLeft)), Vector3.zero);
            foreach (var glyph in glyphs)
            {
                bounds.Encapsulate(relativeRoot.InverseTransformPoint(text.transform.TransformPoint(glyph.bottomLeft)));
                bounds.Encapsulate(relativeRoot.InverseTransformPoint(text.transform.TransformPoint(glyph.topRight)));
            }
            return bounds;
        }

        private static IEnumerator ExerciseReference(string label)
        {
            yield return Capture("reference_initial_content_" + label);
            Click("reference_tab.user_manual"); yield return Stable();
            CheckReferenceLocomotionGate(true);
            yield return ExerciseReferenceNavigation(label);
            var panel = controller.view.referencePanel;
            var handle = panel.GetComponentInChildren<ResearchDragHandle>();
            if (controller.Flow.IsTutorial)
            {
                Require(!controller.view.mainPanel.gameObject.activeInHierarchy, "Tutorial Reference uses one reader with no detached coach");
                var detail = panel.transform.Find("Expanded/TutorialCue").GetComponent<TMP_Text>();
                Require(detail.gameObject.activeInHierarchy && Plain(detail.text).Contains(ResearchStrings.Get(controller.TutorialInstructionKey, requestedLanguage)), "Integrated tutorial names the current localized action");
                Require(detail.rectTransform.rect.width <= ((RectTransform)detail.transform.parent.parent).rect.width,
                    "Integrated Reference action fits inside its reader");
                yield return Capture("reference_integrated_tutorial");
            }
            var screen = ScreenBounds(panel.GetComponent<RectTransform>());
            var viewport = controller.origin.Camera.pixelRect;
            Require(viewport.Contains(screen.min) && viewport.Contains(screen.max), "Expanded Reference fits inside the GameView viewport");
            Require(handle != null && handle.panel == panel, "Only explicit reference bottom handle owns panel drag");
            var raycaster = panel.GetComponent<GraphicRaycaster>();
            var canvas = panel.GetComponent<Canvas>();
            var oldCamera = canvas.worldCamera;
            var camera = controller.origin.Camera;
            canvas.worldCamera = camera;
            var before = panel.transform.position;
            var pointer = new PointerEventData(EventSystem.current) { pointerId = -73022, position = camera.WorldToScreenPoint(handle.transform.position), pointerPressRaycast = new RaycastResult { module = raycaster }, button = PointerEventData.InputButton.Left };
            controller.Log("DEMO_INPUT", "reference.drag", "ProgrammaticPointerFixture", source: "Tester");
            handle.OnBeginDrag(pointer);
            // Move outward: the old inward target intersects the Task B fan and is correctly refused.
            pointer.position = camera.WorldToScreenPoint(handle.transform.position - camera.transform.right * .04f);
            handle.OnDrag(pointer); handle.OnEndDrag(pointer);
            canvas.worldCamera = oldCamera;
            Require(Vector3.Distance(before, panel.transform.position) > .03f, "Separated bottom handle moves the reference panel");
            Require(Vector3.Dot(panel.transform.up, Vector3.up) > .99f, "Dragged panel stays upright");
            var distance = Vector3.Distance(panel.transform.position, controller.view.Head.position);
            Require(distance >= .69f && distance <= 1.81f, "Dragged panel stays within distance limits");
            Require(panel.transform.position.y >= controller.view.Head.position.y - .651f && panel.transform.position.y <= controller.view.Head.position.y + .451f, "Dragged panel stays in readable vertical range");
            yield return Stable(); yield return Capture("repositioned_" + label);
            var moved = panel.transform.position; var movedRotation = panel.transform.rotation;
            ResearchFlowVisualInput.SetControllerWorldPose(1, ResearchFlowVisualInput.ControllerWorldPose(1), thumbstick: new Vector2(0,.8f));
            yield return Stable();
            Time.timeScale = 0;
            yield return ToolbarClick("reference_minimize", true);
            Require(controller.ReferenceMinimized && controller.ReferenceOpen, "Minimized reference stays available");
            if (controller.Flow.IsTutorial) CheckTutorialPrompt("tutorial.try.restore");
            CheckReferenceLocomotionGate(false);
            Require(Vector3.Distance(moved, panel.transform.position) < .001f, "Minimize preserves panel position");
            if (controller.Flow.IsTaskActive) Require(!ScreenBounds(panel.GetComponent<RectTransform>()).Overlaps(ScreenBounds(controller.view.mainPanel.GetComponent<RectTransform>())), "Default task brief and minimized Reference have separate readable space");
            yield return Capture("minimized_" + label);
            Click("reference_restore"); yield return Stable();
            if (controller.Flow.IsTutorial)
            {
                CheckTutorialPrompt("tutorial.try.close");
                yield return ToolbarClick("reference_minimize", true);
                CheckTutorialPrompt("tutorial.try.restore");
                Require(ActiveCommands().Contains("reference_restore"), "Repeated minimize instruction points to a visible Restore action");
                yield return Capture("tutorial_reference_minimized_again");
                Click("reference_restore"); yield return Stable();
                CheckTutorialPrompt("tutorial.try.close");
            }
            Require(!controller.ReferenceMinimized && controller.ReferenceSection == "user_manual", "Restore preserves active information format");
            CheckReferenceLocomotionGate(true);
            bool expandedPoseFits = (bool)typeof(ResearchPanelMotion).GetMethod("FitsView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, new object[] { moved, movedRotation });
            Require(expandedPoseFits ? Vector3.Distance(moved, panel.transform.position) < .001f && Quaternion.Angle(movedRotation, panel.transform.rotation) < .01f : panel.IsReadableFromHead && !panel.PlacementBlocked,
                "Restore preserves a fitting expanded pose or recovers out-of-view expanded bounds; priorFits=" + expandedPoseFits + "; recoveryDistance=" + Vector3.Distance(moved, panel.transform.position));
            Require(!ActiveCommands().Contains("reference_recenter"), "Reference has no retired participant Recenter button");
            ResearchFlowVisualInput.SetControllerWorldPose(1, ResearchFlowVisualInput.ControllerWorldPose(1), thumbstick: new Vector2(0,.8f));
            yield return Stable();
            yield return ToolbarClick("reference_close");
            yield return ObserveUnscaledAnimation(.4, "Reference close at timescale zero");
            var referenceGroup = panel.GetComponent<CanvasGroup>();
            var mainGroup = controller.view.mainPanel.GetComponent<CanvasGroup>();
            Require(!controller.ReferenceOpen && !panel.gameObject.activeSelf, "Reference close reaches hidden end state while timescale zero");
            Require(Mathf.Abs(referenceGroup.alpha - 1) < .001f && Mathf.Abs(mainGroup.alpha - 1) < .001f, "Closed reference resets alpha and main panel is fully visible");
            CheckReferenceLocomotionGate(false);
            if (controller.Flow.IsTaskActive) Require(!ActiveCommands().Contains("main_restore"), "Reference close preserves expanded task state");
            if (controller.Flow.IsTutorial)
            {
                Require(!ActiveCommands().Contains("reference") && ActiveCommands().Contains("inspect"), "Completed Reference practice exposes only current Inspect action");
                controller.Log("DEMO_INPUT", "reference.reopen", "ProgrammaticAnimationRegressionFixture", source: "Tester");
                controller.Command("reference", "SimulatedController");
            }
            else Click("reference");
            yield return ObserveUnscaledAnimation(.4, "Reference open at timescale zero");
            CheckReferenceLocomotionGate(true);
            Require(controller.ReferenceOpen && panel.gameObject.activeInHierarchy && referenceGroup.interactable && referenceGroup.blocksRaycasts, "Reference open reaches interactive end state while timescale zero");
            Require(Mathf.Abs(referenceGroup.alpha - 1) < .001f && Mathf.Abs(mainGroup.alpha - 1) < .001f,
                "Opened Reference preserves task visibility or the integrated Tutorial reader");
            Require(Vector3.Distance(panel.transform.localScale, Vector3.one * .001f) < .000001f, "Reference animation finishes at exact positive standard scale");
            Require(panel.PlacementClear(panel.transform.position, panel.transform.rotation), "Reopened Reference has a clear system placement while timescale zero");
            Time.timeScale = originalTimeScale;
        }

        private static void CheckReferenceLocomotionGate(bool expanded)
        {
            Require(controller.locomotionInputRoots.Length > 0, "Existing locomotion roots available for Reference input gate verification");
            if (expanded)
            {
                Require(controller.ReferenceOpen && !controller.ReferenceMinimized, "Reference is expanded during navigation gate check");
                Require(controller.locomotionInputRoots.All(root => !root.activeInHierarchy), "Expanded Reference blocks locomotion roots while ray navigation owns input");
                foreach (var root in controller.locomotionInputRoots)
                {
                    bool nativeActive = root.activeSelf;
                    try
                    {
                        root.SetActive(true); // Reproduce Meta's ray-hover gate reopening its native root.
                        Require(!root.activeInHierarchy, "Native root activation cannot bypass the Reference navigation gate");
                    }
                    finally { root.SetActive(nativeActive); }
                }
            }
            else
            {
                Require(controller.locomotionInputRoots.All(root => !root.activeInHierarchy), "Closed or minimized Reference retains locomotion gate before explicit input recovery fixture");
                controller.Log("DEMO_INPUT", "reference.navigation_gate", "UnavailableInputZeroAxisFixture", source: "Tester");
                controller.UpdateReferenceNavigation(false, Vector2.zero);
                Require(controller.locomotionInputRoots.All(root => !root.activeInHierarchy), "Missing input with zero axis must not be mistaken for a neutral tracked stick");
                controller.Log("DEMO_INPUT", "reference.navigation_gate", "AvailableInputHeldAxisFixture", source: "Tester");
                controller.UpdateReferenceNavigation(true, new Vector2(0, .8f));
                Require(controller.locomotionInputRoots.All(root => !root.activeInHierarchy), "Recovered input with held stick retains the Reference locomotion gate");
                controller.Log("DEMO_INPUT", "reference.navigation_gate", "AvailableInputNeutralAxisFixture", source: "Tester");
                ResearchFlowVisualInput.SetControllerWorldPose(1, ResearchFlowVisualInput.ControllerWorldPose(1));
                controller.UpdateReferenceNavigation(true, Vector2.zero);
                bool tutorialEligible = controller.Flow.CanInteract;
                Require(controller.locomotionInputRoots.All(root => root.transform.parent.gameObject.activeSelf == tutorialEligible), "Closed or minimized Reference restores locomotion only in legitimate interactive phases");
            }
        }

        private static IEnumerator ExerciseReferenceNavigation(string label)
        {
            var panel = controller.view.referencePanel;
            var scroll = panel.GetComponentInChildren<ScrollRect>();
            var focus = panel.GetComponent<ResearchSlateFocus>();
            Require(scroll != null && focus != null, "Reference reuses ScrollRect with slate focus");
            var tabs = panel.GetComponentsInChildren<RectTransform>(false).Where(rect => rect.name.StartsWith("Tab_")).ToArray();
            var availableFormats = ResearchReferenceScheme.Active.Where(controller.ReferenceAvailable).ToArray();
            Require(ResearchReferenceScheme.Active.SequenceEqual(new[] { "user_manual", "step_by_step", "illustrated", "video" })
                && tabs.Length == ResearchReferenceScheme.Active.Length, "Reference preserves all four study identities in this fixture");
            foreach (var unavailable in tabs.Where(tab => !controller.ReferenceAvailable(tab.GetComponent<ResearchUIAction>().command.Substring(14))))
                Require(!unavailable.GetComponent<Button>().interactable
                    && unavailable.GetComponentInChildren<TMP_Text>().text.Contains(ResearchStrings.Get("reference.unavailable", requestedLanguage)),
                    "Unavailable format is explicitly labelled and has no active selection: " + unavailable.name);
            foreach (string format in availableFormats)
            {
                Click("reference_tab." + format); yield return Stable();
                Require(controller.ReferenceSection == format, "Reference switches to information format " + format);
                var tab = tabs.Single(t => t.GetComponent<ResearchUIAction>().command == "reference_tab." + format);
                Require(tab.GetComponentInChildren<TMP_Text>().text == ResearchStrings.Get("reference." + format, requestedLanguage),
                    "Reference format label is localized: " + format);
                bool visualTutorial = controller.Flow.IsTutorial && format == "illustrated";
                var drawing = panel.transform.Find("Expanded/IllustratedInstruction");
                Require(drawing != null && drawing.gameObject.activeSelf == visualTutorial, "Tutorial diagram is isolated from experimental content: " + format);
                if (visualTutorial)
                {
                    Require(drawing.Cast<Transform>().Count(t => t.name.StartsWith("Frame_")) == 4 && !scroll.gameObject.activeSelf, "Illustrated Instruction contains four visual panels");
                    foreach (var caption in drawing.GetComponentsInChildren<TMP_Text>(false).Where(t => t.name == "Caption"))
                    {
                        caption.ForceMeshUpdate(); Require(!caption.isTextOverflowing && caption.preferredHeight <= caption.rectTransform.rect.height + 1, "Illustrated caption fits: " + caption.text);
                    }
                }
                else
                {
                    var content = scroll.viewport.GetComponentsInChildren<TMP_Text>(false).Single(t => t.name == "ContentText");
                    Require(!string.IsNullOrWhiteSpace(content.text), "Technical Reference format content: " + format);
                }
                if (scroll.content.rect.height <= scroll.viewport.rect.height + 1)
                {
                    Require(!ActiveCommands().Contains("reference_previous") && !ActiveCommands().Contains("reference_next")
                        && !panel.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "PagePosition").gameObject.activeSelf,
                        "Single-page format hides Previous, Next and page count: " + format);
                }
                if (label == "tutorial")
                {
                    CheckTutorialPrompt(controller.TutorialInstructionKey);
                    // Keep the synthetic off-hand clear of captions in evidence images.
                    var offHand = ResearchFlowVisualInput.ControllerWorldPose(0);
                    ResearchFlowVisualInput.SetControllerWorldPose(0, new Pose(offHand.position + Vector3.down * .6f, offHand.rotation));
                    yield return Stable(); yield return Capture("reference_format_" + format);
                    ResearchFlowVisualInput.SetControllerWorldPose(0, offHand); yield return Stable();
                }
            }
            Click("reference_tab.user_manual"); yield return Stable();
            var tabCorners = new Vector3[4]; var viewportCorners = new Vector3[4];
            scroll.viewport.GetWorldCorners(viewportCorners);
            foreach (var tab in tabs)
            {
                tab.GetWorldCorners(tabCorners);
                float gapMillimeters = Vector3.Dot((tabCorners[0] + tabCorners[3] - viewportCorners[1] - viewportCorners[2]) * .5f, panel.transform.up) * 1000;
                Require(gapMillimeters >= 12, "Reference content stays at least 12 mm below format tabs " + tab.name);
            }
            Require(scroll.verticalScrollbar == null || !scroll.verticalScrollbar.IsInteractable(), "Narrow scrollbar is a passive indicator, never required input");
            if (scroll.content.rect.height <= scroll.viewport.rect.height + 1)
            {
                Require(!ActiveCommands().Contains("reference_previous") && !ActiveCommands().Contains("reference_next")
                    && !panel.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "PagePosition").gameObject.activeSelf,
                    "Short reference content has no Previous, Next or page count");
                yield return Capture("reference_short_content_" + label);
                yield break;
            }
            yield return Capture("reference_long_content_" + label);
            foreach (var key in new[] { "PagePosition", "PreviousPage/Label", "NextPage/Label" })
            {
                var caption = panel.transform.Find("Expanded/" + key).GetComponent<TMP_Text>(); caption.ForceMeshUpdate();
                Require(caption.textInfo.lineCount == 1 && !caption.isTextOverflowing && caption.preferredWidth <= caption.rectTransform.rect.width + 1,
                    "Localized Reference page control fits one readable line: " + key);
            }
            scroll.verticalNormalizedPosition = 1; yield return Stable();
            Click("reference_next"); yield return Stable();
            Require(scroll.verticalNormalizedPosition < .99f, "Next page navigates reference without touching scrollbar");
            var pageText = scroll.viewport.GetComponentsInChildren<TMP_Text>().First(t => t.name == "ContentText");
            Require(pageText.overflowMode == TextOverflowModes.Page && pageText.pageToDisplay == 2 && pageText.textInfo.pageCount >= 2,
                "Next renders a distinct TMP page instead of a nearly identical scrolled viewport");
            Require(panel.transform.Find("Expanded/PagePosition").GetComponent<TMP_Text>().text == string.Format(ResearchStrings.Get("reference.page_position", requestedLanguage), 2, pageText.textInfo.pageCount),
                "Reference progress explicitly identifies the current page");
            // TMP pageInfo.lastCharacterIndex retains overflow lookahead; use assigned character pages.
            var characters = pageText.textInfo.characterInfo.Take(pageText.textInfo.characterCount).ToArray();
            Require(characters.Any(c => c.pageNumber == 1) && characters.Where(c => c.pageNumber == 1).Min(c => c.index) > characters.Where(c => c.pageNumber == 0).Max(c => c.index),
                "Reference pages contain distinct, ordered character ranges");
            yield return Capture("reference_page_2_" + label);
            Click("reference_previous"); yield return Stable();
            Require(scroll.verticalNormalizedPosition > .99f, "Previous page returns to reference beginning");
            var controllerPoses = new[] { ResearchFlowVisualInput.ControllerWorldPose(0), ResearchFlowVisualInput.ControllerWorldPose(1) };
            for (int side = 0; side < 2; side++)
                ResearchFlowVisualInput.SetControllerWorldPose(side, new Pose(controllerPoses[side].position, Quaternion.LookRotation(-controller.origin.Camera.transform.forward)));
            yield return Stable();
            Require(!focus.Focused, "Both native rays leave the slate before the unfocused scroll check");
            float top = scroll.verticalNormalizedPosition;
            focus.Scroll(-1, .25f);
            Require(Mathf.Abs(scroll.verticalNormalizedPosition - top) < .001f, "Thumbstick scroll consumer ignores unfocused slate");
            var pointer = new PointerEventData(EventSystem.current) { pointerId = -73026, button = PointerEventData.InputButton.Left };
            controller.Log("DEMO_INPUT", "reference.navigation", "ProgrammaticContentDragAndScrollFixture", source: "Tester");
            focus.OnPointerEnter(pointer); yield return Stable();
            focus.Scroll(.1f, .25f);
            Require(Mathf.Abs(scroll.verticalNormalizedPosition - top) < .001f, "Focused thumbstick scroll retains deadzone");
            focus.Scroll(-1, .25f);
            Require(scroll.verticalNormalizedPosition < top, "Focused thumbstick axis consumer scrolls content");
            scroll.verticalNormalizedPosition = 1;
            var canvas = panel.GetComponent<Canvas>(); var oldCamera = canvas.worldCamera;
            var camera = controller.origin.Camera;
            try
            {
                canvas.worldCamera = camera;
                pointer.position = camera.WorldToScreenPoint(scroll.viewport.position);
                pointer.pointerPressRaycast = new RaycastResult { module = panel.GetComponent<GraphicRaycaster>(), gameObject = scroll.gameObject };
                scroll.OnInitializePotentialDrag(pointer); scroll.OnBeginDrag(pointer);
                pointer.position += Vector2.up * 120;
                scroll.OnDrag(pointer); scroll.OnEndDrag(pointer);
                Require(scroll.verticalNormalizedPosition < .99f, "Content-surface drag scrolls without narrow scrollbar");
                yield return Stable(); yield return Capture("reference_content_drag_" + label);
            }
            finally
            {
                canvas.worldCamera = oldCamera;
                focus.OnPointerExit(pointer);
                scroll.StopMovement(); scroll.verticalNormalizedPosition = 1;
                for (int side = 0; side < 2; side++) ResearchFlowVisualInput.SetControllerWorldPose(side, controllerPoses[side]);
            }
        }

        // Hover/press feedback is colour only: button text must never rescale (sub-pixel text shimmer in the headset).
        private static IEnumerator ExerciseButtonFeedback()
        {
            var action = controller.view.GetComponentsInChildren<ResearchUIAction>(false).Single(a => a.command == "tutorial_start");
            var button = action.GetComponent<Button>();
            var pointer = new PointerEventData(EventSystem.current) { pointerId = -73023 };
            var state = controller.Flow.State;
            Require(button.transition == Selectable.Transition.ColorTint && button.colors.highlightedColor != button.colors.normalColor
                && button.colors.pressedColor != button.colors.normalColor, "Primary button has colour hover/press feedback");
            Time.timeScale = 0;
            try
            {
                foreach (var enter in new[] { true, false })
                {
                    if (enter) { button.OnPointerEnter(pointer); button.OnPointerDown(pointer); } else { button.OnPointerUp(pointer); button.OnPointerExit(pointer); }
                    yield return ObserveUnscaledAnimation(.2, "Button " + (enter ? "press" : "normal") + " at timescale zero");
                    Require(Vector3.Distance(action.transform.localScale, Vector3.one) < .0001f, "Button scale stays one while " + (enter ? "pressed" : "normal"));
                }
            }
            finally { button.OnPointerUp(pointer); button.OnPointerExit(pointer); Time.timeScale = originalTimeScale; }
            Require(controller.Flow.State == state, "Feedback callbacks do not advance tutorial or trigger button action");
        }

        // Editor timing only: main-thread Canvas update/batch cost, not a Quest measurement.
        private static IEnumerator MeasureCanvasCost(string label, int frames)
        {
            var markers = new[] { "Canvas.SendWillRenderCanvases", "Canvas.BuildBatch", "Canvas.RenderOverlays" };
            var recorders = markers.Select(m => Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Gui, m, frames)).ToArray();
            int start = Time.frameCount;
            try
            {
                while (Time.frameCount < start + frames) { gameView.Repaint(); yield return null; }
                canvasCost.Add(new
                {
                    label, frames, hardware_verified = false,
                    markers = markers.Select((m, i) => new
                    {
                        marker = m, valid = recorders[i].Valid, samples = recorders[i].Count,
                        mean_ms = recorders[i].Count == 0 ? 0 : Enumerable.Range(0, recorders[i].Count).Average(k => recorders[i].GetSample(k).Value) / 1e6,
                        max_ms = recorders[i].Count == 0 ? 0 : Enumerable.Range(0, recorders[i].Count).Max(k => recorders[i].GetSample(k).Value) / 1e6
                    }).ToArray()
                });
            }
            finally { foreach (var recorder in recorders) recorder.Dispose(); }
        }

        private static IEnumerator ObserveUnscaledAnimation(double seconds, string description)
        {
            double start = EditorApplication.timeSinceStartup; int frame = Time.frameCount;
            while (EditorApplication.timeSinceStartup - start < seconds || Time.frameCount < frame + 2)
            {
                if (EditorApplication.timeSinceStartup - start > 3) throw new TimeoutException("Animation frames stopped: " + description);
                CheckAnimationGeometry(); gameView.Repaint(); yield return null;
            }
            CheckAnimationGeometry(); Require(Time.timeScale == 0, description + " completed using unscaled time with finite bounded geometry");
        }

        private static void CheckAnimationGeometry()
        {
            foreach (var transform in controller.view.GetComponentsInChildren<Transform>(false))
            {
                var scale = transform.localScale;
                if (!Finite(scale.x) || !Finite(scale.y) || !Finite(scale.z) || scale.x <= 0 || scale.y <= 0 || scale.z <= 0)
                    throw new InvalidOperationException("Invalid animation scale: " + transform.name);
            }
            foreach (var group in controller.view.GetComponentsInChildren<CanvasGroup>(false))
                if (!Finite(group.alpha) || group.alpha < -.001f || group.alpha > 1.001f) throw new InvalidOperationException("Invalid animated alpha: " + group.name);
            // Motion is opacity-only: panel roots and buttons never rescale.
            foreach (var panel in new[] { controller.view.mainPanel, controller.view.referencePanel })
                if (panel.gameObject.activeInHierarchy && Mathf.Abs(panel.transform.localScale.x - .001f) > 1e-7f)
                    throw new InvalidOperationException("Panel animation changed scale: " + panel.name);
            foreach (var button in controller.view.GetComponentsInChildren<Button>(false))
                if (Mathf.Abs(button.transform.localScale.x - 1) > 1e-5f)
                    throw new InvalidOperationException("Button feedback changed scale: " + button.name);
            foreach (var line in controller.origin.GetComponentsInChildren<LineRenderer>(false))
                if (!Finite(line.widthMultiplier) || line.widthMultiplier < 0 || !Finite(line.startWidth) || line.startWidth < 0 || !Finite(line.endWidth) || line.endWidth < 0)
                    throw new InvalidOperationException("Invalid ray line width: " + line.name);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static IEnumerator ToolbarClick(string command, bool capture = false)
        {
            var action = controller.view.GetComponentsInChildren<ResearchUIAction>(false).FirstOrDefault(a => a.command == command);
            Require(action != null, "Toolbar command exists: " + command);
            var focus = action.GetComponentInParent<ResearchSlateFocus>();
            Require(focus != null, "Toolbar belongs to a focusable spatial slate: " + command);
            var pointer = new PointerEventData(EventSystem.current) { pointerId = -73025 };
            controller.Log("DEMO_INPUT", "slate.focus", "ProgrammaticPointerFixture", source: "Tester");
            focus.OnPointerExit(pointer); yield return Stable();
            var headerGroup = action.GetComponent<CanvasGroup>();
            Require(action.transform.parent == controller.view.referencePanel.transform.Find("Expanded")
                && action.GetComponent<Button>().IsInteractable()
                && (!headerGroup || (headerGroup.alpha == 1 && headerGroup.interactable && headerGroup.blocksRaycasts)),
                "Reference header control is visible and interactive without hover: " + command);
            if (capture) yield return Capture("reference_header_" + command + "_" + controller.Flow.CurrentTask);
            Click(command); yield return Stable();
            focus.OnPointerExit(pointer);
        }

        private static void Click(string command)
        {
            Require(string.IsNullOrEmpty(controller.Error), "No runtime error before " + command);
            var action = controller.view.GetComponentsInChildren<ResearchUIAction>(false).FirstOrDefault(a => a.command == command && a.GetComponent<Button>().IsInteractable());
            Require(action != null, "Active real Button available: " + command);
            var before = controller.view.mainPanel.transform.position;
            var beforeState = controller.Flow.State;
            var beforeInspect = controller.Flow.InspectPassed; var beforePaused = controller.Flow.Paused;
            var expandsTutorial = command == "reference_close" && controller.Flow.IsTutorial && controller.ReferenceOpen;
            var pointer = StagedOwnedPointer();
            controller.Log("DEMO_INPUT", command, "AcquiredThenReleasedProgrammaticButtonCallbacks_NotPhysicalRaySelection", source: "Tester");
            action.OnPointerDown(pointer); action.OnPointerUp(pointer); action.GetComponent<Button>().onClick.Invoke();
            // Help may recover newly enlarged Pause bounds; it must never start a participant drag.
            if (!expandsTutorial && command != "reference" && !command.EndsWith("recenter") && beforeState == controller.Flow.State && beforeInspect == controller.Flow.InspectPassed && beforePaused == controller.Flow.Paused)
                Require(command == "help_request" ? !controller.view.mainPanel.IsDragging
                    : Vector3.Distance(before, controller.view.mainPanel.transform.position) < .001f,
                    "Button does not drag task panel: " + command);
        }

        private static string[] ActiveCommands() => controller.view.GetComponentsInChildren<ResearchUIAction>(false).Where(a => a.GetComponent<Button>().IsInteractable()).Select(a => a.command).Where(c => !string.IsNullOrEmpty(c)).ToArray();

        private static void VerifyExperimentalUI()
        {
            Require(controller.Flow.IsTaskActive, "Experimental task active");
            var taskBody = controller.view.mainPanel.GetComponentsInChildren<TMP_Text>(false).Single(t => t.text == ResearchStrings.Get("task." + controller.Flow.CurrentTask + ".body", requestedLanguage));
            taskBody.ForceMeshUpdate();
            Require(taskBody.preferredHeight <= taskBody.rectTransform.rect.height, "Localized task copy fits its body area above action buttons");
            foreach (var utility in controller.view.mainPanel.GetComponentsInChildren<ResearchUIAction>(false).Where(a => a.command == "main_minimize" || a.command == "main_recenter"))
            {
                Require(utility.GetComponentInChildren<ResearchUtilityIcon>(true) && !utility.transform.Find("Label").gameObject.activeSelf
                    && utility.GetComponent<ResearchControlHint>(), "Icon utility has a consistent mesh, localized tooltip and no permanent caption: " + utility.command);
            }
            Require(!controller.trainingRoot.activeSelf, "Training objects unavailable during experiment");
            Require(controller.view.ParticipantCanvases.Where(c => c.name == "TutorialHelperKiosk").All(c => !c.gameObject.activeSelf), "Tutorial helper unavailable during experiment");
            Require(!controller.view.GetComponentsInChildren<RectTransform>(false).Any(r => r.name == "TutorialChecklist"), "No experimental checklist");
            Require(!controller.view.GetComponentsInChildren<RectTransform>(false).Any(r => r.name == "TutorialDemo" || r.name == "TutorialSuccess" || r.name.StartsWith("PracticeHighlight_")),
                "Experimental tasks get no teaching demo, success coaching or object highlight");
            var commands = ActiveCommands();
            Require(new[] { "reference", "inspect", "pause" }.All(commands.Contains), "Reference, Inspect and Pause remain available");
            Require(!commands.Contains("helper"), "No experimental helper action");
            var participantText = string.Join(" ", controller.view.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text));
            Require(!new[] { "H1", "H2", "H3", "H4", "DEV00", "Thai Group", "Japanese Group", "Efficiency Score" }.Any(participantText.Contains), "Participant text does not expose researcher identifiers or analytics");
            // P10: the experiment observes, it does not coach or rate. Runs are Thai or Japanese, so the terms are too.
            Require(!new[] { "score", "efficien", "strategy", "hint", "next step", "คะแนน", "ประสิทธิภาพ", "กลยุทธ์", "คำใบ้", "ขั้นตอนถัดไป", "スコア", "得点", "効率", "戦略", "ヒント", "次の手順" }
                .Any(participantText.ToLowerInvariant().Contains), "Experiment text has no score, rating, strategy label or coaching");
        }

        private static void VerifyLog()
        {
            var savedEvents = new List<JObject>();
            using (var reader = new StreamReader(new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                for (string line; (line = reader.ReadLine()) != null;)
                    if (!string.IsNullOrWhiteSpace(line)) savedEvents.Add(JObject.Parse(line));
            var events = savedEvents.ToArray();
            Require(events.All(e => e.Value<bool>("technical_test") && e.Value<bool>("test_data")), "Every recorded event is technical test data");
            Require(!events.Any(e => e.Value<string>("event") == "THROW"), "No semantic THROW events");
            foreach (var name in new[] { "RUN_STARTED", "RUN_COMPLETED", "INFORMATION_OPEN", "INFORMATION_CLOSE", "EXAMINE", "REPAIR", "INSPECT", "INSPECT_FAIL", "INSPECT_PASS", "RETRY", "TASK_STARTED", "TASK_COMPLETED", "PANEL_REPOSITION", "PANEL_MINIMIZE", "PANEL_RESTORE" })
                Require(events.Any(e => e.Value<string>("event") == name), "Raw log includes " + name);
            Require(events.Count(e => e.Value<string>("event") == "TASK_COMPLETED") == 2, "Exactly two completed tasks in raw log");
            foreach (var taskId in new[] { "A", "B" })
            {
                var taskEvents = events.Where(e => e.Value<string>("task_id") == taskId).ToArray();
                var failed = taskEvents.First(e => e.Value<string>("event") == "INSPECT_FAIL");
                var passed = taskEvents.Single(e => e.Value<string>("event") == "INSPECT_PASS");
                var completed = taskEvents.Single(e => e.Value<string>("event") == "TASK_COMPLETED");
                Require(failed.Value<long>("seq") < passed.Value<long>("seq") && passed.Value<long>("seq") < completed.Value<long>("seq"), "Task " + taskId + " invalid fixture fails before valid fixture passes and task completes");
            }
            var examinations = events.Where(e => e.Value<string>("event") == "EXAMINE").ToArray();
            Require(examinations.Length == 4 && examinations.All(e => e.Value<string>("target") == "A.module" || e.Value<string>("target") == "B.module"),
                "Raw examination log contains only four staged selections of the two real assemblies; no unavailable connection/mount IDs");
            Require(examinations.All(e => e.Value<string>("input_source") == "SimulatedController"), "Raw examination events retain their actual simulated pointer attribution");
            if (!technicalSetup) return;
            Require(events.Count(e => e.Value<string>("event") == "TECHNICAL_SELF_START" && e.Value<string>("source") == "Tester") == 1, "Headset technical start is logged once with Tester provenance");
            Require(events.Count(e => e.Value<string>("event") == "TECHNICAL_CHECKPOINT_RESUME" && e.Value<string>("source") == "Tester") == 2, "Both checkpoint continues are logged as Tester actions");
            Require(events.Count(e => e.Value<string>("event") == "DEMO_STATE_SET" && e.Value<string>("source") == "Tester") == 2, "Each demo pass fixture is logged as a Tester action");
            Require(events.Count(e => e.Value<string>("event") == "CHECKPOINT_COMPLETED") == 2, "Existing checkpoint completion path still records both checkpoints");
        }

        private static void SetupClick(string command)
        {
            var action = controller.view.GetComponentsInChildren<ResearchUIAction>(false).FirstOrDefault(a => a.command == command && a.GetComponent<Button>().IsInteractable());
            Require(action != null, "Active real setup Button available: " + command);
            var pointer = StagedOwnedPointer();
            action.OnPointerDown(pointer); action.OnPointerUp(pointer); action.GetComponent<Button>().onClick.Invoke();
        }

        private static PointerEventData StagedOwnedPointer()
        {
            var ray = controller.guards.Where(g => g && g.interactionsRoot).SelectMany(g => g.interactionsRoot.GetComponentsInChildren<Oculus.Interaction.RayInteractor>(true))
                .FirstOrDefault(r => r.isActiveAndEnabled && controller.InputSourceForPointer(r.Identifier) == "SimulatedController");
            Require(ray, "Explicit offline callback fixture uses an owned SimulatedController pointer"
                + (ray ? "" : " | " + JsonConvert.SerializeObject(OwnedInputDiagnostics())));
            return new PointerEventData(EventSystem.current) { pointerId = ray.Identifier };
        }

        private static IEnumerator TechnicalSetupStart()
        {
            SetDisplay(0);
            Require(controller.Record == null && controller.Flow == null, "Technical package boots into the in-headset technical setup");
            Require(controller.guards.All(g => g.SuspensionReason != "researcher_setup"), "Technical setup does not wait on an external researcher suspension");
            Require(!controller.trainingRoot.activeSelf && controller.locomotionInputRoots.All(r => !r.activeInHierarchy)
                && controller.taskPorts.All(p => !p.InteractionAllowed), "Objects, locomotion and task ports stay gated before the session");
            yield return Stable();
            var title = controller.view.mainPanel.GetComponentsInChildren<TMP_Text>(false).Single(t => t.name == "Title");
            Require(title.text == ResearchStrings.Get("technical.setup.title", "Thai"), "Default setup language is Thai");
            Require(!controller.TechnicalReady && !ActiveCommands().Contains("technical_start"), "Start Full Test is disabled until headset, both controllers, floor and logger are ready");
            ResearchFlowVisualInput.Begin(1.65f); // Synthetic Touch/HMD; the guards' own tracking gates decide readiness.
            var start = EditorApplication.timeSinceStartup;
            while (!controller.TechnicalReady) { if (EditorApplication.timeSinceStartup - start > 10) throw new TimeoutException("Technical readiness never reached"); yield return null; }
            yield return Stable();
            Require(controller.SetupLanguage == ParticipantLanguage.Thai && controller.SetupInput == ExperimentInputMode.Controller && controller.SetupOrder == TaskOrder.AB, "Defaults are Thai / Controller / AB");
            Require(!ActiveCommands().Contains("technical_input") && controller.guards.All(g => g.policy == VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabInputGuard.ModePolicy.Controllers), "Headset setup has no input-mode choice and requires controller guards");
            controller.Command("technical_input", "ProgrammaticOfflineFixture");
            Require(controller.SetupInput == ExperimentInputMode.Controller, "Retired input-mode command cannot enable hand mode");
            var primary = ActiveCommands().Where(c => !c.StartsWith("technical_language") && !c.StartsWith("technical_input") && !c.StartsWith("technical_order")).ToArray();
            Require(primary.SequenceEqual(new[] { "technical_start" }), "Exactly one primary action: Start Full Test");
            var readiness = controller.view.mainPanel.transform.Find("Expanded/TechnicalReadiness");
            var readinessLabels = readiness.GetComponentsInChildren<TMP_Text>(false);
            Require(readiness.gameObject.activeInHierarchy && readiness.childCount == 5, "Setup shows five readiness rows");
            foreach (var key in new[] { "technical.headset", "technical.left", "technical.right", "technical.floor", "technical.logger" })
                Require(readinessLabels.Any(t => t.text == ResearchStrings.Get(key, "Thai")), "Readiness shows " + key);
            Require(controller.view.mainPanel.GetComponentsInChildren<TMP_Text>(false).All(t => !t.text.Contains("DEV") && !t.text.Contains("TH0") && !t.text.Contains("JP0")), "Setup never asks for or shows a participant ID");
            Require(!ActiveCommands().Any(c => c == "technical_language" || c == "technical_order"), "Participant setup has no operator language/order options");
            if (requestedLanguage == "Japanese") controller.Command("technical_language", "ProgrammaticOfflineFixture");
            if (requestedOrder == TaskOrder.BA) controller.Command("technical_order", "ProgrammaticOfflineFixture");
            yield return Stable();
            Require(controller.SetupLanguage.ToString() == requestedLanguage && controller.SetupOrder == requestedOrder, "Explicit technical operator configures language/order before participant start");
            foreach (var option in new[] { null, "technical_order" })
            {
                if (option != null) { controller.Command(option, "ProgrammaticOfflineFixture"); yield return Stable(); }
                foreach (var label in readinessLabels)
                { label.ForceMeshUpdate(); Require(label.preferredHeight <= label.rectTransform.rect.height + 1, "Readiness label fits its row"); }
                foreach (var label in controller.view.mainPanel.GetComponentsInChildren<TMP_Text>(false))
                { label.ForceMeshUpdate(); Require(label.textInfo.lineCount <= 1, "Setup label stays on one line: " + label.name + " '" + label.text + "'"); }
                if (option != null) { controller.Command(option, "ProgrammaticOfflineFixture"); yield return Stable(); }
            }
            Require(controller.SetupInput == ExperimentInputMode.Controller && controller.SetupOrder == requestedOrder, "Option cycling returns to the requested settings");
            yield return Capture("00_technical_setup_ready");
            int devBefore = controller.Registry.List(ParticipantGroup.Dev).Count;
            SetupClick("technical_start");
            Require(controller.TechnicalSelfTest && controller.Record != null && controller.Record.group == ParticipantGroup.Dev && controller.Record.test_data
                && controller.Registry.List(ParticipantGroup.Dev).Count == devBefore + 1, "Start Full Test created the next DEV test-data record");
            Require(controller.Flow.State == ResearchFlowState.TutorialWelcome, "Flow left ResearcherSetup through StartSession");
            yield return Transition("arrival_training");
            start = EditorApplication.timeSinceStartup;
            while (controller.guards.Any(g => !g.InteractionsAllowed)) { if (EditorApplication.timeSinceStartup - start > 5) throw new TimeoutException("Guards never released"); yield return null; }
            Require(controller.guards.All(g => g.TrackingValid && g.InteractionsAllowed && !g.Suspended), "Both controller guards released after the station transition");
        }

        private static Dictionary<Transform, Matrix4x4> worldPoses;

        // The rig moves; the world never does. Snapshot static world transforms, then simulate physical head motion and recenter.
        private static IEnumerator WorldAnchorChecks()
        {
            var origin = controller.origin.transform;
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name.StartsWith("Environment_") || g.name == "ResearchFlow_EnvironmentSecondPass" || g.name == "ResearchFlow_EnvironmentDepthPass")
                .Select(g => g.transform).Concat(controller.stations).Concat(controller.taskPorts.Select(p => p.transform))
                .Concat(controller.GetComponentsInChildren<ResearchExamineTarget>(true).Select(t => t.transform)).ToArray();
            Require(roots.Length > 3 && roots.All(r => !r.IsChildOf(origin) && !origin.IsChildOf(r)), "No room, table, workstation or station is parented under the XR rig (or vice versa)");
            Require(!controller.trainingRoot.transform.IsChildOf(origin), "Practice table content is not parented under the XR rig");
            worldPoses = roots.Concat(new[] { controller.trainingRoot.transform }).SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => !t.GetComponentInParent<Rigidbody>(true) && !t.GetComponentInParent<Animator>(true) && !(t is RectTransform)).Distinct()
                .ToDictionary(t => t, t => t.localToWorldMatrix);
            var originPose = origin.localToWorldMatrix;
            var cameraLocal = controller.origin.Camera.transform.localPosition;
            foreach (var step in new[] { (new Vector3(.6f, 0, -.4f), 0f), (new Vector3(-.5f, -.45f, .3f), 40f), (new Vector3(.2f, 0, .7f), -30f), (Vector3.zero, 0f) })
            {
                ResearchFlowVisualInput.SetHeadMotion(step.Item1, step.Item2);
                yield return Stable();
                var moved = controller.origin.Camera.transform.localPosition - cameraLocal;
                Require(Vector3.Distance(moved, step.Item1) < .02f, "Physical head motion is tracked 1:1 in the rig (" + step.Item1 + ")");
                Require(origin.localToWorldMatrix == originPose, "Physical head motion does not move the XR origin");
                WorldUnchanged("physical head motion " + step.Item1);
            }
            controller.view.RecenterAll(false); yield return Stable();
            WorldUnchanged("panel recenter");
        }

        private static void WorldUnchanged(string after)
        {
            var moved = worldPoses.Where(p => p.Key && p.Key.localToWorldMatrix != p.Value).Select(p => p.Key.name).Take(5).ToArray();
            Require(moved.Length == 0, "Room/table/workstations stay fixed in world space after " + after + (moved.Length > 0 ? ": " + string.Join(", ", moved) : ""));
        }

        private static IEnumerator Stable()
        {
            var start = EditorApplication.timeSinceStartup;
            int frame = Time.frameCount;
            while (EditorApplication.timeSinceStartup - start < .45 || Time.frameCount < frame + 2) { gameView.Repaint(); yield return null; }
            yield return AwaitOwnedUiInput();
        }

        private static bool OwnedUiInputExpected => ResearchFlowVisualInput.Running && controller && !controller.Transitioning
            && string.IsNullOrEmpty(controller.Error) && ((controller.TechnicalSelfSetup && controller.Record == null)
                || (controller.Record?.session_state == RegistryState.InProgress && controller.Logger?.State == HandsLabLogState.Active));

        private static bool OwnedUiInputReady()
        {
            if (!Application.isFocused || !controller.FloorReady || controller.guards.Length != 2) return false;
            return controller.guards.All(guard => guard && guard.controllerReference is Oculus.Interaction.Input.IController input
                && input.IsConnected && input.IsPoseValid && guard.TrackingValid && guard.InteractionsAllowed
                && !guard.WaitingForNeutral && !guard.Suspended
                && guard.interactionsRoot && guard.interactionsRoot.GetComponentsInChildren<Oculus.Interaction.RayInteractor>(true)
                    .Any(ray => ray.isActiveAndEnabled && controller.InputSourceForPointer(ray.Identifier) == "SimulatedController"));
        }

        private static IEnumerator AwaitOwnedUiInput()
        {
            double timeout = EditorApplication.timeSinceStartup + 5;
            // Pause retains UI permission. Only transitions/terminal sessions intentionally suspend this input.
            // Observe the real SDK and guard state; elapsed wall time alone cannot establish readiness after focus changes.
            while (OwnedUiInputExpected && !OwnedUiInputReady())
            {
                if (EditorApplication.timeSinceStartup >= timeout)
                    throw new TimeoutException("Owned UI input did not become ready: " + JsonConvert.SerializeObject(OwnedInputDiagnostics()));
                gameView.Repaint(); yield return null;
            }
        }

        private static IEnumerator AwaitTutorialActionReady(string fact)
        {
            yield return AwaitOwnedUiInput();
            double timeout = EditorApplication.timeSinceStartup + 5;
            while (!controller.view.TutorialActionReady(fact))
            {
                if (controller.Flow?.IsTutorial != true || controller.TutorialFact != fact || EditorApplication.timeSinceStartup >= timeout)
                    throw new TimeoutException("Tutorial action was not ready before simulated evidence: " + JsonConvert.SerializeObject(new
                    { fact, current_fact = controller.TutorialFact, instruction_fact = controller.TutorialInstructionFact,
                        controller.TutorialNeedsGrab, controller.TutorialNeedsRelease, input = OwnedInputDiagnostics() }));
                gameView.Repaint(); yield return null;
            }
        }

        private static object OwnedInputDiagnostics()
        {
            var settings = UnityEngine.InputSystem.InputSystem.settings;
            return new
            {
                focused = Application.isFocused, focused_window = EditorWindow.focusedWindow?.GetType().Name,
                background_behavior = settings.backgroundBehavior.ToString(), editor_input_behavior = settings.editorInputBehaviorInPlayMode.ToString(),
                input = ResearchFlowVisualInput.Audit(), controller.FloorReady, controller.Transitioning,
                controller.ReferenceOpen, controller.ReferenceMinimized, recovery_active = controller.view.RecoveryCanvas.gameObject.activeInHierarchy,
                devices = UnityEngine.InputSystem.InputSystem.devices.Where(device => device.name.StartsWith("HandsLabSimulated", StringComparison.Ordinal))
                    .Select(device => new { device.name, device.added, device.enabled, device.canRunInBackground, device.lastUpdateTime, device.wasUpdatedThisFrame,
                        tracked = device is UnityEngine.InputSystem.TrackedDevice trackedDevice ? (bool?)trackedDevice.isTracked.isPressed : null,
                        tracking_state = device is UnityEngine.InputSystem.TrackedDevice poseDevice ? (int?)poseDevice.trackingState.ReadValue() : null }).ToArray(),
                guards = controller.guards.Where(guard => guard).Select(guard => new { side = guard.side.ToString(), guard.Suspended,
                    guard.SuspensionReason, guard.InputNeutral, connected = (guard.controllerReference as Oculus.Interaction.Input.IController)?.IsConnected,
                    pose_valid = (guard.controllerReference as Oculus.Interaction.Input.IController)?.IsPoseValid }).ToArray(),
                rays = controller.guards.Where(guard => guard && guard.interactionsRoot)
                    .SelectMany(guard => guard.interactionsRoot.GetComponentsInChildren<Oculus.Interaction.RayInteractor>(true))
                    .Select(ray => new { ray.name, ray.Identifier, ray.isActiveAndEnabled, state = ray.State.ToString(), source = controller.InputSourceForPointer(ray.Identifier) }).ToArray()
            };
        }

        private static IEnumerator Transition(string arrivalCapture = null)
        {
            var start = EditorApplication.timeSinceStartup;
            while (controller.Transitioning) { if (EditorApplication.timeSinceStartup - start > 10) throw new TimeoutException("Station transition timeout"); yield return null; }
            if (arrivalCapture != null) yield return Capture(arrivalCapture, Time.realtimeSinceStartupAsDouble, Time.frameCount);
            yield return Stable();
            Require(string.IsNullOrEmpty(controller.Error), "Transition finished without runtime error");
        }

        private static IEnumerator Capture(string label, double? transitionObservedRealtime = null, int transitionObservedFrame = -1)
        {
            if (controller.Flow != null && controller.Flow.State == ResearchFlowState.Task2Ready)
            {
                var heading = controller.view.mainPanel.transform.Find("Expanded/Title").GetComponent<TMP_Text>(); heading.ForceMeshUpdate();
                Require(heading.textInfo.lineCount == 1 && !heading.isTextOverflowing && heading.preferredWidth <= heading.rectTransform.rect.width + 1,
                    "Task2 Ready title fits one readable line without reducing its font");
            }
            if (label == "03_tutorial_checklist")
            {
                var pause = controller.view.GetComponentsInChildren<ResearchUIAction>(false).Single(a => a.command == "pause");
                var hit = (RectTransform)pause.transform;
                Require(pause.GetComponentInChildren<ResearchUtilityIcon>(true) && hit.rect.width >= 64 && hit.rect.height >= 64
                    && pause.GetComponent<ResearchControlHint>()?.key == "action.pause_short" && pause.transform.Find("Label").gameObject.activeSelf
                    && pause.transform.Find("Label").GetComponent<TMP_Text>().text == ResearchStrings.Get("action.pause_caption", requestedLanguage),
                    "Tutorial Pause uses a mesh icon, generous target and localized contextual label");
            }
            if (label == "05_task_A" || label == "09_task_B")
            {
                foreach (var action in controller.view.mainPanel.GetComponentsInChildren<ResearchUIAction>(false)
                    .Where(a => a.command == "reference" || a.command == "inspect" || a.command == "pause"))
                {
                    var corners = new Vector3[4]; ((RectTransform)action.transform).GetWorldCorners(corners);
                    Require(corners.Select(controller.origin.Camera.WorldToViewportPoint).All(p => p.z > 0 && p.x >= .02f && p.x <= .98f && p.y >= .02f && p.y <= .98f),
                        "Initial task Reference/Inspect/Pause is fully inside the viewport: " + action.command);
                }
                var texts = controller.view.mainPanel.GetComponentsInChildren<TMP_Text>(false);
                var heading = texts.FirstOrDefault(t => t.name == "Title");
                var detail = texts.FirstOrDefault(t => t.name == "Body");
                if (heading && detail)
                {
                    heading.ForceMeshUpdate();
                    Require(heading.preferredHeight <= heading.rectTransform.rect.height + 1, "Task heading fits after localized wrapping");
                    if (requestedLanguage == "Japanese") Require(heading.textInfo.lineInfo.Take(heading.textInfo.lineCount).All(line => line.visibleCharacterCount >= 2), "Japanese task heading has no isolated final character");
                    float headingBottom = heading.rectTransform.anchoredPosition.y - heading.rectTransform.rect.height / 2;
                    float detailTop = detail.rectTransform.anchoredPosition.y + detail.rectTransform.rect.height / 2;
                    Require(headingBottom - detailTop >= 12, "Task heading and body retain at least 12 mm spacing");
                }
            }
            gameView.Focus(); gameView.Repaint();
            bool isArrival = label.StartsWith("arrival_", StringComparison.Ordinal);
            if (!isArrival) yield return Stable();
            else
            {
                // Observe the real transient cue promptly; the generic .45s/two-frame wait can outlive it.
                var cue = controller.view.transform.Find("ArrivalCue");
                var cueGroup = cue ? cue.GetComponent<CanvasGroup>() : null;
                double deadline = EditorApplication.timeSinceStartup + 2;
                while (cueGroup && cueGroup.gameObject.activeInHierarchy && cueGroup.alpha <= .1f
                    && EditorApplication.timeSinceStartup < deadline)
                { gameView.Repaint(); yield return null; }
            }
            var referenceScroll = controller.view.referencePanel.GetComponentInChildren<ScrollRect>(true);
            if (controller.ReferenceOpen && !controller.ReferenceMinimized && referenceScroll.gameObject.activeInHierarchy)
            {
                var text = referenceScroll.viewport.GetComponentInChildren<TMP_Text>(); text.ForceMeshUpdate(true);
                var rect = referenceScroll.viewport.rect; var padding = referenceScroll.viewport.GetComponent<RectMask2D>().padding;
                var glyphs = text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(ch => ch.isVisible && ch.pageNumber == text.pageToDisplay - 1).ToArray();
                Require(glyphs.Length > 0 && glyphs.All(ch => {
                    var bottom = referenceScroll.viewport.InverseTransformPoint(text.transform.TransformPoint(ch.bottomLeft));
                    var top = referenceScroll.viewport.InverseTransformPoint(text.transform.TransformPoint(ch.topRight));
                    return bottom.x >= rect.xMin + padding.x - 1 && bottom.y >= rect.yMin + padding.y - 1
                        && top.x <= rect.xMax - padding.z + 1 && top.y <= rect.yMax - padding.w + 1;
                }), "Every visible Reference page glyph fits its actual viewport mask: " + label);
                if (requestedLanguage == "Japanese")
                {
                    Require(text.textInfo.lineInfo.Take(text.textInfo.lineCount).Where(line => line.visibleCharacterCount > 0
                        && text.textInfo.characterInfo[line.firstVisibleCharacterIndex].pageNumber == text.pageToDisplay - 1)
                        .All(line => line.visibleCharacterCount >= (text.textInfo.characterInfo[line.lastVisibleCharacterIndex].character == '。' ? 3 : 2)),
                        "Japanese Reference has no isolated final syllable: " + label);
                    var characters = text.textInfo.characterInfo.Take(text.textInfo.characterCount).ToArray();
                    var plain = new string(characters.Select(ch => ch.character).ToArray());
                    foreach (System.Text.RegularExpressions.Match suffix in System.Text.RegularExpressions.Regex.Matches(plain, "してください。|ください。"))
                        Require(characters.Skip(suffix.Index).Take(suffix.Length).Select(ch => ch.lineNumber).Distinct().Count() == 1,
                            "Japanese instruction closing phrase stays on one rendered line: " + label);
                }
            }
            if (controller.ReferenceOpen && !controller.ReferenceMinimized)
                foreach (var tab in controller.view.referencePanel.GetComponentsInChildren<TMP_Text>(false)
                    .Where(t => t.transform.parent.name.StartsWith("Tab_") || new[] { "Pause", "Minimize", "Close" }.Contains(t.transform.parent.name)))
                {
                    tab.ForceMeshUpdate();
                    var tabButton = tab.transform.parent.GetComponent<Button>();
                    bool unavailable = tabButton && !tabButton.interactable;
                    Require(tab.textInfo.lineCount <= (unavailable ? 3 : 2) && !tab.isTextOverflowing
                        && (!unavailable || tab.text.Contains(ResearchStrings.Get("reference.unavailable", requestedLanguage))),
                        "Reference format identity and explicit unavailable status fit the tab: " + tab.transform.parent.name + " " + label);
                }
            if (controller.ReferenceOpen && !controller.ReferenceMinimized && controller.Flow.IsTutorial && controller.ReferenceSection == "illustrated")
                foreach (var text in controller.view.referencePanel.GetComponentsInChildren<TMP_Text>(false).Where(t => t.name == "Caption"))
                {
                    text.ForceMeshUpdate();
                    Require(!text.isTextOverflowing && (requestedLanguage != "Japanese" || text.textInfo.lineInfo.Take(text.textInfo.lineCount).All(line => line.visibleCharacterCount >= 2)),
                        "Illustrated caption fits without an isolated Japanese character");
                }
            foreach (var hint in controller.view.GetComponentsInChildren<ResearchControlHint>(false)
                .Where(h => h.GetComponent<ResearchDragHandle>() && h.label && h.label.gameObject.activeInHierarchy))
            {
                hint.label.ForceMeshUpdate();
                Require(hint.label.textInfo.lineCount == 1 && !hint.label.isTextOverflowing
                    && hint.label.preferredWidth <= hint.label.rectTransform.rect.width + 1,
                    "Visible drag hint fits one line without clipping: " + hint.key + " " + label);
            }
            if (requestedLanguage == "Japanese" && controller.Flow != null && controller.Flow.State == ResearchFlowState.PostTask1Checkpoint)
                foreach (var text in controller.view.mainPanel.transform.Find("Expanded/CheckpointSteps").GetComponentsInChildren<TMP_Text>(false))
                {
                    text.ForceMeshUpdate();
                    Require(!text.isTextOverflowing && text.textInfo.lineInfo.Take(text.textInfo.lineCount).All(line => line.visibleCharacterCount >= 2),
                        "Japanese checkpoint has no clipping or isolated final character");
                }
            foreach (var panel in new[] { controller.view.mainPanel, controller.view.referencePanel })
                if (ReadDisplay() == 0 && panel.gameObject.activeInHierarchy && panel.GetComponent<CanvasGroup>().alpha > .1f)
                    Require(panel.GetComponent<Canvas>().enabled && !panel.PlacementBlocked,
                        "Expected participant panel is rendered at capture: " + panel.panelId + " " + label);
            foreach (var icon in controller.view.GetComponentsInChildren<ResearchUtilityIcon>(false)
                .Where(icon => icon.GetComponentsInParent<CanvasGroup>().All(group => group.alpha > .1f)))
                Require(icon.GetComponent<CanvasRenderer>() && icon.canvasRenderer.GetMesh() && icon.canvasRenderer.GetMesh().vertexCount > 0,
                    "Visible utility has an actual rendered mesh: " + icon.transform.parent.name + " " + label);
            var path = Path.Combine(output, "final-screenshots", prefix + "_" + screenshots.Count.ToString("D3") + "_" + label + ".png");
            if (File.Exists(path)) throw new IOException("Refusing to overwrite capture evidence: " + path);
            float? arrivalAlpha = null;
            if (isArrival)
            {
                var arrival = controller.view.transform.Find("ArrivalCue");
                var group = arrival ? arrival.GetComponent<CanvasGroup>() : null;
                lastArrivalCaptureEvidence = new
                {
                    label, transitionObservedFrame, transitionObservedRealtime, checkFrame = Time.frameCount,
                    checkRealtime = Time.realtimeSinceStartupAsDouble,
                    elapsedSinceTransitionObserved = transitionObservedRealtime.HasValue ? Time.realtimeSinceStartupAsDouble - transitionObservedRealtime.Value : (double?)null,
                    alpha = group ? (float?)group.alpha : null, active = group && group.gameObject.activeInHierarchy,
                    caption = arrival ? arrival.GetComponentInChildren<TMP_Text>()?.text : null,
                    floorReady = controller.FloorReady, paused = controller.Flow?.Paused, error = controller.Error
                };
                Require(group != null && group.gameObject.activeInHierarchy && group.alpha > .1f, "Naturally triggered arrival cue is visible at capture: " + label);
                var camera = controller.origin.Camera;
                var cueCorners = new Vector3[4]; arrival.GetComponent<RectTransform>().GetWorldCorners(cueCorners);
                var cuePoints = cueCorners.Select(camera.WorldToViewportPoint).ToArray();
                Require(cuePoints.All(p => p.z > 0 && p.x >= .02f && p.x <= .98f && p.y >= .02f && p.y <= .98f), "Arrival cue remains inside the viewport: " + label);
                var cueBounds = Rect.MinMaxRect(cuePoints.Min(p => p.x), cuePoints.Min(p => p.y), cuePoints.Max(p => p.x), cuePoints.Max(p => p.y));
                var heading = controller.view.mainPanel.transform.Find("Expanded/Title").GetComponent<TMP_Text>(); heading.ForceMeshUpdate();
                foreach (var ch in heading.textInfo.characterInfo.Take(heading.textInfo.characterCount).Where(ch => ch.isVisible))
                {
                    var glyphPoints = new[] { ch.bottomLeft, ch.topLeft, ch.topRight, ch.bottomRight }
                        .Select(point => camera.WorldToViewportPoint(heading.transform.TransformPoint(point))).ToArray();
                    Require(!cueBounds.Overlaps(Rect.MinMaxRect(glyphPoints.Min(p => p.x), glyphPoints.Min(p => p.y), glyphPoints.Max(p => p.x), glyphPoints.Max(p => p.y))),
                        "Arrival cue does not cover a task heading glyph: " + label);
                }
                arrivalAlpha = group.alpha;
            }
            int screenshotRequestedFrame = Time.frameCount;
            double screenshotRequestedRealtime = Time.realtimeSinceStartupAsDouble;
            ScreenCapture.CaptureScreenshot(path);
            var start = EditorApplication.timeSinceStartup;
            while (!File.Exists(path) || new FileInfo(path).Length < 1024) { if (EditorApplication.timeSinceStartup - start > 15) throw new TimeoutException("GameView screenshot not produced: " + label); yield return null; }
            var layout = controller.view.GetComponentsInChildren<TMP_Text>(false).Select(t => { t.ForceMeshUpdate(); return new { name = t.name, text = t.text, overflowing = t.isTextOverflowing, font = t.font != null ? t.font.name : "missing" }; }).ToArray();
            screenshots.Add(new { label, path, gameViewDisplay = ReadDisplay(), frame = Time.frameCount, screenshotRequestedFrame, screenshotRequestedRealtime,
                method = "ScreenCapture.CaptureScreenshot actual GameView", arrivalAlpha, arrivalCaptureEvidence = isArrival ? lastArrivalCaptureEvidence : null, layout, hitTargets = CaptureHitTargets(label) });
            if (label == "06_reference_task_A") yield return ResolutionPair(label);
            // The PNG observes the requested frame first; only the following callback waits for owned input recovery.
            yield return AwaitOwnedUiInput();
        }

        // Editor evidence only: fresh supersampled rendering is distinct from enlarging an existing PNG.
        static IEnumerator ResolutionPair(string label)
        {
            string directory = Path.Combine(output, "resolution-pair", prefix); Directory.CreateDirectory(directory);
            var camera = controller.origin.Camera;
            var animator = UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)
                .FirstOrDefault(a => a.runtimeAnimatorController && a.runtimeAnimatorController.name == "Factory_Loop");
            float speedBefore = animator ? animator.speed : 0;
            foreach (string key in new[] { "EnvironmentV2Isolated.ContinuationRuntime", "EnvironmentV2Isolated.Runtime" })
            { string state = SessionState.GetString(key, ""); Require(state != "running" && state != "entering", "Resolution pair has no foreign factory sampling callback: " + key); }
            float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
            float[] Q(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
            var captures = new List<object>();
            try
            {
                if (animator) animator.speed = 0;
                foreach (int superSize in new[] { 1, 2 })
                {
                    string capture = Path.Combine(directory, "SUPER" + superSize + ".png");
                    var pose = new { position = V(camera.transform.position), rotation = Q(camera.transform.rotation) };
                    var projection = Enumerable.Range(0, 16).Select(i => camera.projectionMatrix[i]).ToArray();
                    var farGeometry = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                        .Where(r => r.name.StartsWith("Production_Straight") || r.name.StartsWith("Production_Support"))
                        .Select(r => new { r.name, position = V(r.transform.position), rotation = Q(r.transform.rotation), scale = V(r.transform.lossyScale) }).ToArray();
                    var factoryTransforms = animator ? animator.GetComponentsInChildren<Transform>(true)
                        .Select(t => new { t.name, matrix = Enumerable.Range(0, 16).Select(i => t.localToWorldMatrix[i]).ToArray() }).ToArray() : null;
                    int requestedFrame = Time.frameCount;
                    ScreenCapture.CaptureScreenshot(capture, superSize);
                    double start = EditorApplication.timeSinceStartup;
                    while (!File.Exists(capture) || new FileInfo(capture).Length < 1024)
                    { if (EditorApplication.timeSinceStartup - start > 15) throw new TimeoutException("Resolution pair not produced: " + capture); yield return null; }
                    var texture = new Texture2D(2, 2);
                    int width, height;
                    try { Require(texture.LoadImage(File.ReadAllBytes(capture)), "Resolution pair PNG decodes"); width = texture.width; height = texture.height; }
                    finally { UnityEngine.Object.Destroy(texture); }
                    Require(width == Screen.width * superSize && height == Screen.height * superSize, "Resolution pair uses actual fresh superSize=" + superSize);
                    captures.Add(new { capture, superSize, width, height, screenWidth = Screen.width, screenHeight = Screen.height,
                        cameraPixelWidth = camera.pixelWidth, cameraPixelHeight = camera.pixelHeight, camera.fieldOfView, camera.aspect,
                        stereo = camera.stereoEnabled, target = camera.targetTexture ? camera.targetTexture.name : "GameView",
                        pose, projection, farGeometry, factoryTransforms, animatorTime = animator ? animator.GetCurrentAnimatorStateInfo(0).normalizedTime : 0,
                        requestedFrame, completedFrame = Time.frameCount });
                }
            }
            finally
            {
                if (animator) animator.speed = speedBefore;
                File.WriteAllText(Path.Combine(directory, "captures.json"), JsonConvert.SerializeObject(new { label, captures,
                    animator = animator ? animator.name : "none", speedBefore, restored = !animator || animator.speed == speedBefore,
                    scope = "Fresh mono Editor GameView render; factory Animator frozen only for this pair; shader time is not frozen; no native or wearer sharpness proof",
                    hardware_verified = false }, Formatting.Indented));
            }
        }

        // Meta's ray only hovers inside the canvas RayInteractable surface (BoundsClipper = canvas root rect).
        // A control drawn outside it looks pressable but cannot be hit (the old move bar lost its bottom third).
        private static void RequireRayReachable(RectTransform rect, string label)
        {
            var surface = rect.GetComponentInParent<Canvas>()?.rootCanvas.GetComponentInChildren<Oculus.Interaction.RayInteractable>();
            if (!surface) return;
            var head = controller.view.Head.position; var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) * .5f;
            foreach (var point in corners.Select(c => Vector3.Lerp(center, c, .85f)).Append(center))
                Require(surface.Raycast(new Ray(head, (point - head).normalized), out _, 10f, false),
                    "Ray surface contains " + rect.name + " at " + label + " (" + rect.GetComponentInParent<Canvas>().rootCanvas.name + ")");
        }

        private static object[] CaptureHitTargets(string label)
        {
            var head = controller.view.Head.position;
            // Tutorial may point at a control; Task A/B must not (no cue beyond the neutral affordance).
            foreach (var handle in controller.view.GetComponentsInChildren<ResearchDragHandle>(true))
                Require(!handle.highlight || (controller.Flow.IsTutorial && controller.TutorialFact == "reposition" && handle.panel == controller.view.referencePanel),
                    "Drag-handle highlight only in the Tutorial move-panel step: " + label);
            foreach (var rect in controller.view.GetComponentsInChildren<Button>(false).Where(b => b.IsInteractable()).Select(b => (RectTransform)b.transform)
                .Concat(controller.view.GetComponentsInChildren<ResearchDragHandle>(false).Select(h => (RectTransform)h.transform)))
                if (rect.GetComponentsInParent<CanvasGroup>().Aggregate(1f, (alpha, group) => alpha * group.alpha) > .5f
                    && rect.GetComponentsInParent<CanvasGroup>().All(g => g.blocksRaycasts)) RequireRayReachable(rect, label);
            return controller.view.GetComponentsInChildren<Button>(false).Select(b => (RectTransform)b.transform)
                .Concat(controller.GetComponentsInChildren<ResearchExamineTarget>(false).SelectMany(target => target.GetComponentsInChildren<Button>(false)).Select(button => (RectTransform)button.transform))
                .Concat(controller.view.GetComponentsInChildren<ResearchDragHandle>(false).Select(h => (RectTransform)h.transform))
                .Distinct().Select(rect =>
                {
                    var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    float width = Vector3.Distance(corners[0], corners[3]), height = Vector3.Distance(corners[0], corners[1]);
                    var center = (corners[0] + corners[2]) * .5f;
                    float distance = Vector3.Distance(head, center);
                    var panel = rect.GetComponentInParent<ResearchPanelMotion>();
                    var canvas = rect.GetComponentInParent<Canvas>();
                    var action = rect.GetComponent<ResearchUIAction>();
                    var button = rect.GetComponent<Button>();
                    var examine = rect.GetComponentInParent<ResearchExamineTarget>();
                    return (object)new
                    {
                        name = rect.name, command = action ? action.command : examine ? "examine" : "panel_drag_handle",
                        target = examine ? examine.stableTarget : "",
                        panel = panel ? panel.panelId : canvas ? canvas.name : "none",
                        canvasSize = new[] { rect.rect.width, rect.rect.height },
                        canvasScale = canvas ? canvas.transform.lossyScale.x : 0,
                        physicalMillimeters = new[] { width * 1000, height * 1000 },
                        panelDistanceMeters = panel ? Vector3.Distance(head, panel.transform.position) : distance,
                        controlDistanceMeters = distance,
                        approximateAngularDegrees = new[] { 2 * Mathf.Atan2(width, 2 * distance) * Mathf.Rad2Deg, 2 * Mathf.Atan2(height, 2 * distance) * Mathf.Rad2Deg },
                        projectedAngularDegrees = new[] { Vector3.Angle((corners[0] + corners[1]) * .5f - head, (corners[2] + corners[3]) * .5f - head), Vector3.Angle((corners[0] + corners[3]) * .5f - head, (corners[1] + corners[2]) * .5f - head) },
                        effectiveAlpha = rect.GetComponentsInParent<CanvasGroup>().Aggregate(1f, (alpha, group) => alpha * group.alpha),
                        interactable = button ? button.IsInteractable() : true
                    };
                }).ToArray();
        }

        private static int ReadDisplay()
        {
            var property = gameView.GetType().GetProperty("targetDisplay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null) return (int)property.GetValue(gameView);
            var field = gameView.GetType().GetField("m_TargetDisplay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingMemberException("GameView target display unavailable; no screenshot substitution allowed.");
            return (int)field.GetValue(gameView);
        }

        private static void SetDisplay(int display)
        {
            var property = gameView.GetType().GetProperty("targetDisplay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite) property.SetValue(gameView, display);
            else gameView.GetType().GetField("m_TargetDisplay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(gameView, display);
            if (ReadDisplay() != display) throw new InvalidOperationException("GameView did not switch display.");
            gameView.Focus(); gameView.Repaint();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions.Add(message);
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            while (routines.Count > 0)
            {
                try { (routines.Pop() as IDisposable)?.Dispose(); }
                catch (Exception cleanupError) { if (error == null) error = cleanupError; }
            }
            ResearchFlowVisualInput.Stop();
            Time.timeScale = originalTimeScale;
            if (researcherCanvas != null) researcherCanvas.enabled = oldResearcherEnabled;
            try { if (gameView != null) SetDisplay(oldDisplay); } catch (Exception restoreError) { if (error == null) error = restoreError; }
            Status = error == null ? "Passed" : "Failed";
            File.WriteAllText(LastEvidencePath, JsonConvert.SerializeObject(new
            {
                status = Status, language = requestedLanguage, order = requestedOrder.ToString(), technical_test = true, test_data = true,
                execution_environment = "EditorOffline", hardware_verified = false,
                input_evidence = "Actual Unity button/drag callbacks; context focus is explicitly staged and chip presses use an owned SimulatedController pointer ID. Existing Meta hand visuals use the opt-in visual fixture. DEMO_INPUT marks simulation; no physical ray-acquisition, tracked-hand, locomotion-hardware, or Quest verification.",
                screenshots_require_human_visual_inspection = true, participant_id = controller?.Record?.participant_id,
                lastArrivalCaptureEvidence,
                raw_log = controller?.Logger?.EventsPath, exported_bundle = controller?.LastExportPath, export_scope = "Local immutable snapshot verification; PC delivery/QC is a separate check",
                canvas_cost = canvasCost, assertions, screenshots, error = error?.ToString()
            }, Formatting.Indented));
            if (error == null) Debug.Log("RESEARCH_FLOW_PLAY_CHECKS_PASS " + LastEvidencePath);
            else Debug.LogError("RESEARCH_FLOW_PLAY_CHECKS_FAILED " + LastEvidencePath + "\n" + error);
        }
    }
}
#endif
