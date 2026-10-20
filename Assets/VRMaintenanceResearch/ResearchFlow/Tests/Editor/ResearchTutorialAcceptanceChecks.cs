#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Oculus.Interaction.Locomotion;
using Unity.XR.CoreUtils;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchTutorialAcceptanceChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden).GetValue(owner);
        static void Set(object owner, string name, object value) => owner.GetType().GetProperty(name).GetSetMethod(true).Invoke(owner, new[] { value });
        static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Hidden).Invoke(owner, args);
        static void Require(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); }

        internal sealed class Fixture : IDisposable
        {
            public readonly GameObject root;
            readonly GameObject inputs;
            readonly string previousPolicy = ResearchUIPolicy.Active;
            static void ApplyPolicy(string policy) => typeof(ResearchUIPolicy).GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { policy });
            public readonly ResearchSessionFlowController controller;
            public readonly ResearchLocomotionEvents locomotion;
            public readonly HandsLabObject item;
            public readonly HandsLabInputGuard guard;
            public readonly Transform head;
            public HandsLabLogger logger;
            public readonly HashSet<string> facts;
            readonly HashSet<int> held;
            readonly Quaternion facing;
            readonly Scene scene;
            readonly string directory;

            public Fixture(string directory, int group, params string[] credited)
                : this(directory, group, false, credited) { }

            internal Fixture(string directory, int group, bool bindRun, params string[] credited)
            {
                this.directory = directory;
                var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
                if (!donor) throw new InvalidOperationException("Load ResearchFlow scene with its fonts before running the callback fixture.");
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("TutorialAcceptance_CallbackFixture") { hideFlags = HideFlags.HideAndDontSave };
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                try
                {
                controller = root.AddComponent<ResearchSessionFlowController>();
                var originRoot = new GameObject("FixtureOrigin"); originRoot.transform.SetParent(root.transform);
                controller.origin = originRoot.AddComponent<XROrigin>();
                var headRoot = new GameObject("FixtureHead"); headRoot.transform.SetParent(originRoot.transform);
                head = headRoot.transform;
                head.SetPositionAndRotation(new Vector3(20, 1.65f, 20), Quaternion.Euler(0, 180, 0));
                facing = head.rotation;
                controller.origin.Camera = headRoot.AddComponent<Camera>();
                controller.origin.Camera.fieldOfView = 100; controller.origin.Camera.aspect = 1;
                controller.view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                controller.view.controller = controller; controller.view.Head = head;
                foreach (var panel in new[] { controller.view.mainPanel, controller.view.referencePanel })
                    typeof(ResearchPanelMotion).GetField("environment", Hidden).SetValue(panel, Array.Empty<Renderer>());
                guard = root.AddComponent<HandsLabInputGuard>(); guard.side = Handedness.Right;
                Set(guard, "Mode", HandsLabInputGuard.InputMode.Controller);
                Set(guard, "TrackingValid", true); Set(guard, "WaitingForNeutral", false); Set(guard, "InteractionsAllowed", true);
                var left = root.AddComponent<HandsLabInputGuard>(); left.side = Handedness.Left;
                Set(left, "Mode", HandsLabInputGuard.InputMode.Controller);
                Set(left, "TrackingValid", true); Set(left, "WaitingForNeutral", false); Set(left, "InteractionsAllowed", true);
                controller.guards = new[] { left, guard };
                // A disabled controller reads as unknown, not neutral (HandsLabInputGuard.ControllerNeutral), and holds every
                // panel placement as interaction_busy. The inputs live outside the inactive controller root so they stay enabled.
                inputs = new GameObject("FixtureControllerInputs") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(inputs, scene);
                foreach (var owner in controller.guards)
                {
                    var input = inputs.AddComponent<RecoveryControllerFixture>(); input.side = owner.side;
                    owner.controllerReference = input;
                    typeof(HandsLabInputGuard).GetField("controller", Hidden).SetValue(owner, input);
                    typeof(HandsLabInputGuard).GetField("started", Hidden).SetValue(owner, true);
                }
                var registry = new ResearchParticipantRegistry(Path.Combine(directory, "registry"));
                var id = registry.CreateNext(ParticipantGroup.Dev).participant_id;
                Set(controller, "Registry", registry);
                Set(controller, "Record", registry.StartOrResume(id, ParticipantLanguage.Thai, ExperimentInputMode.Controller));
                // StartSession applies the recorded policy (ResearchSessionFlowController.StartSession); a DEV record is DevMovable.
                ApplyPolicy(ResearchUIPolicy.ForRecord(controller.Record.ui_policy));
                Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, group));
                if (bindRun) Set(controller, "Record", registry.SaveProgress(controller.Record, controller.Flow));
                logger = new HandsLabLogger(directory, "None", "Actual Editor callback fixture", executionEnvironment: "Editor", technicalTest: true, testData: true,
                    phase: bindRun ? "ResearchSession" : "HandsLab", taskId: bindRun ? "session" : "handslab", runId: bindRun ? controller.Record.run_id : null);
                Set(controller, "Logger", logger);
                logger.WriteFailed += message => Call(controller, "LoggerFailed", message);
                if (bindRun) Require(controller.Log("SESSION_CONFIGURED", "session", "Ready", after: controller.Record, source: "System"), logger.LastError);
                typeof(ResearchSessionFlowController).GetField("interactionAllowed", Hidden).SetValue(controller, true);
                var objectRoot = new GameObject("TrainingFixtureObject"); objectRoot.transform.SetParent(root.transform);
                item = objectRoot.AddComponent<HandsLabObject>(); item.stableId = "fixture.training_object";
                item.body = objectRoot.AddComponent<Rigidbody>(); item.grabbable = objectRoot.AddComponent<Grabbable>();
                item.transform.position = head.position + head.forward;
                controller.trainingObjects = new[] { item };
                held = Field<HashSet<int>>(item, "pointers");
                facts = Field<HashSet<string>>(controller, "tutorialEvidence");
                foreach (string fact in credited) facts.Add(fact);
                locomotion = root.AddComponent<ResearchLocomotionEvents>(); locomotion.controller = controller;
                controller.view.mainPanel.Recenter(false, false);
                }
                catch
                {
                    if (inputs) UnityEngine.Object.DestroyImmediate(inputs);
                    ApplyPolicy(previousPolicy); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); throw;
                }
            }
            public void Present()
            {
                controller.view.Refresh(); controller.view.mainPanel.Recenter(false, false);
                // Read before IsReadableFromHead below overwrites it with the unplaced pose's rejection.
                string placement = controller.view.mainPanel.LastRejection;
                Canvas.ForceUpdateCanvases(); controller.TutorialPromptShown();
                Require(controller.view.TutorialActionReady(controller.TutorialFact), "Fixture prompt was not actually visible before the action. fact=" + controller.TutorialFact
                    + " shown=" + Field<string>(controller.view, "shownFact") + " blocked=" + controller.view.mainPanel.PlacementBlocked
                    + " placement=" + placement + " policy=" + ResearchUIPolicy.Active + " panel=" + controller.view.mainPanel.transform.position
                    + " readable=" + controller.view.mainPanel.IsReadableFromHead + " rejection=" + controller.view.mainPanel.LastRejection
                    + " flow=" + controller.Flow.State + " paused=" + controller.Flow.Paused + " record=" + controller.Record.session_state
                    + " logger=" + logger.State + " loggerError=" + logger.LastError + " error=" + controller.Error + " floor=" + controller.FloorReady
                    + " editorFixture=" + ResearchFloorPolicy.EditorFixtureAllowed + " reference=" + controller.ReferenceOpen
                    + " body=" + Field<TMPro.TMP_Text>(controller.view, "body").gameObject.activeSelf
                    + " main=" + Field<GameObject>(controller.view, "mainContent").activeSelf
                    + " alpha=" + Field<CanvasGroup>(controller.view, "mainGroup").alpha
                    + " title=" + Field<TMPro.TMP_Text>(controller.view, "title").text);
            }
            public void LookAway() { head.rotation = facing * Quaternion.Euler(65, 150, 0); controller.TutorialPromptShown(); }
            public void LookBack() { head.rotation = facing; controller.TutorialPromptShown(); }
            public void Held(bool value) { if (value) held.Add(731); else held.Clear(); }
            public void Pointer(PointerEventType type) => Call(controller, "ObjectPointer", item, new PointerEvent(731, type, new Pose(item.transform.position, item.transform.rotation)));
            public void Requested(LocomotionEvent evt) => Call(locomotion, "Requested", evt, guard, "fixture.teleport_anchor");
            public void Completed(LocomotionEvent evt) => Call(locomotion, "Completed", evt, Pose.identity);
            public void Tick() => Call(controller, "Update");
            internal void RestartSameRun()
            {
                logger.Dispose();
                Set(controller, "Record", controller.Registry.StartOrResume(controller.Record.participant_id, controller.Record.language, controller.Record.input_mode));
                var record = controller.Record;
                Set(controller, "Flow", new ResearchSessionFlow(record.task_order, record.flow_state, record.tutorial_completed, record.paused, record.inspect_passed));
                facts.Clear(); held.Clear();
                typeof(ResearchSessionFlowController).GetField("tutorialInstruction", Hidden).SetValue(controller, null);
                typeof(ResearchSessionFlowController).GetField("tutorialEpoch", Hidden).SetValue(controller, 0L);
                Set(controller, "Error", "");
                typeof(ResearchSessionFlowController).GetField("interactionAllowed", Hidden).SetValue(controller, true);
                foreach (var input in controller.guards) { Set(input, "InteractionsAllowed", true); Set(input, "WaitingForNeutral", false); Set(input, "TrackingValid", true); }
                logger = new HandsLabLogger(directory, "None", "Actual Editor callback fixture", executionEnvironment: "Editor", technicalTest: true, testData: true,
                    phase: "ResearchSession", taskId: "session", runId: record.run_id, resume: true);
                Set(controller, "Logger", logger);
                logger.WriteFailed += message => Call(controller, "LoggerFailed", message);
                Require(logger.State == HandsLabLogState.Active, "Actual logger could not resume: " + logger.LastError);
            }
            public JObject[] Events()
            {
                var lines = new List<JObject>();
                using (var reader = new StreamReader(new FileStream(logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                    for (string line; (line = reader.ReadLine()) != null;) lines.Add(JObject.Parse(line));
                return lines.ToArray();
            }
            public void Dispose()
            {
                Set(controller, "Logger", null); logger.Dispose(); ApplyPolicy(previousPolicy); UnityEngine.Object.DestroyImmediate(inputs); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static string Run(string evidenceDirectory)
        {
            evidenceDirectory = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(evidenceDirectory);
            var results = new List<object>(); int passed = 0, failed = 0;
            void Case(string name, int group, string[] credited, Action<Fixture> action)
            {
                try
                {
                    using (var f = new Fixture(Path.Combine(evidenceDirectory, name), group, credited)) action(f);
                    results.Add(new { name, passed = true, detail = "Observed expected state transition in actual C# controller and adapter callbacks." }); passed++;
                }
                catch (Exception ex)
                {
                    while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                    results.Add(new { name, passed = false, detail = ex.Message, exception = ex.ToString() }); failed++;
                }
            }
            Case("teleport_look_away_commit_once", 0, Array.Empty<string>(), f =>
            {
                f.Present(); var e = new LocomotionEvent(731, Vector3.forward, LocomotionEvent.TranslationType.Absolute);
                f.Requested(e); f.LookAway(); f.Completed(e); f.Completed(e);
                Require(f.controller.TutorialFact == "snap" && f.facts.SetEquals(new[] { "teleport" }),
                    "Committed teleport after looking away must credit once. Observed fact=" + f.controller.TutorialFact + " credited=" + string.Join(",", f.facts));
                Require(f.Events().Count(x => (string)x["event"] == "TELEPORT_COMPLETED") == 1, "Duplicate SDK completion must not emit a second committed event.");
            });
            Case("snap_callback_outside_view", 0, new[] { "teleport" }, f =>
            {
                f.Present(); var e = new LocomotionEvent(731, Quaternion.Euler(0, 45, 0), LocomotionEvent.RotationType.Relative);
                f.Requested(e); f.LookAway(); f.Completed(e);
                Require(f.controller.TutorialFact == "roomscale" && f.facts.Contains("snap"), "Valid snap callback outside view must credit. Observed fact=" + f.controller.TutorialFact);
            });
            Case("lift_baseline_survives_gaze_return", 2, new[] { "grab" }, f =>
            {
                f.Held(true); f.Present(); var baselines = Field<Dictionary<HandsLabObject, Vector3>>(f.controller, "grabPositions");
                var start = baselines[f.item]; f.LookAway(); f.item.transform.position += Vector3.up * .12f; f.LookBack();
                Require(baselines[f.item] == start, "Gaze return must not reset Lift baseline. Original=" + start.y + " observed=" + baselines[f.item].y);
                f.Tick(); Require(f.facts.Contains("move"), "A single lift over .08m must receive credit after gaze return.");
            });
            Case("roomscale_baseline_survives_gaze_return", 0, new[] { "teleport", "snap" }, f =>
            {
                f.Present(); var start = Field<Vector3>(f.controller, "physicalHeadStart");
                f.LookAway(); f.head.localPosition += Vector3.right * .2f; f.LookBack();
                Require(Field<Vector3>(f.controller, "physicalHeadStart") == start, "Gaze return must retain roomscale baseline, not move the goal.");
            });
            Case("early_release_retains_prior_facts", 2, new[] { "grab", "move" }, f =>
            {
                f.Held(true); f.Present(); f.Pointer(PointerEventType.Select); f.item.transform.rotation *= Quaternion.Euler(0, 35, 0); f.Tick();
                f.LookAway(); f.Held(false); f.Pointer(PointerEventType.Unselect);
                Require(f.controller.Flow.TutorialCompleted >= 3 || (f.facts.Contains("grab") && f.facts.Contains("move") && f.facts.Contains("rotate")),
                    "Quick release must finalize the handling group or retain valid prior handling skills.");
                Require(f.Events().Count(row => (string)row["event"] == "TUTORIAL_SKILL_CREDIT" && (string)row["target"] == "training.rotate") <= 1,
                    "One successful rotation cannot receive duplicate durable credits.");
                Require(f.controller.Flow.TutorialCompleted == 3 || (f.controller.TutorialFact == "release" && f.controller.TutorialNeedsGrab),
                    "Quick release must either accept an already armed release or request only the missing skill.");
            });
            Case("request_before_prompt_cannot_replay", 0, Array.Empty<string>(), f =>
            {
                var e = new LocomotionEvent(731, Vector3.forward, LocomotionEvent.TranslationType.Absolute);
                f.Requested(e); f.Present(); f.Completed(e);
                Require(!f.facts.Contains("teleport"), "Request before instruction arm must not credit after the prompt appears.");
            });
            Case("pending_before_pause_cannot_resume_credit", 0, Array.Empty<string>(), f =>
            {
                f.Present(); var e = new LocomotionEvent(731, Vector3.forward, LocomotionEvent.TranslationType.Absolute); f.Requested(e);
                Call(f.controller, "Pause", "participant"); f.controller.Flow.ResearcherResume(); f.Present(); f.Completed(e);
                Require(!f.facts.Contains("teleport"), "Pre-pause pending request must not credit on a resumed instruction.");
            });
            Case("tracking_loss_pending_commit_rejected", 0, Array.Empty<string>(), f =>
            {
                f.Present(); var e = new LocomotionEvent(731, Vector3.forward, LocomotionEvent.TranslationType.Absolute); f.Requested(e);
                Set(f.guard, "TrackingValid", false); f.Completed(e);
                Require(!f.facts.Contains("teleport"), "Untracked native commitment must not receive tutorial credit.");
            });
            Case("wrong_fact_and_below_threshold_rejected", 2, new[] { "grab" }, f =>
            {
                f.Held(true); f.Present(); f.controller.Evidence("rotate"); f.item.transform.position += Vector3.up * .03f; f.Tick();
                Require(f.facts.SetEquals(new[] { "grab" }) && f.controller.TutorialFact == "move", "Wrong skill and below-threshold movement must not credit.");
            });
            string summary = "TUTORIAL_ACCEPTANCE_" + (failed == 0 ? "PASS" : "FAIL") + " passed=" + passed + " failed=" + failed;
            File.WriteAllText(Path.Combine(evidenceDirectory, "result.json"), JsonConvert.SerializeObject(new { summary, passed, failed, results,
                hardware_verified = false, scope = "Inactive actual Unity controller/view/native adapter callback fixture; no wearer input or physical SDK acquisition claim." }, Formatting.Indented));
            return summary;
        }
    }
}
#endif
