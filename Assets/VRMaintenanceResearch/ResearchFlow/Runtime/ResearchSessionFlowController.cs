using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Locomotion;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow
{
    // One owner for session state. The existing logger and SDK still own file format and interaction physics.
    public sealed class ResearchSessionFlowController : MonoBehaviour
    {
        public XROrigin origin;
        public ResearchFlowView view;
        public HandsLabInputGuard[] guards = Array.Empty<HandsLabInputGuard>();
        public HandsLabObject[] trainingObjects = Array.Empty<HandsLabObject>();
        public ResearchTaskPort[] taskPorts = Array.Empty<ResearchTaskPort>();
        public Transform[] stations = Array.Empty<Transform>();
        public FirstPersonLocomotor locomotor;
        public GameObject trainingRoot;
        public GameObject[] locomotionInputRoots = Array.Empty<GameObject>();
        public string registryRoot = "";
        public bool translationsReviewed;
        public ResearchParticipantRegistry Registry { get; private set; }
        public ParticipantRecord Record { get; private set; }
        public ResearchSessionFlow Flow { get; private set; }
        public HandsLabLogger Logger { get; private set; }
        public string Error { get; private set; } = "";
        public bool TaskStartConfirmation { get; private set; }
        public string ExportDeliveryError { get; private set; } = "";
        public string LastExportPath { get; private set; } = "";
        // Snapshots run on the export worker; results are applied here on the main thread, in request order.
        readonly List<(System.Threading.Tasks.Task<string> task, string run)> pendingExports = new List<(System.Threading.Tasks.Task<string>, string)>();
        public bool ExportPending => pendingExports.Count > 0;
        string exportStatusKey, exportStatusCache = "saved";
        double exportStatusCheckedAt = double.NegativeInfinity;
        public string ExportDeliveryStatus
        {
            get
            {
                if (!string.IsNullOrEmpty(ExportDeliveryError)) return "attention";
                // Raw data is already on the device; the newest snapshot is not yet a queued bundle.
                if (Record != null && pendingExports.Any(p => p.run == Record.run_id)) return "saved";
                string key = Record == null ? "" : Record.participant_id + "/" + Record.session_id + "/" + Record.run_id + "/" + Record.revision + "/" + LastExportPath;
                double now = Time.realtimeSinceStartupAsDouble;
                if (key != exportStatusKey || now < exportStatusCheckedAt || now - exportStatusCheckedAt >= 1)
                {
                    exportStatusKey = key; exportStatusCheckedAt = now;
                    exportStatusCache = ResearchExportQueue.DeliveryStatus(DataRoot, Record, LastExportPath);
                }
                return exportStatusCache;
            }
        }
        public ResearchFloorPolicy FloorPolicy { get; private set; }
        // Inactive Editor callback fixtures have no Awake/native XR; standalone always requires the real policy.
        public bool FloorReady => FloorPolicy ? FloorPolicy.Ready : IsTechnical && ResearchFloorPolicy.EditorFixtureAllowed;
        public bool DeviceReady => FloorReady && guards.Length == 2 && guards.All(g => g && g.TrackingValid);
        public bool LoggerReady => Registry != null && (Logger == null || Logger.State == HandsLabLogState.Active);
        // One reason string per researcher control so every operator surface shows the same why-not, mirroring StartSession/ResearcherResume gates.
        public string ResearcherStartBlocker(ParticipantRecord record) =>
            Record != null ? "A session is already open: " + Record.participant_id
            : record == null ? "Select or create a participant ID."
            : record.session_state != RegistryState.Available && record.session_state != RegistryState.InProgress ? record.participant_id + " is " + record.session_state + " and stays locked."
            : !string.IsNullOrEmpty(Error) ? Error
            : !FloorReady ? "Device not ready: floor/boundary is not confirmed."
            : !DeviceReady ? "Device not ready: both controllers must be tracked."
            : !LoggerReady ? "Data logger not ready." : null;
        public string ResearcherResumeBlocker()
        {
            if (Record == null || Flow == null) return "No open session.";
            if (Record.session_state != RegistryState.InProgress || Logger?.State != HandsLabLogState.Active) return "Session is " + Record.session_state + "; logger " + Logger?.State + ".";
            if (!string.IsNullOrEmpty(Error)) return Error;
            if (Transitioning) return "Station transition in progress.";
            bool checkpoint = Flow.State == ResearchFlowState.PostTask1Checkpoint || Flow.State == ResearchFlowState.PostTask2Checkpoint;
            if (!Flow.Paused && !checkpoint) return "Nothing to resume: participant is in " + Flow.State + ".";
            if (Flow.Paused && Flow.IsTaskActive && (applicationPaused || applicationUnfocused)) return "Participant app is not focused.";
            if (Flow.Paused && Flow.IsTaskActive && (!HeadsetReady || !DeviceReady)) return "Headset or controllers not tracked.";
            return null;
        }
        public string Language => Record?.language.ToString() ?? SetupLanguage.ToString();
        // In-headset technical self-setup exists only in the ".technical" package identity (same gate as the ADB channel).
        // Study builds keep researcher-controlled setup; this path only ever creates DEV test-data records.
        public static bool TechnicalBuild => Application.identifier.EndsWith(".technical", StringComparison.Ordinal);
        public bool TechnicalSelfSetup { get; private set; }
        // True only for a session started from the headset technical setup; enables tester-labelled checkpoint/demo controls.
        public bool TechnicalSelfTest { get; private set; }
        public ParticipantLanguage SetupLanguage { get; private set; } = ParticipantLanguage.Thai;
        public ExperimentInputMode SetupInput { get; private set; } = ExperimentInputMode.Controller;
        public TaskOrder SetupOrder { get; private set; } = TaskOrder.AB;
        public bool HeadsetReady
        {
            get
            {
                var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                bool tracked = hmd.isValid && hmd.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked;
                bool absent = hmd.isValid && hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool present) && !present;
                return (tracked && !absent) || ResearchFloorPolicy.EditorFixtureAllowed;
            }
        }
        public bool GuardTracked(Oculus.Interaction.Input.Handedness side) => guards.Any(g => g && g.side == side && g.TrackingValid);
        public bool TechnicalReady => TechnicalSelfSetup && Record == null && string.IsNullOrEmpty(Error) && HeadsetReady && DeviceReady && LoggerReady;
        public bool DemoPassSet => CurrentPort && CurrentPort.demoPassed;
        float nextSetupPoll; int setupSignature = -1; bool menuWasPressed;
        public bool IsTechnical => Record?.test_data ?? true;
        internal ResearchRenderExperiment RenderComparison { get; set; }
        bool RenderComparisonRunning => IsTechnical && RenderComparison && RenderComparison.Running;
        internal bool LocalUICommandsAllowed => Flow?.CanInteract == true && (!Flow.IsTaskActive || taskTiming.CanManipulate) && FloorReady && !Transitioning
            && string.IsNullOrEmpty(Error) && !RenderComparisonRunning
            && (Record == null || (Record.session_state == RegistryState.InProgress && Logger?.State == HandsLabLogState.Active))
            && (Logger == null || Logger.State == HandsLabLogState.Active);
        readonly ResearchTaskTiming taskTiming = new ResearchTaskTiming();
        object inspectionSnapshot;
        string inspectionId = "", inspectionInput = "None";
        double inspectionRequestedAt, inspectionEvaluatedAt;
        bool taskEndRecorded;
        bool applicationPaused, applicationUnfocused;
        public bool InspectionChecking => Flow?.IsTaskActive == true && !taskTiming.Ended && taskTiming.Stage == ResearchInspectionStage.Checking;
        public bool InspectionFeedbackVisible => taskTiming.Stage == ResearchInspectionStage.Visible;
        public string InspectionId => inspectionId;
        public string TaskEndReason => taskTiming.EndReason;
        public double TaskActiveElapsed => taskTiming.ActiveElapsed;
        public double TaskWallElapsed => taskTiming.WallElapsed;
        bool TaskContentAvailable => Flow?.IsTaskActive == true && taskTiming.Started && !taskTiming.Ended;
        public bool Transitioning { get; private set; }
        public string ReferenceSection { get; private set; } = ResearchReferenceScheme.Active[0];
        string referenceChoiceTask;
        public bool ReferenceOpen { get; private set; }
        public bool ReferenceMinimized { get; private set; }
        // Temporary participant pause vs system/safety pause. Technical sessions and the tutorial let the
        // participant continue; study tasks keep researcher-only resume (unchanged protocol).
        public string PauseKind { get; private set; } = "";
        public bool HelpRequested { get; private set; }
        public bool ParticipantMayResume => Flow != null && Flow.Paused && PauseKind != "stop" && string.IsNullOrEmpty(Error) &&
            (IsTechnical || (Flow.IsTutorial && PauseKind == "participant"));
        public bool ParticipantCanStop => Record?.session_state == RegistryState.InProgress && Flow != null
            && Flow.State > ResearchFlowState.ResearcherSetup && Flow.State < ResearchFlowState.ParticipantComplete;
        double pauseStart = double.NaN;
        readonly Dictionary<HandsLabObject, Action<PointerEvent>> objectListeners = new Dictionary<HandsLabObject, Action<PointerEvent>>();
        readonly Dictionary<ResearchTaskPort, Action<string, string, object, object>> portListeners = new Dictionary<ResearchTaskPort, Action<string, string, object, object>>();
        readonly Dictionary<HandsLabObject, Quaternion> grabRotations = new Dictionary<HandsLabObject, Quaternion>();
        readonly Dictionary<HandsLabObject, Vector3> grabPositions = new Dictionary<HandsLabObject, Vector3>();
        readonly Dictionary<HandsLabObject, object> lastHeldSnapshots = new Dictionary<HandsLabObject, object>();
        readonly Dictionary<HandsLabObject, string> grabSources = new Dictionary<HandsLabObject, string>();
        readonly Dictionary<(HandsLabObject item, int pointer), string> grabActions = new Dictionary<(HandsLabObject, int), string>();
        readonly HashSet<string> tutorialEvidence = new HashSet<string>();
        sealed class TutorialInstruction
        {
            public readonly string fact;
            public readonly long epoch;
            public TutorialInstruction(string fact, long epoch) { this.fact = fact; this.epoch = epoch; }
        }
        TutorialInstruction tutorialInstruction;
        long tutorialEpoch;
        public long TutorialInstructionEpoch => tutorialInstruction?.epoch ?? tutorialEpoch;
        public string TutorialArmedFact => tutorialInstruction?.fact;
        public string TutorialLastCompletedFact { get; private set; }
        public string TutorialLastCreditActionId { get; private set; }
        public long TutorialLastCreditEpoch { get; private set; }
        public int TutorialFactCount => TutorialFacts.Sum(facts => facts.Length);
        public int TutorialCompletedFactCount => Flow == null ? 0 : TutorialFacts.Take(Flow.TutorialCompleted).Sum(facts => facts.Length)
            + (Flow.TutorialCompleted < TutorialFacts.Length ? TutorialFacts[Flow.TutorialCompleted].Count(tutorialEvidence.Contains) : 0);
        // Instructions and completion use the same facts, including the room-scale action.
        static readonly string[][] BasicTutorialFacts = {
            new[] { "teleport", "snap", "roomscale" }, new[] { "select" },
            new[] { "grab", "move", "rotate", "release" },
            new[] { "reference", "switch", "reposition", "close" }, new[] { "inspect" }
        };
        // Fixed-position UI policy (ADVISOR_UI_AMENDMENT_20261014): panels cannot be dragged, so the drag lesson is
        // removed together with the drag interaction; open/switch/close Reader practice is unchanged.
        static readonly string[][] FixedBasicTutorialFacts = BasicTutorialFacts
            .Select(facts => facts.Where(fact => fact != "reposition").ToArray()).ToArray();
        // With the neutral Trainer_FX1 station the last group practises the S1 §4 mechanisms before the Inspect samples.
        static readonly string[][] TrainerTutorialFacts = WithTrainer(BasicTutorialFacts);
        static readonly string[][] FixedTrainerTutorialFacts = FixedBasicTutorialFacts.Take(4)
            .Concat(new[] { ResearchTrainingStation.FixedSequence.ToArray() }).ToArray();
        static string[][] WithTrainer(string[][] basic) => basic.Take(4)
            .Concat(new[] { ResearchTrainingStation.Facts.Concat(new[] { "inspect" }).ToArray() }).ToArray();
        string[][] TutorialFacts => ResearchUIPolicy.TrainingIncludesPanelDrag
            ? (trainingStation ? TrainerTutorialFacts : BasicTutorialFacts)
            : (trainingStation ? FixedTrainerTutorialFacts : FixedBasicTutorialFacts);
        /// <summary>True when the current policy's training requires this practice fact (e.g. "reposition" only in the legacy policy).</summary>
        public bool TutorialRequires(string fact) => TutorialFacts.Any(facts => facts.Contains(fact));
        public ResearchTrainingStation trainingStation;
        public bool TutorialUsesHands => Record?.input_mode == ExperimentInputMode.HandTracking ||
            (Record?.input_mode == ExperimentInputMode.TechnicalBoth && guards.Any(g => g && g.TrackingValid && g.Mode == HandsLabInputGuard.InputMode.TrackedHand));
        public bool TutorialNeedsRelease => Flow?.TutorialCompleted == 2 && TutorialFact == "grab" && trainingObjects.Any(o => o && o.IsHeld);
        public bool TutorialNeedsGrab => Flow?.TutorialCompleted == 2 && TutorialFact != "grab" && !trainingObjects.Any(o => o && o.IsHeld);
        public string TutorialInstructionFact => TutorialNeedsRelease ? "release" : TutorialNeedsGrab ? "grab" : TutorialFact;
        bool lastTutorialHands;
        // The single action the tutorial coach shows now; null once the tutorial has nothing left to try.
        public string TutorialFact
        {
            get
            {
                if (Flow?.IsTutorial != true || Flow.TutorialCompleted >= TutorialFacts.Length) return null;
                string fact = TutorialFacts[Flow.TutorialCompleted].FirstOrDefault(f => !tutorialEvidence.Contains(f));
                if (fact == null) return null;
                if (Flow.TutorialCompleted == 3 && ReferenceOpen && ReferenceMinimized) return "restore";
                return Flow.TutorialCompleted == 3 && !ReferenceOpen ? "reference" : fact;
            }
        }
        public string TutorialInstructionKey
        {
            get
            {
                string fact = TutorialFact;
                if (fact == null) return "tutorial.complete.body";
                if (TutorialNeedsGrab) return "tutorial.try.regrab" + (TutorialUsesHands ? ".hand" : ".controller");
                if (fact == "restore") return "tutorial.try.restore";
                if (fact == "inspect" && trainingStation)
                    return ResearchUIPolicy.TrainingIncludesPanelDrag ? "tutorial.try.inspect.trainer" : "tutorial.try.inspect.trainer_ready";
                if (fact == "inspect_fail" && !ResearchUIPolicy.TrainingIncludesPanelDrag) return "tutorial.try.inspect_fail.start";
                string key = "tutorial.try." + fact;
                return fact == "teleport" || fact == "snap" || fact == "select" || fact == "grab" || fact == "release" || fact == "reposition"
                    ? key + (TutorialUsesHands ? ".hand" : ".controller") : key;
            }
        }
        readonly List<MonoBehaviour> pointerComponents = new List<MonoBehaviour>();
        string exposureAction, exposureSource;
        string exposureSelectionKind = "automatic_default";
        string exposurePhase, exposureTask;
        double exposureStart;
        bool interactionAllowed, shuttingDown;
        bool referenceNavigationHeld;
        GameObject[] locomotionGates = Array.Empty<GameObject>();
        ResearchWorldInteractionGate[] worldGates = Array.Empty<ResearchWorldInteractionGate>();
        public bool ReferenceNavigationInputAvailable => guards.Any(g => g && g.side == Oculus.Interaction.Input.Handedness.Right && g.TrackingValid && g.Mode != HandsLabInputGuard.InputMode.None);
        public Vector2 ReferenceScrollAxis
        {
            get
            {
                foreach (var guard in guards)
                    if (guard && guard.side == Oculus.Interaction.Input.Handedness.Right && guard.TrackingValid &&
                        guard.Mode == HandsLabInputGuard.InputMode.Controller && guard.controllerReference is Oculus.Interaction.Input.IController input)
                        return input.ControllerInput.Primary2DAxis;
                return Vector2.zero;
            }
        }
        readonly List<Oculus.Interaction.Grabbable> placementObjects = new List<Oculus.Interaction.Grabbable>();
        ResearchLocomotionEvents placementLocomotion;
        Oculus.Interaction.Grabbable placementHeldObject;
        public bool PanelPlacementBusy
        {
            get
            {
                if (!placementLocomotion) placementLocomotion = GetComponent<ResearchLocomotionEvents>();
                if (placementLocomotion && placementLocomotion.PlacementBusy) return true;
                foreach (var guard in guards)
                {
                    if (!guard || guard.Mode == HandsLabInputGuard.InputMode.None) continue;
                    if (!guard.InputNeutral) return true;
                    if (guard.Mode != HandsLabInputGuard.InputMode.Controller || !(guard.controllerReference is Oculus.Interaction.Input.IController input)) continue;
                    var value = input.ControllerInput;
                    if (value.Primary2DAxis.sqrMagnitude >= .04f || value.Secondary2DAxis.sqrMagnitude >= .04f
                        || value.Trigger >= guard.analogReleaseThreshold || value.Grip >= guard.analogReleaseThreshold
                        || value.PrimaryButton || value.SecondaryButton || value.TriggerButton || value.GripButton
                        || value.MenuButton || value.Primary2DAxisClick) return true;
                }
                if (trainingObjects.Any(item => item && item.IsHeld)) return true;
                if (placementHeldObject && placementHeldObject.gameObject.activeInHierarchy && placementHeldObject.GrabPoints?.Count > 0) return true;
                placementHeldObject = null;
                foreach (var root in gameObject.scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(false, placementObjects);
                    foreach (var item in placementObjects)
                        if (item && item.GrabPoints?.Count > 0) { placementHeldObject = item; return true; }
                }
                return false;
            }
        }
        Vector3 physicalHeadStart;
        // Lost-object recovery reference: real Training room geometry, found once. Missing = disabled.
        Bounds recoveryRoom; float recoveryFloorTop; Transform recoveryEnvironment; bool recoveryReady;
        readonly Dictionary<HandsLabObject, float> lostSince = new Dictionary<HandsLabObject, float>();
        readonly Collider[] recoveryHits = new Collider[8];
        string DataRoot => string.IsNullOrWhiteSpace(registryRoot) ? Path.Combine(Application.persistentDataPath, "ResearchSessionRegistry") : registryRoot;
        ResearchTaskPort CurrentPort => taskPorts.FirstOrDefault(t => t && t.taskId == Flow?.CurrentTask);
        public string InspectionFeedbackKey => CurrentPort?.FeedbackKey;

        void Awake()
        {
            worldGates = guards.Where(g => g).Select(g =>
            {
                var gate = g.gameObject.AddComponent<ResearchWorldInteractionGate>();
                gate.Configure(g); return gate;
            }).ToArray();
            // Meta owns each native root's active state (ray hover / input mode).
            // Gate its parent so SDK state changes cannot bypass session or Reference safety.
            locomotionGates = new GameObject[locomotionInputRoots.Length];
            for (int i = 0; i < locomotionInputRoots.Length; i++)
            {
                var inputRoot = locomotionInputRoots[i];
                if (!inputRoot) continue;
                var gate = new GameObject("ResearchLocomotionGate");
                gate.SetActive(false);
                gate.transform.SetParent(inputRoot.transform.parent, false);
                gate.transform.SetSiblingIndex(inputRoot.transform.GetSiblingIndex());
                inputRoot.transform.SetParent(gate.transform, false);
                locomotionGates[i] = gate;
            }
            try { Registry = new ResearchParticipantRegistry(DataRoot); }
            catch (Exception ex) { Error = "Registry unavailable: " + ex.Message; }
            FloorPolicy = GetComponent<ResearchFloorPolicy>() ?? gameObject.AddComponent<ResearchFloorPolicy>();
            FloorPolicy.Configure(origin);
            FloorPolicy.Changed += FloorReadinessChanged;
            if (view) { view.controller = this; view.Head = origin ? origin.Camera.transform : null; }
            foreach (var item in trainingObjects)
            {
                item.Initialize(null); // Preserve HandsLab's velocity sampler and SDK grab implementation.
                var captured = item;
                Action<PointerEvent> listener = e => ObjectPointer(captured, e);
                item.grabbable.WhenPointerEventRaised += listener;
                objectListeners.Add(item, listener);
            }
            foreach (var port in taskPorts)
                if (port)
                {
                    var capturedPort = port;
                    Action<string, string, object, object> listener = (name, target, before, after) =>
                    { if (capturedPort == CurrentPort || (after as ResearchTaskPort.AttemptPayload)?.SystemTerminal == true) TaskBehavior(name, target, before, after); };
                    port.Behavior += listener; portListeners.Add(port, listener);
                }
            // Technical self-setup keeps the guards' own tracking/neutral gates so the tester can ray-select Start;
            // Refresh() below still disables objects, locomotion and task ports until the session exists.
            TechnicalSelfSetup = TechnicalBuild;
            foreach (var guard in guards) if (guard)
            {
                guard.StateChanged += TutorialInputChanged;
                if (TechnicalSelfSetup) guard.policy = HandsLabInputGuard.ModePolicy.Controllers;
                else guard.Suspend("researcher_setup");
            }
            var environment = gameObject.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Environment_V2");
            var floor = environment ? environment.transform.Find("Training/Training_Floor")?.GetComponent<Collider>() : null;
            var shell = environment ? environment.transform.Find("Training/Training_WallShell")?.GetComponent<Collider>() : null;
            if (floor && shell) ConfigureRecovery(shell.bounds, floor.bounds.max.y, environment.transform);
        }

        public void ConfigureRecovery(Bounds room, float floorTop, Transform environment)
        {
            recoveryRoom = room; recoveryFloorTop = floorTop; recoveryEnvironment = environment;
            // Fail closed: an authored pose that already counts as lost would reset in a loop.
            recoveryReady = trainingObjects.All(t => !t || ResearchObjectRecovery.Reason(t.transform.position, room, floorTop, false) == null);
        }

        void Start()
        {
            ResearchMovementSampler.Install(this);
            if (view) view.ReferenceVisibilityChanged += ReferenceVisibilityChanged;
            ResearchHandContactProxy.Install(this);
            ResearchHeldObjectConstraint.Install(this);
            if (TechnicalSelfSetup) Refresh(); else view?.Refresh();
        }

        void FloorReadinessChanged()
        {
            if (shuttingDown) return;
            if (Flow?.IsTaskActive == true && !FloorReady) SuspendTaskForTracking("floor_reference_unconfirmed");
            if (Flow != null && Record != null) Refresh();
            else view?.Refresh();
        }

        public void StartSession(string id, ParticipantLanguage language, ExperimentInputMode input, TaskOrder? order = null, string layout = null, string uiPolicy = null)
        {
            if (Record != null) { Error = "A session is already open. End it before opening another."; return; }
            TaskStartConfirmation = false;
            try
            {
                if (Registry == null) throw new InvalidOperationException("Registry not ready");
                var selected = Enum.GetValues(typeof(ParticipantGroup)).Cast<ParticipantGroup>().SelectMany(Registry.List).Single(p => p.participant_id == id);
                bool technical = ResearchParticipantRegistry.IsTechnical(selected.group);
                bool resume = selected.session_state == RegistryState.InProgress;
                if (!technical && (!DeviceReady || !translationsReviewed || taskPorts.Length != 2 || taskPorts.Any(t => !t || !t.IsReadyForStudy)
                    || taskPorts.Select(t => t.taskId).Distinct().Count() != 2))
                    throw new InvalidOperationException("Study requires tracked device, bilingual review and two approved task adapters. Use DEV/TEST for the integration demo.");
                Record = Registry.StartOrResume(id, language, input, order, layoutOverride: layout, uiPolicy: uiPolicy);
                // A resumed record keeps the policy it started with (absent = earlier movable-panel build), so training facts,
                // drag availability and analysis grouping never mix policies within one session.
                ResearchUIPolicy.Apply(ResearchUIPolicy.ForRecord(Record.ui_policy));
                policyAttribution = Record.ui_policy != null ? "recorded_at_session_start" : resume ? "unrecorded" : "unrecorded_new_session";
                foreach (var task in FindObjectsByType<ResearchS1TaskAMechanics>(FindObjectsSortMode.None))
                    task.ApplyLayout(Record.task_a_layout ?? task.layout);
                Flow = new ResearchSessionFlow(Record.task_order, Record.flow_state, Record.tutorial_completed, Record.paused, Record.inspect_passed);
                foreach (var port in taskPorts) if (port) port.demoMode = Record.test_data;
                foreach (var g in guards) g.policy = Record.input_mode == ExperimentInputMode.Controller ? HandsLabInputGuard.ModePolicy.Controllers :
                    Record.input_mode == ExperimentInputMode.HandTracking ? HandsLabInputGuard.ModePolicy.TrackedHands : HandsLabInputGuard.ModePolicy.Auto;
                var logRoot = Path.Combine(DataRoot, "Logs");
                Logger = new HandsLabLogger(logRoot, "None", "UnityXR / OpenXR", executionEnvironment: Application.isEditor ? "Editor" : "QuestStandalone",
                    phase: "ResearchSession", taskId: "session", contentVersion: "research.flow.1", technicalTest: Record.test_data, testData: Record.test_data,
                    runId: Record.run_id, resume: resume);
                Logger.WriteFailed += LoggerFailed;
                if (Logger.State != HandsLabLogState.Active) throw new IOException(Logger.LastError);
                if (resume && Record.ui_policy == null)
                {
                    // Sidecar entry lost (e.g. a copied registry): the session's own log still states the policy it started with.
                    string recovered = RecordedPolicyFromEvents();
                    if (recovered != null) { ResearchUIPolicy.Apply(recovered); policyAttribution = "recovered_from_session_event_log"; }
                    else policyAttribution = "unrecorded_runtime_keeps_movable_panels_for_continuity";
                }
                if (resume && !RestoreTutorialCredits()) return;
                Log(resume ? "SESSION_RESUMED" : "SESSION_CONFIGURED", "session", "Ready", after: Record, source: "System");
                Log("SESSION_PROTOCOL_VERSIONS", "session", ResearchUIPolicy.Active, after: new { ui_policy = ResearchUIPolicy.Active,
                    recorded_ui_policy = Record.ui_policy, protocol_version = Record.protocol_version ?? ResearchUIPolicy.ProtocolFor(ResearchUIPolicy.Active),
                    training_version = Record.training_version ?? ResearchUIPolicy.TrainingFor(ResearchUIPolicy.Active),
                    layout_version = Record.layout_version, task_a_layout = Record.task_a_layout,
                    panel_drag_allowed = ResearchUIPolicy.PanelDragAllowed, participant_recenter_allowed = ResearchUIPolicy.ParticipantRecenterAllowed,
                    tutorial_facts = TutorialFacts, attribution = policyAttribution }, source: "System", reason: policyAttribution);
                if (!resume && Record.task_order_overridden) Log("TASK_ORDER_OVERRIDE", "session.task_order", Record.task_order.ToString(), source: "System");
                Error = "";
                if (resume && Flow.IsTaskActive)
                {
                    Pause("restored_session"); Save();
                    Error = "Active task resume requires verified physical state and active-clock restoration. No task state or deadline was reset. Contact the researcher.";
                    BlockInteractions("task_restore_required"); view?.Refresh(); return;
                }
                StartCoroutine(MoveToStation(Flow.IsTutorial ? 0 : Flow.CurrentTask == "A" ? 1 : 2));
                Refresh();
            }
            catch (Exception ex) { Error = ex.Message; BlockInteractions("session_start_error"); view?.Refresh(); }
        }

        bool SetupCommand(string command, string inputSource)
        {
            if (!TechnicalSelfSetup || Record != null || Transitioning || !string.IsNullOrEmpty(Error)) return false;
            switch (command)
            {
                case "technical_language": SetupLanguage = SetupLanguage == ParticipantLanguage.Thai ? ParticipantLanguage.Japanese : ParticipantLanguage.Thai; break;
                case "technical_order": SetupOrder = SetupOrder == TaskOrder.AB ? TaskOrder.BA : TaskOrder.AB; break;
                case "technical_start": return StartTechnicalSelfTest(inputSource);
                default: return false;
            }
            view?.Refresh();
            return string.IsNullOrEmpty(Error);
        }

        // Next DEV id + the existing StartSession path. Readiness is re-checked here, not only in the UI.
        public bool StartTechnicalSelfTest(string inputSource = "None")
        {
            if (!TechnicalReady) return false;
            string id;
            try { id = Registry.CreateNext(ParticipantGroup.Dev).participant_id; }
            catch (Exception ex) { Error = "Registry unavailable: " + ex.Message; view?.Refresh(); return false; }
            StartSession(id, SetupLanguage, SetupInput, SetupOrder);
            if (Flow == null || !string.IsNullOrEmpty(Error)) return false;
            TechnicalSelfTest = true;
            Log("TECHNICAL_SELF_START", "session", "Started", after: new { participant_id = id, language = SetupLanguage.ToString(),
                input_mode = SetupInput.ToString(), task_order = SetupOrder.ToString(), source = "HeadsetTechnicalSetup" }, source: "Tester", inputSource: inputSource);
            view?.Refresh();
            return string.IsNullOrEmpty(Error);
        }

        internal void ConfigureTechnicalRegistry(string isolatedRoot)
        {
            if (!TechnicalBuild || Record != null || Flow != null) throw new InvalidOperationException("Technical fixture cannot replace an existing session.");
            registryRoot = isolatedRoot;
            Registry = new ResearchParticipantRegistry(DataRoot);
        }

        // Shared dispatch reports an accepted state change/request for UI feedback, never evaluator correctness.
        public bool Command(string command, string inputSource = "None")
        {
            if (command == "stop") return Stop(inputSource, "User", "Paused", "participant_stop");
            if (Flow == null) return SetupCommand(command, inputSource);
            if (Record != null && (Record.session_state != RegistryState.InProgress || Logger?.State != HandsLabLogState.Active)) return false;
            if (Logger != null && !LoggerReady) return false;
            if (Transitioning || !string.IsNullOrEmpty(Error)) return false;
            AdvanceTaskTiming();
            if (!string.IsNullOrEmpty(Error)) return false;
            if (command == "pause")
            {
                CloseReference("safety_pause", "System");
                if (!Pause("participant")) return false;
                Log("SESSION_PAUSED", "safety", "Paused", after: PauseTiming(), source: "User", inputSource: inputSource, reason: "participant_pause"); Save(); Refresh();
                return string.IsNullOrEmpty(Error);
            }
            if (RenderComparisonRunning && command != "help_request") return false;
            if (command == "session_resume") return ParticipantMayResume && Resume("ParticipantResumed", "User", inputSource);
            if (command == "help_request")
            {
                if (!Flow.Paused || HelpRequested || !Log("HELP_REQUESTED", "safety", "Requested", inputSource: inputSource)) return false;
                HelpRequested = true; view?.Refresh();
                return string.IsNullOrEmpty(Error);
            }
            if (Flow.Paused) return false;
            bool changed = false, accepted = false;
            switch (command)
            {
                case "tutorial_start": changed = Flow.StartTutorial(); if (changed && origin && origin.Camera) physicalHeadStart = origin.Camera.transform.localPosition; break;
                case "tutorial_select": if (Flow.TutorialCompleted == 1) accepted = Evidence("select", inputSource); break;
                case "tutorial_continue":
                    if (Flow.State == ResearchFlowState.TutorialComplete) CloseReference("tutorial_complete", "System");
                    changed = Flow.Continue(); if (changed) StartCoroutine(MoveToStation(Flow.CurrentTask == "A" ? 1 : 2)); break;
                case "task_start":
                case "task_start_confirm":
                    if (Flow.State != ResearchFlowState.Task1Ready && Flow.State != ResearchFlowState.Task2Ready) break;
                    if (Logger?.State != HandsLabLogState.Active || applicationPaused || applicationUnfocused || !FloorReady || !HeadsetReady || !DeviceReady
                        || !CurrentPort || !CurrentPort.HasBoundMechanics) break;
                    if (command == "task_start")
                    {
                        if (TaskStartConfirmation) break;
                        TaskStartConfirmation = true;
                        accepted = Log("TASK_START_REQUESTED", "task." + Flow.CurrentTask, "AwaitingConfirmation", inputSource: inputSource);
                        if (!accepted) TaskStartConfirmation = false;
                        view?.Refresh(); break;
                    }
                    if (!TaskStartConfirmation) break;
                    TaskStartConfirmation = false;
                    changed = Flow.StartTask();
                    if (changed)
                    {
                        taskTiming.Start(Logger.Timestamp); taskEndRecorded = false;
                        inspectionId = ""; inspectionSnapshot = null;
                        object initial;
                        try { initial = CurrentPort.CaptureState(); }
                        catch (Exception ex) { Fail("task_initial_snapshot", ex); break; }
                        Log("TASK_STARTED", "task." + Flow.CurrentTask, "Started",
                            after: new { task_started_at = taskTiming.StartedAt, initial_state = initial, active_elapsed_s = 0d, wall_elapsed_s = 0d },
                            inputSource: inputSource, taskState: initial);
                    }
                    break;
                case "task_start_back":
                    if (!TaskStartConfirmation || (Flow.State != ResearchFlowState.Task1Ready && Flow.State != ResearchFlowState.Task2Ready)) break;
                    TaskStartConfirmation = false;
                    accepted = Log("TASK_START_CANCELLED", "task." + Flow.CurrentTask, "Ready", inputSource: inputSource);
                    view?.Refresh(); break;
                case "reference": if (LocalUICommandsAllowed && !ReferenceOpen) { OpenReference(inputSource); accepted = ReferenceOpen; } break;
                case "reference_close": if (ReferenceOpen) { CloseReference("closed", "User", inputSource); accepted = !ReferenceOpen; } break;
                case "reference_minimize": if (ReferenceOpen && !ReferenceMinimized) { MinimizeReference(inputSource); accepted = ReferenceMinimized; } break;
                case "reference_restore": if (ReferenceOpen && ReferenceMinimized) { RestoreReference(inputSource); accepted = !ReferenceMinimized; } break;
                case "inspect": accepted = Inspect(inputSource); break;
                case "inspect_continue":
                    if (!InspectionFeedbackVisible || !Flow.InspectPassed.HasValue) break;
                    bool passed = Flow.InspectPassed == true;
                    if (!passed && !taskTiming.ReturnToTask(Logger.Timestamp)) break;
                    changed = Flow.ContinueInspect();
                    if (changed && !passed) Log("RETRY", "task." + Flow.CurrentTask, "Continued", inputSource: inputSource);
                    break;
                case "helper": if (Flow.IsTutorial && view) { view.ShowHelper(); accepted = true; } break;
                // Tester-labelled stand-ins for the researcher console/ADB, only in a headset-started technical session.
                case "technical_resume":
                    if (TechnicalSelfTest && (Flow.State == ResearchFlowState.PostTask1Checkpoint || Flow.State == ResearchFlowState.PostTask2Checkpoint))
                    {
                        Log("TECHNICAL_CHECKPOINT_RESUME", "questionnaire", "TesterConfirmed", source: "Tester", inputSource: inputSource,
                            phaseOverride: "Checkpoint", taskIdOverride: Flow.CurrentTask);
                        var previous = Flow.State; ResearcherResume(); accepted = Flow.State != previous;
                    }
                    break;
                case "technical_demo_pass":
                    if (TechnicalSelfTest && Flow.InspectPassed == false && !DemoPassSet) { SetDemoPass(true); view?.Refresh(); accepted = DemoPassSet; }
                    break;
                default:
                    if (command != null && command.StartsWith("reference_tab."))
                    { string previous = ReferenceSection; SwitchReference(command.Substring(14), inputSource); accepted = ReferenceSection != previous; }
                    break;
            }
            if (changed) { Save(); Refresh(); }
            return (changed || accepted) && string.IsNullOrEmpty(Error);
        }

        bool Inspect(string input)
        {
            if (!LocalUICommandsAllowed) return false;
            if (Flow.IsTutorial)
            {
                if (Flow.TutorialCompleted != 4) return false;
                Log("INSPECT", "training.sample", "Requested", before: CaptureTaskState(), inputSource: input);
                if (!trainingStation) return Evidence("inspect", input);
                Log("INSPECT_RESULT", "training.trainer", trainingStation.Ready ? "PracticePass" : "PracticeFail",
                    after: new { result_key = trainingStation.InspectResultKey, scored = false }, inputSource: input);
                view?.ShowTrainerResult(trainingStation.InspectResultKey);
                return Evidence(trainingStation.InspectFact, input);
            }
            if (!taskTiming.CanManipulate || Logger?.State != HandsLabLogState.Active) return false;
            inspectionRequestedAt = Logger.Timestamp;
            if (!CurrentPort || !CurrentPort.TryInspect(out bool passed, out object state))
            { Error = "Task inspection adapter unavailable. Contact researcher."; BlockInteractions("inspection_unavailable"); view?.Refresh(); return false; }
            inspectionEvaluatedAt = Logger.Timestamp;
            try { inspectionSnapshot = JToken.FromObject(state); }
            catch (Exception ex) { Fail("inspection_snapshot", ex); return false; }
            if (!taskTiming.RequestInspection(inspectionRequestedAt, passed)) return false;
            inspectionId = Logger.NewActionId(); inspectionInput = input;
            Log("INSPECT", "task." + Flow.CurrentTask, "Requested", before: inspectionSnapshot,
                after: new { inspection_id = inspectionId, request_timestamp = inspectionRequestedAt, active_elapsed_s = taskTiming.ActiveElapsed },
                inputSource: input, actionId: inspectionId, taskState: inspectionSnapshot);
            Log(passed ? "INSPECT_PASS" : "INSPECT_FAIL", "task." + Flow.CurrentTask, passed ? "Passed" : "NotPassed", before: inspectionSnapshot,
                after: new { inspection_id = inspectionId, evaluation_timestamp = inspectionEvaluatedAt, feedback_visible = false, passed, outcome = CurrentPort.FeedbackKey },
                inputSource: input, actionId: inspectionId, taskState: inspectionSnapshot);
            if (!string.IsNullOrEmpty(Error)) return false;
            CloseReference("inspect_pending", "System");
            AdvanceTaskTiming(); Save(); Refresh();
            return string.IsNullOrEmpty(Error);
        }

        void AdvanceTaskTiming()
        {
            if (Flow?.IsTaskActive != true || !taskTiming.Started || Logger == null || !string.IsNullOrEmpty(Error)) return;
            taskTiming.Tick(Logger.Timestamp);
            if (taskTiming.EndReason == "time_limit" && !taskEndRecorded)
            {
                object final = CaptureTaskState();
                CloseReference("time_limit", "System");
                Log("TASK_TERMINATED", "task." + Flow.CurrentTask, "TimeLimit", before: final, source: "System", reason: "time_limit", taskState: final);
                RecordTaskEnd(final, "time_limit");
                Flow.EndTask(); Save(); Refresh();
                if (string.IsNullOrEmpty(Error)) QueueExport("task_" + Flow.CurrentTask + "_time_limit", "task_finalized");
                return;
            }
            if (!Flow.Paused && !taskTiming.Ended && taskTiming.Stage == ResearchInspectionStage.Ready && !Flow.InspectPassed.HasValue)
            {
                if (Flow.Inspect(taskTiming.InspectionPassed == true)) { Save(); Refresh(); }
            }
        }

        internal bool InspectionFeedbackRendered(string id, string renderedText, int frame)
        {
            if (id != inspectionId || Flow?.IsTaskActive != true || !Flow.InspectPassed.HasValue || Flow.Paused
                || !view || !view.InspectionFeedbackMatches(id, renderedText, frame) || Logger?.State != HandsLabLogState.Active || !string.IsNullOrEmpty(Error)) return false;
            AdvanceTaskTiming();
            double visibleAt = Logger.Timestamp;
            if (!taskTiming.MarkFeedbackVisible(visibleAt)) return false;
            bool passed = taskTiming.InspectionPassed == true;
            if (!Log("INSPECT_FEEDBACK_VISIBLE", "task." + Flow.CurrentTask, passed ? "Passed" : "NotPassed",
                after: new { inspection_id = id, feedback_timestamp = visibleAt, active_elapsed_s = taskTiming.ActiveElapsed,
                    wall_elapsed_s = taskTiming.WallElapsed, text = renderedText, language = Language, frame,
                    visibility_basis = "rendered_readable_panel", human_seen = (bool?)null }, source: "System",
                actionId: id, taskState: inspectionSnapshot)) return false;
            if (passed)
            {
                Log("TASK_COMPLETED", "task." + Flow.CurrentTask, "Completed", after: inspectionSnapshot,
                    source: "System", inputSource: inspectionInput, taskState: inspectionSnapshot);
                RecordTaskEnd(inspectionSnapshot, "passed");
            }
            Save(); Refresh();
            if (passed && string.IsNullOrEmpty(Error)) QueueExport("task_" + Flow.CurrentTask + "_completed", "task_finalized");
            return string.IsNullOrEmpty(Error);
        }

        void RecordTaskEnd(object final, string reason)
        {
            if (taskEndRecorded) return;
            taskEndRecorded = Log("TASK_ENDED", "task." + Flow.CurrentTask, reason == "passed" ? "Completed" : "Incomplete",
                before: final, after: new { end_reason = reason, completed = reason == "passed", task_started_at = taskTiming.Started ? (double?)taskTiming.StartedAt : null,
                    task_ended_at = taskTiming.EndedAt, wall_elapsed_s = taskTiming.Started ? (double?)taskTiming.WallElapsed : null,
                    active_elapsed_s = taskTiming.Started ? (double?)taskTiming.ActiveElapsed : null, inspection_id = inspectionId, feedback_visible = InspectionFeedbackVisible },
                source: "System", reason: reason, taskState: final);
        }

        void SuspendTaskForTracking(string reason)
        {
            if (Flow?.IsTaskActive != true || Flow.Paused || taskTiming.Ended || !string.IsNullOrEmpty(Error)) return;
            if (!Pause("tracking")) return;
            CloseReference(reason, "System");
            Log("SESSION_PAUSED", "tracking", "Paused", after: PauseTiming(), source: "System", reason: reason);
            Save(); Refresh();
        }

        public void ResearcherResume()
        {
            if (Record != null && (Record.session_state != RegistryState.InProgress || Logger?.State != HandsLabLogState.Active)) return;
            if (Flow == null || Transitioning || !string.IsNullOrEmpty(Error)) return;
            string checkpointTask = Flow.CurrentTask;
            // Headset removal may pause a checkpoint; the same researcher confirmation completes it.
            if (Flow.Paused && !Resume("ResearcherResumed", "System", "None")) return;
            if (!Flow.ResearcherResumeCheckpoint()) return;
            Log("CHECKPOINT_COMPLETED", "questionnaire", "ResearcherConfirmed", source: "System", phaseOverride: "Checkpoint", taskIdOverride: checkpointTask);
            if (Flow.State == ResearchFlowState.ParticipantComplete)
            {
                CloseReference("session_complete", "System");
                GetComponent<ResearchMovementSampler>()?.FlushAndStop("session_complete");
                if (Logger == null || !Logger.Complete("research_session_completed"))
                {
                    Error = "Session log could not complete; participant remains locked and is not marked Completed.";
                    BlockInteractions("completion_write_error"); view?.Refresh(); return;
                }
            }
            Save();
            if (Flow.State == ResearchFlowState.ParticipantComplete && string.IsNullOrEmpty(Error)) QueueExport("session_completed", "finalized");
            if (Flow.State == ResearchFlowState.Task2Ready) StartCoroutine(MoveToStation(Flow.CurrentTask == "A" ? 1 : 2));
            Refresh();
        }

        // The researcher has no participant input device, so the actor lives in source/result/reason, as in ResearcherResume.
        public bool ResearcherStop() => Stop("None", "System", "ResearcherStopped", "researcher_stop");

        bool Stop(string inputSource, string source, string result, string reason)
        {
            if (!ParticipantCanStop) return false;
            if (Flow.Paused && PauseKind == "stop") return true;
            try { if (!Flow.Paused) Pause("stop"); }
            catch (Exception ex)
            {
                Flow.Pause();
                if (string.IsNullOrEmpty(Error)) Error = "Stop timing unavailable: " + ex.Message;
            }
            finally { PauseKind = "stop"; HelpRequested = false; Refresh(); }
            try
            {
                CloseReference(reason, "System", inputSource);
                bool recorded = Log("SESSION_PAUSED", "safety", result, after: PauseTiming(), source: source, inputSource: inputSource, reason: reason);
                if (!recorded && string.IsNullOrEmpty(Error)) Error = "Stop applied; event logging unavailable.";
                Save();
            }
            catch (Exception ex) { Error = (string.IsNullOrEmpty(Error) ? "" : Error + " | ") + "Stop record unavailable: " + ex.Message; }
            finally { Refresh(); }
            return Flow.Paused;
        }

        void RestoreSafetyUI()
        {
            if (!ParticipantCanStop || Transitioning || applicationPaused || applicationUnfocused || !HeadsetReady || !DeviceReady
                || (string.IsNullOrEmpty(Error) && Logger?.State == HandsLabLogState.Active)) return;
            foreach (var g in guards)
                if (g && g.Suspended && g.TrackingValid && g.InputNeutral) g.Resume();
        }

        bool Pause(string kind)
        {
            TaskStartConfirmation = false;
            AdvanceTaskTiming();
            if (!Flow.Pause()) return false;
            pauseStart = Logger?.Timestamp ?? double.NaN;
            if (Flow.IsTaskActive && taskTiming.Started && !taskTiming.Ended) taskTiming.Pause(pauseStart);
            InvalidateTutorialInstruction("pause_" + kind);
            PauseKind = kind; HelpRequested = false;
            return true;
        }

        object PauseTiming() => new { pause_kind = PauseKind, pause_started_at = double.IsNaN(pauseStart) ? (double?)null : pauseStart,
            active_elapsed_s = taskTiming.Started ? (double?)taskTiming.ActiveElapsed : null,
            wall_elapsed_s = taskTiming.Started ? (double?)taskTiming.WallElapsed : null };

        // Paused time is logged so analysis (and SummaryText) can exclude it from active task duration.
        bool Resume(string result, string source, string inputSource)
        {
            if (Flow.IsTaskActive && (applicationPaused || applicationUnfocused || !HeadsetReady || !DeviceReady)) return false;
            if (!Flow.ResearcherResume()) return false;
            double resumedAt = Logger?.Timestamp ?? double.NaN;
            if (Flow.IsTaskActive && taskTiming.Started && !taskTiming.Ended) taskTiming.Resume(resumedAt);
            double paused = resumedAt - pauseStart;
            Log("SESSION_RESUMED", "safety", result, after: new { pause_kind = PauseKind,
                pause_started_at = double.IsNaN(pauseStart) ? (double?)null : pauseStart,
                pause_resumed_at = double.IsNaN(resumedAt) ? (double?)null : resumedAt,
                paused_seconds = double.IsNaN(paused) ? (double?)null : paused },
                source: source, inputSource: inputSource);
            PauseKind = ""; HelpRequested = false; pauseStart = double.NaN;
            if (!FinalizeRecordedTutorialCredits()) return false;
            Save(); Refresh();
            return string.IsNullOrEmpty(Error);
        }

        public void ResearcherAbort(bool invalid = false, string terminationReason = "other")
        {
            if (Record == null || Record.session_state != RegistryState.InProgress) return;
            if (!new[] { "time_limit", "withdrawal", "safety_stop", "technical_failure", "other" }.Contains(terminationReason))
                throw new ArgumentException("Explicit known termination reason required.", nameof(terminationReason));
            if (Flow?.IsTaskActive == true && !taskEndRecorded)
            {
                object final = CaptureTaskState();
                if (taskTiming.Started && !taskTiming.Ended)
                    taskTiming.End(terminationReason, Logger.Timestamp);
                string taskReason = taskTiming.Ended ? taskTiming.EndReason : terminationReason;
                Log("TASK_TERMINATED", "task." + Flow.CurrentTask, "Aborted", before: final, source: "System", reason: taskReason, taskState: final);
                // A UI refresh fault must not stop the terminal registry write and export below.
                try { RecordTaskEnd(final, taskReason); }
                catch (Exception ex) { Log("SYSTEM_ERROR", "session.abort", "RefreshFault", after: ex.Message, source: "System"); }
            }
            try { CloseReference("session_ended", "System"); }
            catch (Exception ex) { Log("SYSTEM_ERROR", "session.abort", "RefreshFault", after: ex.Message, source: "System"); }
            BlockInteractions("researcher_ended");
            if (!Log("SESSION_ABORTED", "session", invalid ? "Invalid" : "Aborted", source: "System", reason: terminationReason) && string.IsNullOrEmpty(Error))
                Error = "Session ended with incomplete logs; terminal events were not written.";
            try { Record = Registry.Finish(Record, invalid ? RegistryState.Invalid : RegistryState.Aborted, terminationReason); }
            catch (Exception ex) { Error = ex.Message; }
            GetComponent<ResearchMovementSampler>()?.FlushAndStop("session_abort");
            Logger?.Dispose();
            // Queue the terminal export before any UI refresh so a view fault cannot strand the data.
            QueueExport(invalid ? "session_invalid" : "session_aborted", "partial");
            try { view?.Refresh(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        // After an emergency stop the researcher records the real category; the registry keeps the first reason and every change.
        public string AmendTermination(string participantId, string reason, string source)
        {
            try
            {
                var amended = Registry.AmendTermination(participantId, reason, source, out bool changed);
                if (!changed) return null;
                bool current = Record?.participant_id == amended.participant_id &&
                    Record.session_id == amended.session_id && Record.run_id == amended.run_id;
                if (current) Record = amended;
                try
                {
                    if (current && Logger != null)
                    {
                        if (!QueueExport("termination_amended", "partial"))
                            throw new IOException(string.IsNullOrEmpty(ExportDeliveryError) ? "No export snapshot created" : ExportDeliveryError);
                    }
                    else
                        TrackExport(ResearchExportQueue.SnapshotSavedRun(Registry, amended.participant_id,
                            amended.session_id, amended.run_id, Logger?.RunId, "termination_amended"), amended.run_id);
                    return null;
                }
                catch (Exception ex)
                {
                    string failure = "Termination amendment saved; automatic export failed: " + ex.Message;
                    if (current) ExportDeliveryError = failure;
                    return failure;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is IOException) { return ex.Message; }
        }

        public void SetDemoPass(bool passed)
        {
            if (!IsTechnical || !CurrentPort) return;
            CurrentPort.demoPassed = passed;
            Log("DEMO_STATE_SET", "task." + CurrentPort.taskId, passed ? "PassFixture" : "FailFixture", after: CurrentPort.CaptureState(), source: "Tester");
        }

        void Save()
        {
            try { Record = Registry.SaveProgress(Record, Flow); }
            catch (Exception ex) { Error = "Session persistence failed: " + ex.Message; Log("SYSTEM_ERROR", "registry", "WriteError", after: Error, source: "System"); BlockInteractions("persistence_error"); }
        }

        void Refresh()
        {
            bool uiAllowed = !Transitioning && string.IsNullOrEmpty(Error) &&
                ((TechnicalSelfSetup && Record == null) || (FloorReady && Flow != null
                    && Record?.session_state == RegistryState.InProgress && Logger?.State == HandsLabLogState.Active));
            bool allow = uiAllowed && FloorReady && Flow != null && Flow.CanInteract && (!Flow.IsTaskActive || taskTiming.CanManipulate);
            interactionAllowed = allow;
            bool practice = allow && Flow.CanInteract && Flow.IsTutorial;
            foreach (var item in trainingObjects)
                if (item && item.grabbable)
                {
                    if (!practice && item.IsHeld) item.DetachForSystem("practice_gate", Logger?.NewActionId());
                    item.grabbable.enabled = practice;
                }
            foreach (var gate in locomotionGates) if (gate) gate.SetActive(allow && Flow.CanInteract && !referenceNavigationHeld);
            try
            {
                foreach (var port in taskPorts) if (port) port.SetInteractionAllowed(allow && Flow.CanInteract && Flow.IsTaskActive && port == CurrentPort);
            }
            catch (Exception ex) { Fail("task_interaction_gate", ex); return; }
            // Detach/port cancellation must precede SDK OnDisable, which emits Unselect.
            foreach (var gate in worldGates) if (gate) gate.SetAllowed(allow);
            if (trainingRoot) trainingRoot.SetActive(Flow != null && Flow.IsTutorial);
            // Pause/checkpoint/Inspect keep native UI input; suspend only after system releases.
            foreach (var g in guards)
            {
                if (!g) continue;
                if (uiAllowed && g.Suspended) g.Resume();
                else if (!uiAllowed && !g.Suspended) g.Suspend(FloorReady ? "flow_gate" : "floor_reference_unconfirmed");
            }
            RestoreSafetyUI();
            view?.Refresh();
        }

        void BlockInteractions(string reason)
        {
            if (!string.IsNullOrEmpty(Error) && Flow?.IsTaskActive == true && taskTiming.Started && !taskTiming.Ended && Logger != null)
            {
                taskTiming.End("technical_failure", Logger.Timestamp);
                if (Logger.State == HandsLabLogState.Active) RecordTaskEnd(CaptureTaskState(), taskTiming.EndReason);
            }
            InvalidateTutorialInstruction(reason);
            if (!string.IsNullOrEmpty(Error) && ReferenceOpen)
            {
                ReferenceOpen = false; ReferenceMinimized = false;
                CloseExposure("system_error", "System", "None");
            }
            foreach (var item in trainingObjects) if (item && item.IsHeld) item.DetachForSystem(reason, Logger?.NewActionId());
            foreach (var port in taskPorts)
                if (port)
                    try { port.SetInteractionAllowed(false); }
                    catch (Exception ex) { Error = "Task interaction gate failed: " + ex.Message; }
            foreach (var gate in worldGates) if (gate) gate.SetAllowed(false);
            foreach (var g in guards) if (g) g.Suspend(reason);
            foreach (var gate in locomotionGates) if (gate) gate.SetActive(false);
            interactionAllowed = false;
        }

        public object CaptureTaskState()
        {
            try
            {
                return Flow != null && Flow.IsTaskActive && CurrentPort ? CurrentPort.CaptureState() :
                    new { flow = Flow?.State.ToString() ?? "ResearcherSetup", training = trainingObjects.Where(t => t).Select(t => t.CaptureSnapshot()).ToArray() };
            }
            catch (Exception ex) { return new { state_available = false, error_type = ex.GetType().Name }; }
        }

        public bool Log(string name, string target, string result, object before = null, object after = null, string source = "User", string inputSource = "None", string actionId = null, bool? trackingValid = null,
            string phaseOverride = null, string taskIdOverride = null, string reason = null, object taskState = null)
        {
            return Logger != null && Logger.Record(name, target, result, taskState ?? CaptureTaskState(), "None", before, after, source,
                reason: reason, actionId: actionId, inputSourceOverride: inputSource, trackingValid: trackingValid,
                phaseOverride: phaseOverride ?? (Flow?.IsTutorial == true ? "Tutorial" : Flow?.CurrentTask.Length > 0 ? "Experiment" : "Session"),
                taskIdOverride: taskIdOverride ?? (Flow?.IsTutorial == true ? "training" : Flow?.CurrentTask.Length > 0 ? Flow.CurrentTask : "session"));
        }

        void Fail(string reason, Exception exception)
        {
            Error = reason + ": " + exception.Message;
            Log("SYSTEM_ERROR", "session." + reason, "Unavailable", after: new { error_type = exception.GetType().Name }, source: "System");
            BlockInteractions(reason); view?.Refresh();
        }

        void LoggerFailed(string message) { Error = "Logger write failed: " + message; BlockInteractions("logger_write_error"); view?.Refresh(); }

        internal string ReferenceScrollInputSource => InputSource(guards.FirstOrDefault(g => g && g.side == Oculus.Interaction.Input.Handedness.Right));
        public string InputSourceForPointer(int pointer) => InputSource(GuardForPointer(pointer));
        string InputSource(HandsLabInputGuard guard)
        {
            if (!guard || !guard.TrackingValid) return "None";
            if (guard.Mode == HandsLabInputGuard.InputMode.TrackedHand) return "HandTracking";
            string usage = guard.side == Oculus.Interaction.Input.Handedness.Left ? "LeftHand" : "RightHand";
            bool simulated = UnityEngine.InputSystem.InputSystem.devices.Any(d => d is UnityEngine.InputSystem.XR.XRController &&
                d.usages.Any(u => u.ToString() == usage) && (d.name.StartsWith("HandsLabSimulated") || d.layout.StartsWith("XRSimulated")));
            return simulated ? "SimulatedController" : "Controller";
        }
        internal HandsLabInputGuard GuardForPointer(int pointer)
        {
            foreach (var guard in guards)
            {
                if (!guard || !guard.interactionsRoot) continue;
                guard.interactionsRoot.GetComponentsInChildren(true, pointerComponents);
                foreach (var component in pointerComponents)
                    if (component && component.isActiveAndEnabled && !(component is InteractorGroup)
                        && component is IInteractor interactor && interactor.Identifier == pointer) return guard;
            }
            return null;
        }

        void ObjectPointer(HandsLabObject item, PointerEvent evt)
        {
            if (evt.Type != PointerEventType.Select && evt.Type != PointerEventType.Unselect && evt.Type != PointerEventType.Cancel) return;
            if (Flow == null || !Flow.IsTutorial || Logger == null) return;
            var key = (item, evt.Identifier);
            var guard = GuardForPointer(evt.Identifier);
            string input = InputSourceForPointer(evt.Identifier);
            if (evt.Type == PointerEventType.Select)
            {
                if (!LocalUICommandsAllowed || !trainingObjects.Contains(item) || (guard && (!guard.TrackingValid || !guard.InteractionsAllowed)))
                { item.DetachForSystem("not_active", Logger.NewActionId()); return; }
                if (grabActions.ContainsKey(key)) return;
                guard?.RegisterHeldBody(item.body); grabRotations[item] = item.transform.rotation; grabPositions[item] = item.transform.position;
                lastHeldSnapshots[item] = item.CaptureSnapshot(); grabSources[item] = input; grabActions[key] = Logger.NewActionId();
                Log("ACTION", item.stableId, "Grab", after: item.CaptureSnapshot(), inputSource: input, trackingValid: guard?.TrackingValid, actionId: grabActions[key]);
                TutorialActionCommitted("grab", TutorialActionEpoch("grab"), grabActions[key], input, guard?.TrackingValid ?? ResearchFloorPolicy.EditorFixtureAllowed);
                if (Flow.TutorialCompleted == 2) view?.Refresh();
            }
            else if (evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
            {
                bool accepted = grabActions.TryGetValue(key, out var action);
                bool system = evt.Type == PointerEventType.Cancel || item.SystemReason != null || (guard && guard.SystemCancelling);
                if (!accepted && !system) return;
                if (accepted) guard?.UnregisterHeldBody(item.body);
                if (accepted && grabSources.TryGetValue(item, out var observedInput)) input = observedInput;
                Log(system ? "SYSTEM_RELEASE" : "ACTION", item.stableId, system ? "Cancelled" : "Release", before: accepted && lastHeldSnapshots.TryGetValue(item, out var before) ? new { sample_kind = "LastHeldFrame", state = before } : null,
                    after: item.CaptureSnapshot(), source: system ? "System" : "User", inputSource: input, trackingValid: guard?.TrackingValid,
                    actionId: accepted ? action : item.SystemActionId);
                if (!accepted) return;
                if (!system) TutorialActionCommitted("release", TutorialActionEpoch("release"), action, input, guard?.TrackingValid ?? ResearchFloorPolicy.EditorFixtureAllowed);
                else if (Flow.TutorialCompleted == 2) InterruptTutorialPractice("system_release");
                grabPositions.Remove(item); grabRotations.Remove(item);
                lastHeldSnapshots.Remove(item); grabSources.Remove(item); grabActions.Remove(key);
                if (Flow.TutorialCompleted == 2) view?.Refresh();
            }
        }

        void Update()
        {
            if (pendingExports.Count > 0 && pendingExports[0].task.IsCompleted) ApplyExports();
            RestoreSafetyUI();
            if (Flow?.IsTaskActive == true && (!HeadsetReady || !DeviceReady)) SuspendTaskForTracking("tracking_unavailable");
            AdvanceTaskTiming();
            if (Flow?.IsTutorial == true && lastTutorialHands != TutorialUsesHands)
            { lastTutorialHands = TutorialUsesHands; view?.Refresh(); }
            // Technical setup readiness lines: redraw only when a readiness input changes.
            if (TechnicalSelfSetup && Record == null && Time.unscaledTime >= nextSetupPoll)
            {
                nextSetupPoll = Time.unscaledTime + .25f;
                int signature = (HeadsetReady ? 1 : 0) | (GuardTracked(Oculus.Interaction.Input.Handedness.Left) ? 2 : 0)
                    | (GuardTracked(Oculus.Interaction.Input.Handedness.Right) ? 4 : 0) | (FloorReady ? 8 : 0) | (LoggerReady ? 16 : 0);
                if (signature != setupSignature) { setupSignature = signature; view?.Refresh(); }
            }
            // Menu pauses during an activity and explicitly recalls an already-paused panel.
            bool menu = false;
            foreach (var g in guards)
                menu |= g && g.TrackingValid && g.Mode == HandsLabInputGuard.InputMode.Controller && g.controllerReference is Oculus.Interaction.Input.IController c
                    && c.IsButtonUsageAnyActive(Oculus.Interaction.Input.ControllerButtonUsage.MenuButton);
            if (menu && !menuWasPressed && Flow != null)
            {
                if (Flow.Paused) view?.LocalCommand("panel_recovery", "Controller");
                else if (Flow.IsTutorial || Flow.IsTaskActive
                    || Flow.State == ResearchFlowState.Task1Ready || Flow.State == ResearchFlowState.Task2Ready) Command("pause", "Controller");
            }
            menuWasPressed = menu;
            // The existing teleport/turn groups share the Reference scroll axis.
            // Return it only after closing/minimizing and releasing the stick.
            if (referenceNavigationHeld && (!ReferenceOpen || ReferenceMinimized))
            {
                UpdateReferenceNavigation(ReferenceNavigationInputAvailable, ReferenceScrollAxis);
            }
            if (interactionAllowed && Flow?.IsTutorial == true && Flow.CanInteract) RecoverLostObjects(Time.time);
            if (Flow?.IsTutorial != true || !origin || !origin.Camera) return;
            TutorialPromptShown();
            if (!TutorialInputValid) return;
            if (TutorialArmedFact == "roomscale" && (origin.Camera.transform.localPosition - physicalHeadStart).magnitude > .15f)
                TutorialActionCommitted("roomscale", TutorialInstructionEpoch, "roomscale_" + TutorialInstructionEpoch);
            foreach (var item in trainingObjects)
            {
                if (!item || !item.IsHeld) continue;
                lastHeldSnapshots[item] = item.CaptureSnapshot();
                if (TutorialArmedFact == "move" && grabPositions.TryGetValue(item, out var pos) && item.transform.position.y - pos.y > .08f)
                    TutorialActionCommitted("move", TutorialInstructionEpoch, "lift_" + TutorialInstructionEpoch, grabSources.TryGetValue(item, out var moveSource) ? moveSource : "None");
                if (TutorialArmedFact == "rotate" && grabRotations.TryGetValue(item, out var rot) && Quaternion.Angle(rot, item.transform.rotation) > 25)
                    TutorialActionCommitted("rotate", TutorialInstructionEpoch, "rotate_" + TutorialInstructionEpoch, grabSources.TryGetValue(item, out var rotateSource) ? rotateSource : "None");
            }
        }

        void RecoverLostObjects(float now)
        {
            if (!recoveryReady || Logger?.State != HandsLabLogState.Active) return;
            foreach (var item in trainingObjects)
            {
                if (!item || !item.body) continue;
                var position = item.transform.position;
                string reason = item.IsHeld ? null : ResearchObjectRecovery.Reason(position, recoveryRoom, recoveryFloorTop, Embedded(position));
                if (reason == null) { lostSince.Remove(item); continue; }
                if (!lostSince.TryGetValue(item, out float since)) { lostSince[item] = now; continue; }
                if (!ResearchObjectRecovery.Due(item.IsHeld, now - since)) continue;
                lostSince.Remove(item);
                var before = item.CaptureSnapshot();
                string action = Logger.NewActionId();
                item.DetachForSystem("object_recovery", action); // releases residual pointers, zeroes velocity
                item.ResetToAuthored();
                Log("SYSTEM_OBJECT_RECOVERY", item.stableId, "ResetToAuthored", before: before, after: item.CaptureSnapshot(),
                    source: "System", actionId: action, reason: reason);
            }
        }

        // Centre inside static Training/Environment geometry (tunnelled into a wall or the table volume).
        bool Embedded(Vector3 position)
        {
            if (!recoveryEnvironment) return false;
            int count = Physics.OverlapSphereNonAlloc(position, .002f, recoveryHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!recoveryHits[i].attachedRigidbody && recoveryHits[i].transform.IsChildOf(recoveryEnvironment)) return true;
            return false;
        }

        public void UpdateReferenceNavigation(bool inputAvailable, Vector2 axis)
        {
            if (referenceNavigationHeld && (!ReferenceOpen || ReferenceMinimized) && inputAvailable && axis.sqrMagnitude < .04f)
            { referenceNavigationHeld = false; Refresh(); }
        }

        public void Evidence(string fact)
        { Evidence(fact, "None"); }

        bool Evidence(string fact, string inputSource)
        { return TutorialActionCommitted(fact, TutorialActionEpoch(fact), Logger?.NewActionId(), inputSource); }

        bool TutorialInputValid => !RenderComparisonRunning && Flow?.IsTutorial == true && Flow.CanInteract && !Transitioning
            && FloorReady && HeadsetReady && string.IsNullOrEmpty(Error) && Logger?.State == HandsLabLogState.Active
            && (Record == null || Record.session_state == RegistryState.InProgress)
            && (guards.Length == 0 ? IsTechnical && ResearchFloorPolicy.EditorFixtureAllowed : guards.All(g => g && g.TrackingValid
                && g.InteractionsAllowed && !g.WaitingForNeutral && !g.Suspended && !g.SystemCancelling));

        public long TutorialActionEpoch(string fact) => TutorialInputValid && tutorialInstruction?.fact == fact ? tutorialInstruction.epoch : 0;

        public long TutorialActionAttempted(string fact, string actionId, string inputSource, bool trackingValid)
        {
            long epoch = TutorialActionEpoch(fact);
            if (Flow?.IsTutorial == true && Logger?.State == HandsLabLogState.Active)
                Log("TUTORIAL_ACTION_ATTEMPT", "training." + fact, "Observed", after: new { instruction_epoch = epoch,
                    armed_epoch = TutorialInstructionEpoch, armed_fact = TutorialArmedFact, tutorial_group = Flow.TutorialCompleted },
                    actionId: actionId, inputSource: inputSource, trackingValid: trackingValid);
            return epoch;
        }

        public bool TutorialActionCommitted(string fact, long instructionEpoch, string actionId, string inputSource = "None", bool trackingValid = true, string source = "User")
        {
            if (Flow?.IsTutorial != true || Logger?.State != HandsLabLogState.Active) return false;
            actionId = actionId ?? Logger.NewActionId();
            var diagnostic = new { fact, instruction_epoch = instructionEpoch, armed_epoch = TutorialInstructionEpoch,
                armed_fact = TutorialArmedFact, tutorial_group = Flow.TutorialCompleted };
            if (!Log("TUTORIAL_ACTION_COMMITTED", "training." + fact, "Observed", after: diagnostic, source: source,
                actionId: actionId, inputSource: inputSource, trackingValid: trackingValid)) return false;
            bool duplicate = tutorialEvidence.Contains(fact) || TutorialFacts.Take(Flow.TutorialCompleted).Any(facts => facts.Contains(fact));
            // Reopening Reference is recovery for a pending skill, not a second mandatory credit.
            bool recovery = (fact == "reference" || fact == "restore") && tutorialInstruction?.fact == fact;
            string rejection = !TutorialInputValid || !trackingValid ? "safety_or_tracking_invalid" : duplicate && !recovery ? "already_credited"
                : instructionEpoch == 0 ? "instruction_not_armed_at_attempt" : tutorialInstruction == null || instructionEpoch != tutorialInstruction.epoch
                ? "stale_instruction_epoch" : fact != tutorialInstruction.fact ? "different_instruction"
                : !TutorialFacts[Flow.TutorialCompleted].Contains(fact) && fact != "restore" ? "not_a_required_skill" : null;
            if (rejection != null)
            {
                Log(duplicate && !recovery ? "TUTORIAL_ACTION_DUPLICATE" : "TUTORIAL_ACTION_REJECTED", "training." + fact, "NotCredited",
                    after: diagnostic, source: source, actionId: actionId, inputSource: inputSource, trackingValid: trackingValid, reason: rejection);
                if (fact == "release" && Flow.TutorialCompleted == 2) InterruptTutorialPractice("released_before_instruction");
                if (fact == "close" && Flow.TutorialCompleted == 3) InterruptTutorialPractice("closed_before_instruction");
                return false;
            }
            if (!Log("TUTORIAL_ACTION_ACCEPTED", "training." + fact, recovery && duplicate ? "RecoveryAccepted" : "SkillAccepted", after: diagnostic,
                source: source, actionId: actionId, inputSource: inputSource, trackingValid: trackingValid)) return false;
            if (fact == "restore" || duplicate)
            { tutorialInstruction = null; view?.Refresh(); return true; }
            if (!Log("TUTORIAL_SKILL_CREDIT", "training." + fact, "CreditedOnce", after: diagnostic, source: source,
                actionId: actionId, inputSource: inputSource, trackingValid: trackingValid)) return false;
            tutorialInstruction = null;
            bool added = tutorialEvidence.Add(fact);
            TutorialLastCompletedFact = fact; TutorialLastCreditActionId = actionId; TutorialLastCreditEpoch = instructionEpoch;
            if (added && TutorialFacts[Flow.TutorialCompleted].Contains(fact)) view?.TutorialFactCompleted(fact);
            bool complete = TutorialFacts[Flow.TutorialCompleted].All(tutorialEvidence.Contains);
            if (!complete) { if (added) view?.Refresh(); return added && TutorialFacts[Flow.TutorialCompleted].Contains(fact); }
            var step = (TutorialStep)Flow.TutorialCompleted;
            if (step == TutorialStep.Reference)
            {
                CloseReference("tutorial_step_completed", "System");
                if (!string.IsNullOrEmpty(Error) || Logger?.State != HandsLabLogState.Active) return false;
            }
            var previous = new ResearchSessionFlow(Flow.Order, Flow.State, Flow.TutorialCompleted, Flow.Paused, Flow.InspectPassed);
            if (Flow.CompleteTutorialStep(step))
            {
                if (!Log("TUTORIAL_STEP_COMPLETED", "training." + step, "Completed", source: "System"))
                {
                    Flow = previous;
                    view?.Refresh();
                    return false;
                }
                tutorialEvidence.Clear(); Save(); Refresh(); view?.AnimateChecklist();
                return string.IsNullOrEmpty(Error);
            }
            return false;
        }

        public void TutorialPromptShown()
        {
            if (Flow?.IsTutorial != true) return;
            if (!TutorialInputValid) { InvalidateTutorialInstruction("safety_or_tracking_invalid"); return; }
            string fact = TutorialFact;
            if (tutorialInstruction?.fact == fact) return;
            if (tutorialInstruction != null) InvalidateTutorialInstruction("instruction_changed");
            if (fact == null || TutorialNeedsGrab || TutorialNeedsRelease || !view || !view.TutorialPromptPresented(fact) || !TutorialControlNeutral(fact)) return;
            long epoch = ++tutorialEpoch;
            if (!Log("TUTORIAL_PROMPT_ARMED", "training." + fact, "PresentedAndArmed", after: new { fact, instruction_epoch = epoch,
                tutorial_group = Flow.TutorialCompleted, prompt_presented = true, measurement = "panel_presentation_not_reading_or_eye_gaze" }, source: "System")) return;
            tutorialInstruction = new TutorialInstruction(fact, epoch);
            if (fact == "roomscale" && origin && origin.Camera) physicalHeadStart = origin.Camera.transform.localPosition;
            foreach (var item in trainingObjects.Where(o => o && o.IsHeld))
            {
                if (fact == "move") grabPositions[item] = item.transform.position;
                if (fact == "rotate") grabRotations[item] = item.transform.rotation;
            }
        }

        bool TutorialControlNeutral(string fact)
        {
            if (fact == "move" || fact == "rotate" || fact == "release" || fact == "roomscale") return true;
            foreach (var guard in guards)
            {
                if (guard.Mode == HandsLabInputGuard.InputMode.Controller && guard.controllerReference is Oculus.Interaction.Input.IController input)
                {
                    if (fact == "teleport" || fact == "snap")
                    { if (input.ControllerInput.Primary2DAxis.sqrMagnitude >= .04f) return false; }
                    else if (fact == "grab")
                    { if (input.ControllerInput.Grip >= guard.analogReleaseThreshold) return false; }
                    else if (input.ControllerInput.Trigger >= guard.analogReleaseThreshold) return false;
                }
                else if (guard.Mode == HandsLabInputGuard.InputMode.TrackedHand && !guard.InputNeutral) return false;
                else if (!ResearchFloorPolicy.EditorFixtureAllowed) return false;
            }
            return true;
        }

        void TutorialInputChanged(Oculus.Interaction.Input.Handedness side, string reason)
        {
            if (!TutorialInputValid) InvalidateTutorialInstruction("input_" + reason);
            if (Flow?.IsTaskActive == true && !DeviceReady) SuspendTaskForTracking("input_" + reason);
        }

        void InvalidateTutorialInstruction(string reason)
        {
            if (tutorialInstruction == null) return;
            var previous = tutorialInstruction; tutorialInstruction = null; ++tutorialEpoch;
            if (Flow?.IsTutorial == true && Logger?.State == HandsLabLogState.Active)
                Log("TUTORIAL_INSTRUCTION_INTERRUPTED", "training." + previous.fact, "RetainCompletedSkills",
                    after: new { instruction_epoch = previous.epoch, tutorial_group = Flow.TutorialCompleted,
                        retained_facts = TutorialFacts[Flow.TutorialCompleted].Where(tutorialEvidence.Contains).ToArray() }, source: "System", reason: reason);
        }

        string policyAttribution = "";
        // First SESSION_PROTOCOL_VERSIONS row of this run that carried a recorded policy; null when the run predates the field.
        string RecordedPolicyFromEvents()
        {
            try
            {
                using (var reader = new StreamReader(new FileStream(Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                    for (string line; (line = reader.ReadLine()) != null;)
                    {
                        if (line.IndexOf("SESSION_PROTOCOL_VERSIONS", StringComparison.Ordinal) < 0) continue;
                        var row = JObject.Parse(line);
                        if ((string)row["event"] != "SESSION_PROTOCOL_VERSIONS" || (string)row["run_id"] != Record.run_id) continue;
                        string policy = (string)(row["after"] as JObject)?["recorded_ui_policy"];
                        if (ResearchUIPolicy.IsKnown(policy)) return policy;
                    }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException) { }
            return null;
        }

        bool RestoreTutorialCredits()
        {
            if (Flow?.IsTutorial != true || Flow.TutorialCompleted >= TutorialFacts.Length) return true;
            try
            {
                if (Record == null || Logger.RunId != Record.run_id) throw new InvalidDataException("Tutorial resume run identity mismatch.");
                bool sessionConfirmed = false;
                var presented = new Dictionary<long, (string fact, int group)>();
                var accepted = new HashSet<string>();
                var restored = new HashSet<string>();
                string last = null; long epoch = tutorialEpoch;
                using (var reader = new StreamReader(new FileStream(Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                    for (string line; (line = reader.ReadLine()) != null;)
                    {
                        var row = JObject.Parse(line);
                        if ((string)row["run_id"] != Record.run_id) throw new InvalidDataException("Mixed run in tutorial evidence.");
                        var after = row["after"] as JObject;
                        string name = (string)row["event"];
                        if ((name == "SESSION_CONFIGURED" || name == "SESSION_RESUMED") && after?["session_id"] != null)
                        {
                            if ((string)after["session_id"] != Record.session_id || (string)after["participant_id"] != Record.participant_id
                                || (string)after["run_id"] != Record.run_id) throw new InvalidDataException("Tutorial resume session identity mismatch.");
                            sessionConfirmed = true;
                        }
                        if (name != "TUTORIAL_PROMPT_ARMED" && name != "TUTORIAL_ACTION_ACCEPTED" && name != "TUTORIAL_SKILL_CREDIT") continue;
                        if (!sessionConfirmed || (string)row["phase"] != "Tutorial" || (string)row["task_id"] != "training")
                            throw new InvalidDataException("Unbound tutorial credit provenance.");
                        long recordedEpoch = (long?)after?["instruction_epoch"] ?? 0;
                        int group = (int?)after?["tutorial_group"] ?? -1;
                        string fact = (string)after?["fact"];
                        if (recordedEpoch <= 0 || group < 0 || group >= TutorialFacts.Length || string.IsNullOrEmpty(fact)
                            || (string)row["target"] != "training." + fact) throw new InvalidDataException("Invalid tutorial instruction identity.");
                        epoch = Math.Max(epoch, recordedEpoch);
                        if (name == "TUTORIAL_PROMPT_ARMED") { presented[recordedEpoch] = (fact, group); continue; }
                        if (!presented.TryGetValue(recordedEpoch, out var instruction) || instruction.fact != fact || instruction.group != group)
                            throw new InvalidDataException("Credit has no matching presented instruction.");
                        string action = (string)row["action_id"];
                        if (string.IsNullOrEmpty(action) || ((string)row["source"] != "User" && ((string)row["source"] != "Tester" || !Record.test_data)))
                            throw new InvalidDataException("Credit has no valid actor or action identity.");
                        string credit = recordedEpoch + ":" + action;
                        if (name == "TUTORIAL_ACTION_ACCEPTED") { accepted.Add(credit); continue; }
                        if (!accepted.Contains(credit) || !TutorialFacts[group].Contains(fact) || group > Flow.TutorialCompleted)
                            throw new InvalidDataException("Credit contradicts accepted action or saved tutorial group.");
                        last = fact; TutorialLastCreditActionId = action; TutorialLastCreditEpoch = recordedEpoch;
                        if (group == Flow.TutorialCompleted && !restored.Add(fact)) throw new InvalidDataException("Duplicate durable tutorial skill credit.");
                    }
                tutorialEpoch = epoch + 1; tutorialInstruction = null;
                tutorialEvidence.UnionWith(restored); TutorialLastCompletedFact = last;
                if (!Log("TUTORIAL_CREDITS_RESTORED", "training", restored.Count == 0 ? "NoRecordedMicroCredits" : "SameRunRecordedCredits",
                    after: new { instruction_epoch = tutorialEpoch, tutorial_group = Flow.TutorialCompleted, retained_facts = restored.ToArray(),
                        run_id = Record.run_id, session_id = Record.session_id }, source: "System")) return false;
                return FinalizeRecordedTutorialCredits();
            }
            catch (Exception ex) { Fail("tutorial_credit_restore_invalid", ex); return false; }
        }

        bool FinalizeRecordedTutorialCredits()
        {
            if (Flow?.IsTutorial != true || Flow.TutorialCompleted >= TutorialFacts.Length || Flow.Paused
                || !TutorialFacts[Flow.TutorialCompleted].All(tutorialEvidence.Contains)) return true;
            var previous = new ResearchSessionFlow(Flow.Order, Flow.State, Flow.TutorialCompleted, Flow.Paused, Flow.InspectPassed);
            var step = (TutorialStep)Flow.TutorialCompleted;
            if (!Flow.CompleteTutorialStep(step)) { Fail("tutorial_credit_restore_state", new InvalidDataException("Recorded credits disagree with saved flow state.")); return false; }
            if (!Log("TUTORIAL_STEP_RECONCILED", "training." + step, "FinalizedRecordedCredits", before: previous.State.ToString(),
                after: new { tutorial_completed = Flow.TutorialCompleted }, source: "System", reason: "durable_credit_before_saved_group_transition"))
            { Flow = previous; return false; }
            tutorialEvidence.Clear(); Save();
            return string.IsNullOrEmpty(Error);
        }

        void InterruptTutorialPractice(string reason)
        {
            if ((Flow?.TutorialCompleted != 2 && Flow?.TutorialCompleted != 3) || tutorialEvidence.Count == 0 || Logger?.State != HandsLabLogState.Active) return;
            if (!Log("TUTORIAL_PRACTICE_INTERRUPTED", Flow.TutorialCompleted == 2 ? "training.Handling" : "training.Reference", "ContinueCurrentAction",
                after: new { retained_facts = TutorialFacts[Flow.TutorialCompleted].Where(tutorialEvidence.Contains).ToArray(), pending_fact = TutorialFact }, source: "System", reason: reason)) return;
            InvalidateTutorialInstruction(reason); view?.CancelTutorialConfirmation(); Refresh();
        }

        // Test-only route emits explicit provenance and cannot advance a study session.
        public void DemoTutorialEvidence(string fact)
        {
            if (!IsTechnical) return;
            string action = Logger?.NewActionId();
            Log("DEMO_INPUT", "training." + fact, "Injected", source: "Tester", actionId: action);
            TutorialActionCommitted(fact, TutorialActionEpoch(fact), action, source: "Tester");
        }

        public void OpenReference(string input)
        {
            if (ReferenceOpen || !LocalUICommandsAllowed || (Flow?.IsTutorial != true && !TaskContentAvailable)) return;
            long instruction = TutorialActionEpoch("reference"); string action = Logger?.NewActionId();
            if (Flow?.IsTutorial != true && referenceChoiceTask != Flow?.CurrentTask) ReferenceSection = "";
            if (Flow?.IsTutorial == true && !ReferenceAvailable(ReferenceSection)) ReferenceSection = ResearchReferenceScheme.Active.First(ReferenceAvailable);
            else if (Flow?.IsTutorial != true && !ReferenceAvailable(ReferenceSection)) ReferenceSection = "";
            referenceNavigationHeld = true;
            ReferenceOpen = true; ReferenceMinimized = false; exposureSource = input;
            exposureSelectionKind = Flow?.IsTutorial == true ? "automatic_default" : "restore";
            string actor = input == "None" && IsTechnical ? "Tester" : "User";
            Log("REFERENCE_OPEN", "reference", "Opened", before: false,
                after: new { reference_open = ReferenceOpen, reference_minimized = ReferenceMinimized, language = Language, reference_section = ReferenceSection, incoming_binding_pending = true },
                source: actor, inputSource: input, actionId: action);
            TutorialActionCommitted("reference", instruction, action, input, source: actor); Refresh();
            if (view && view.IsReferenceActuallyVisible) OpenExposure(input);
        }
        public void CloseReference(string reason, string source, string input = "None")
        {
            if (!ReferenceOpen) return;
            long instruction = TutorialActionEpoch("close");
            string actor = source == "User" && input == "None" && IsTechnical ? "Tester" : source;
            Log("REFERENCE_CLOSE", "reference", "Closed", before: new { reference_open = ReferenceOpen, reference_minimized = ReferenceMinimized },
                after: new { reference_open = false, reference_minimized = false, language = Language, reference_section = ReferenceSection, viewer_at_action = view?.ReferenceViewer?.Metadata },
                source: actor, inputSource: input, reason: reason);
            CloseExposure(reason, source, input); ReferenceOpen = false; ReferenceMinimized = false;
            if (source == "User") TutorialActionCommitted("close", instruction, Logger?.NewActionId(), input, source: actor);
            view?.RefreshReference();
        }
        void SwitchReference(string section, string input)
        {
            if (!ReferenceOpen || ReferenceMinimized || section == ReferenceSection || !ReferenceAvailable(section)) return;
            long instruction = TutorialActionEpoch("switch"); string action = Logger?.NewActionId();
            CloseExposure("tab_changed", "User", input); string previous = ReferenceSection; ReferenceSection = section;
            if (Flow?.IsTutorial != true) referenceChoiceTask = Flow?.CurrentTask;
            exposureSource = input;
            exposureSelectionKind = "deliberate";
            string actor = input == "None" && IsTechnical ? "Tester" : "User";
            Log("INFORMATION_FORMAT_SELECTED", "reference." + section, "DeliberateSelection", before: previous,
                after: new { reference_scheme = ResearchReferenceScheme.Name, format = section, deliberate = true, language = Language, selection_kind = "deliberate", viewer_before_action = view?.ReferenceViewer?.Metadata, incoming_binding_pending = true },
                source: actor, inputSource: input, actionId: action);
            view?.RefreshReference();
            if (view && view.IsReferenceActuallyVisible) OpenExposure(input);
            TutorialActionCommitted("switch", instruction, action, input, source: actor);
        }
        void MinimizeReference(string input)
        {
            if (!ReferenceOpen || ReferenceMinimized) return;
            CloseExposure("minimized", "User", input); ReferenceMinimized = true;
            Log("PANEL_MINIMIZE", "reference", "Minimized", before: false,
                after: new { reference_minimized = ReferenceMinimized, language = Language, reference_section = ReferenceSection, viewer_at_action = view?.ReferenceViewer?.Metadata },
                source: input == "None" && IsTechnical ? "Tester" : "User", inputSource: input); view.Refresh();
        }
        void RestoreReference(string input)
        {
            if (!ReferenceOpen || !ReferenceMinimized) return;
            long instruction = TutorialActionEpoch("restore"); string action = Logger?.NewActionId();
            referenceNavigationHeld = true;
            ReferenceMinimized = false; exposureSource = input; exposureSelectionKind = "restore";
            string actor = input == "None" && IsTechnical ? "Tester" : "User";
            Log("PANEL_RESTORE", "reference", "Restored", before: true,
                after: new { reference_minimized = ReferenceMinimized, language = Language, reference_section = ReferenceSection, viewer_at_action = view?.ReferenceViewer?.Metadata },
                source: actor, inputSource: input, actionId: action); TutorialActionCommitted("restore", instruction, action, input, source: actor); Refresh();
            if (view && view.IsReferenceActuallyVisible) OpenExposure(input);
        }
        void OpenExposure(string input)
        {
            if (exposureAction != null || !ReferenceOpen || ReferenceMinimized || !ReferenceAvailable(ReferenceSection)
                || !view || !view.IsReferenceActuallyVisible || Flow?.Paused == true) return;
            exposureAction = Logger?.NewActionId(); exposureSource = input; exposureStart = Logger?.Timestamp ?? 0;
            exposurePhase = Flow?.IsTutorial == true ? "Tutorial" : "Experiment";
            exposureTask = Flow?.IsTutorial == true ? "training" : Flow?.CurrentTask ?? "session";
            bool automatic = exposureSelectionKind == "system_resume" || exposureSelectionKind == "automatic_default";
            exposureSource = automatic ? "None" : input;
            Log("INFORMATION_OPEN", "reference." + ReferenceSection, "Opened", after: new { reference_scheme = ResearchReferenceScheme.Name,
                actual_content_visible = true, visibility_verified = true, selection_kind = exposureSelectionKind,
                default_presentation = exposureSelectionKind == "automatic_default", video_playback = false, viewer = view.ReferenceViewer?.Metadata, language = Language, reference_section = ReferenceSection },
                source: automatic ? "System" : input == "None" && IsTechnical ? "Tester" : "User", inputSource: automatic ? "None" : input, actionId: exposureAction);
            exposureSelectionKind = "system_resume";
        }
        void ReferenceVisibilityChanged(bool visible, string reason)
        {
            if (visible) OpenExposure(exposureSource ?? "None");
            else if (exposureAction != null)
            {
                CloseExposure(reason, "System", "None");
                Log("INFORMATION_CONCEALED", "reference." + ReferenceSection, "SystemConcealed", source: "System", reason: reason);
            }
        }
        void CloseExposure(string reason, string source, string input)
        {
            if (exposureAction == null) return;
            string closingAction = exposureAction; exposureAction = null;
            Log("INFORMATION_CLOSE", "reference." + ReferenceSection, reason, after: new { exposure_seconds = (Logger?.Timestamp ?? exposureStart) - exposureStart, measurement = "panel_exposure_not_eye_gaze", opening_input_source = exposureSource, language = Language, reference_section = ReferenceSection, viewer_at_action = view?.ReferenceViewer?.Metadata },
                source: source == "User" && input == "None" && IsTechnical ? "Tester" : source, inputSource: input, actionId: closingAction,
                phaseOverride: exposurePhase, taskIdOverride: exposureTask, reason: reason);
        }
        public bool ReferenceAvailable(string section) => ResearchReferenceScheme.Contains(section)
            && (Flow?.IsTutorial == true || TaskContentAvailable) &&
            (IsTechnical ? ResearchReferenceViewer.Available(section, ReaderResource) : CurrentPort && CurrentPort.IsReadyForStudy);
        public string ReaderResource => ResearchReferenceViewer.ResourceFor(Flow?.IsTutorial == true ? "T" : Flow?.CurrentTask, Language);
        public void ReferenceMediaEvent(string eventName, object after, string source = "User", string input = "None", string boundSection = null)
        {
            Log(eventName, "reference." + (boundSection ?? ReferenceSection), "Recorded", after: after, source: source, inputSource: input);
            view?.NotifyReferenceVisibility("media_" + eventName);
        }
        public string ReferenceText => !ReferenceAvailable(ReferenceSection) ? "" : Flow?.IsTutorial == true
            ? ResearchStrings.Get("reference.training." + ReferenceSection + (ReferenceSection == "step_by_step" && TutorialUsesHands ? ".hand" : ""), Language)
            : CurrentPort ? CurrentPort.Reference(ReferenceSection, Language) : "";

        void TaskBehavior(string eventName, string target, object before, object after)
        {
            var lifecycle = after as ResearchTaskPort.AttemptPayload;
            bool terminal = lifecycle != null && lifecycle.SystemTerminal;
            if (Flow == null || (!terminal && (!Flow.CanInteract || !Flow.IsTaskActive))) return;
            var payload = after as Dictionary<string, object>;
            bool error = eventName == "SYSTEM_ERROR";
            string input = payload != null && payload.TryGetValue("input_source", out var supplied) ? supplied as string : "None";
            if (!new[] { "None", "Controller", "HandTracking", "SimulatedController" }.Contains(input) || string.IsNullOrWhiteSpace(target))
            { Fail("invalid_task_event", new ArgumentException("Task adapter supplied invalid target or input source")); return; }
            object snapshot = lifecycle != null ? lifecycle.TaskState : payload != null && payload.TryGetValue("task_state", out var observed) ? observed : null;
            Log(eventName, target, "Recorded", before, after, source: error || terminal ? "System" : "User", inputSource: input,
                actionId: lifecycle?.AttemptId, taskState: snapshot, taskIdOverride: lifecycle?.TaskId, phaseOverride: lifecycle != null ? "Experiment" : null);
            if (error) { Error = "Task adapter reported an error; contact the researcher."; BlockInteractions("task_adapter_error"); view?.Refresh(); }
        }

        IEnumerator MoveToStation(int index)
        {
            if (!string.IsNullOrEmpty(Error)) yield break;
            if (!origin || index < 0 || index >= stations.Length || !stations[index])
            {
                Fail("station_unavailable", new InvalidOperationException("Configured station is missing")); yield break;
            }
            Transitioning = true; CloseReference("station_transition", "System"); BlockInteractions("station_transition");
            if (view) yield return view.Fade(true);
            if (origin && index >= 0 && index < stations.Length && stations[index])
            {
                var before = PoseState(); string action = Logger?.NewActionId();
                Log("TELEPORT_INITIATED", "station." + index, "SystemTransition", before: before, source: "System", actionId: action);
                var destination = stations[index];
                origin.MatchOriginUpCameraForward(Vector3.up, destination.forward);
                origin.MoveCameraToWorldLocation(destination.position + Vector3.up * origin.CameraInOriginSpaceHeight);
                Log("TELEPORT_COMPLETED", "station." + index, "SystemTransition", before, PoseState(), source: "System", actionId: action);
                GetComponent<ResearchMovementSampler>()?.Boundary("station_teleport");
                physicalHeadStart = origin.Camera.transform.localPosition;
                CurrentStationIndex = index;
                MeasureStationEye("System", "station_arrival");
            }
            ReferenceSection = Flow?.IsTutorial == true ? ResearchReferenceScheme.Active[0] : ""; view?.RecenterAll(false);
            if (view) yield return view.Fade(false);
            Transitioning = false; Refresh(); view?.ShowArrival(index == 0 ? "training" : index == 1 ? "A" : "B");
        }

        object PoseState() => origin ? new { position = V(origin.transform.position), yaw = origin.transform.eulerAngles.y } : null;

        // ---- Fixed-UI station anchors (ResearchStationAnchors) --------------------------------------------------------------
        public int CurrentStationIndex { get; private set; } = -1;
        public double StationEyeHeight { get; private set; } = double.NaN;
        public string StationEyeSource { get; private set; } = "";
        /// <summary>Station frame for anchored panels; false when no station/session (callers fall back to head-relative placement).</summary>
        internal bool TryStationFrame(out Transform station, out int index, out string layout, out double eyeHeight)
        {
            index = CurrentStationIndex; station = index >= 0 && index < stations.Length ? stations[index] : null;
            layout = index == ResearchStationAnchors.ComputerStation ? Record?.task_a_layout : null;
            eyeHeight = ResearchStationAnchors.ClampEyeHeight(StationEyeHeight);
            return station && Flow != null && Record != null;
        }
        // Eye height above the station floor from the tracked head. Measured on arrival (System) and re-measurable by the researcher
        // before task timing; never during an active task, so the anchored pose stays fixed within a task.
        bool MeasureStationEye(string source, string reason)
        {
            if (!origin || !origin.Camera || CurrentStationIndex < 0 || CurrentStationIndex >= stations.Length || !stations[CurrentStationIndex]) return false;
            double measured = origin.Camera.transform.position.y - stations[CurrentStationIndex].position.y;
            double previous = StationEyeHeight;
            StationEyeHeight = measured; StationEyeSource = source;
            Log("STATION_ANCHOR_CALIBRATED", "station." + CurrentStationIndex, source == "Researcher" ? "ResearcherCalibrated" : "MeasuredOnArrival",
                before: double.IsNaN(previous) ? null : (object)new { eye_height = previous },
                after: new { anchors_version = ResearchStationAnchors.Version, station = ResearchStationAnchors.StationName(CurrentStationIndex),
                    measured_eye_height = measured, applied_eye_height = ResearchStationAnchors.ClampEyeHeight(measured),
                    clamped = ResearchStationAnchors.ClampEyeHeight(measured) != measured, task_a_layout = Record?.task_a_layout, ui_policy = ResearchUIPolicy.Active },
                source: source, reason: reason);
            return true;
        }
        /// <summary>Researcher re-calibration before task timing (Welcome/Ready states only). Re-anchors visible panels.</summary>
        public string CalibrateStationAnchor(string source = "Researcher")
        {
            if (Flow == null || Record == null) return "No open session.";
            if (Flow.Paused || Transitioning || !string.IsNullOrEmpty(Error)) return "Session is paused, transitioning or in error.";
            if (Flow.State != ResearchFlowState.TutorialWelcome && Flow.State != ResearchFlowState.Task1Ready && Flow.State != ResearchFlowState.Task2Ready)
                return "Calibration is allowed only before timing (Welcome, Task 1 Ready, Task 2 Ready); now " + Flow.State + ".";
            if (!HeadsetReady) return "Headset is not tracked.";
            if (!MeasureStationEye(source, "researcher_calibration_before_timing")) return "Station unavailable.";
            view?.RecenterAll(false);
            return null;
        }
        static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

        // Researcher surfaces poll this twice a second; re-parsing the whole log each time drove full GC pauses (~1 s in the Editor).
        string summaryKey, summaryCache;
        public string SummaryText(string language = "English")
        {
            if (Record == null) return ResearchStrings.Get("summary.empty", language);
            string key = language + ":" + Record.session_id + ":" + Record.session_state + ":" + Record.revision + ":" + Flow?.State + ":" + Flow?.Paused
                + ":" + (Logger != null && File.Exists(Logger.EventsPath) ? new FileInfo(Logger.EventsPath).Length : -1);
            if (key == summaryKey) return summaryCache;
            summaryKey = key;
            return summaryCache = BuildSummaryText(language);
        }

        string BuildSummaryText(string language)
        {
            string S(string key) => ResearchStrings.Get("summary." + key, language);
            var savedRows = new List<JObject>();
            if (Logger != null && File.Exists(Logger.EventsPath))
                using (var reader = new StreamReader(new FileStream(Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                    for (string line; (line = reader.ReadLine()) != null;) savedRows.Add(JObject.Parse(line));
            var rows = savedRows.ToArray();
            var result = new System.Text.StringBuilder();
            if (Record.test_data) result.AppendLine(S("test"));
            result.AppendLine(S("participant") + Record.participant_id + "    " + S("order") + (Record.task_order == TaskOrder.AB ? "A -> B" : "B -> A"));
            result.AppendLine(S("input") + Record.input_mode + "    " + S("session") + Record.session_state);
            result.AppendLine();
            foreach (var task in new[] { "A", "B" })
            {
                var events = rows.Where(r => (string)r["task_id"] == task).ToArray();
                var start = events.FirstOrDefault(r => (string)r["event"] == "TASK_STARTED");
                var end = events.LastOrDefault(r => (string)r["event"] == "TASK_COMPLETED");
                var terminal = events.LastOrDefault(r => (string)r["event"] == "TASK_ENDED");
                var ended = events.LastOrDefault(r => (string)r["event"] == "TASK_ENDED" || (string)r["event"] == "TASK_TERMINATED");
                string endReason = (string)ended?["after"]?["end_reason"] ?? (string)ended?["reason"];
                bool completed = terminal != null ? (bool?)terminal["after"]?["completed"] == true : end != null;
                string duration = "-";
                double? seconds = null;
                if (terminal != null && completed)
                    seconds = (double?)terminal["after"]?["active_elapsed_s"];
                else if (terminal == null && start != null && end != null)
                {
                    double startedAt = (double)start["timestamp"], completedAt = (double)end["timestamp"];
                    double paused = events.Where(r => (string)r["event"] == "SESSION_RESUMED").Sum(r =>
                    {
                        double resumedAt = (double)r["timestamp"], pausedSeconds = (double?)(r["after"] as JObject)?["paused_seconds"] ?? 0;
                        return Math.Max(0, Math.Min(completedAt, resumedAt) - Math.Max(startedAt, resumedAt - pausedSeconds));
                    });
                    seconds = Math.Max(0, completedAt - startedAt - paused);
                }
                if (seconds.HasValue && seconds >= 0 && !double.IsNaN(seconds.Value) && !double.IsInfinity(seconds.Value))
                {
                    var elapsed = TimeSpan.FromSeconds(seconds.Value);
                    duration = ((int)elapsed.TotalMinutes).ToString("00") + ":" + elapsed.Seconds.ToString("00");
                }
                result.AppendLine(S("task") + task + "    " + (completed ? S("completed") : ended != null ? S("incomplete") + (endReason ?? "unknown") + ")" : start != null ? S("progress") : S("not_started")) + "    " + duration);
            }
            result.AppendLine();
            result.AppendLine(S("reference") + rows.Count(r => (string)r["event"] == "INFORMATION_OPEN") + "    " + S("examine") + rows.Count(r => (string)r["event"] == "EXAMINE"));
            result.AppendLine(S("inspect") + rows.Count(r => (string)r["event"] == "INSPECT") + "    " + S("fail") + rows.Count(r => (string)r["event"] == "INSPECT_FAIL"));
            result.AppendLine(S("nasa") + rows.Where(r => (string)r["event"] == "CHECKPOINT_COMPLETED").Select(r => (string)r["task_id"]).Distinct().Count() + " / 2");
            result.AppendLine(S("logger") + (Logger != null && Logger.DataComplete ? S("saved") : Logger != null && Logger.State == HandsLabLogState.Active ? S("recording") : S("review")));
            return result.ToString();
        }

        public bool ExportSession()
        {
            return QueueExport("operator_export", Logger?.DataComplete == true ? "finalized" : "partial");
        }

        // True when the snapshot is frozen and handed to the export worker; the bundle path arrives in LastExportPath.
        bool QueueExport(string reason, string lifecycle)
        {
            if (Logger == null || Record == null) return false;
            try
            {
                TrackExport(ResearchExportQueue.Snapshot(DataRoot, Logger, Record, reason, lifecycle,
                    JObject.FromObject(new { application = Application.identifier, software_version = Application.version,
                        execution_environment = Application.isEditor ? "Editor" : "QuestStandalone", render = ResearchRenderExperiment.CurrentSettings(),
                        test_data = Record.test_data, hardware_verified = false,
                        simulated_presence = GetComponent<ResearchTechnicalScenario>() ? (object)GetComponent<ResearchTechnicalScenario>().SimulatedPresence : "unknown",
                        apk_sha256 = (string)null, content_version = "research.flow.1" })), Record.run_id);
                return true;
            }
            catch (Exception ex) { ExportDeliveryError = ex.Message; return false; }
        }

        public string SavedRunExportError { get; private set; } = "";
        public void TrackExport(System.Threading.Tasks.Task<string> task, string run) => pendingExports.Add((task, run));

        void ApplyExports()
        {
            while (pendingExports.Count > 0 && pendingExports[0].task.IsCompleted)
            {
                var (task, run) = pendingExports[0];
                pendingExports.RemoveAt(0);
                string failure = task.IsCompletedSuccessfully ? null : (task.Exception?.GetBaseException().Message ?? "Export snapshot cancelled");
                if (Record != null && run == Record.run_id)
                {
                    if (failure == null) { LastExportPath = task.Result; ExportDeliveryError = ""; }
                    else ExportDeliveryError = failure;
                }
                else SavedRunExportError = failure == null ? "" : run + ": " + failure;
                if (failure != null) Debug.LogError("[ResearchExport] " + run + ": " + failure);
            }
        }

        /// <summary>Blocks until queued snapshots finish. For shutdown and stopped-Editor fixtures, never during a session frame.</summary>
        public bool WaitForExports(int timeoutMilliseconds)
        {
            bool done = System.Threading.Tasks.Task.WaitAll(pendingExports.Select(p => (System.Threading.Tasks.Task)p.task).ToArray(), timeoutMilliseconds);
            ApplyExports();
            return done;
        }

        void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused) SuspendApplication("application_paused");
        }
        void OnApplicationFocus(bool focused)
        {
            applicationUnfocused = !focused;
            if (!focused) SuspendApplication("application_focus_lost");
        }
        void SuspendApplication(string reason)
        {
            if (Flow == null || Record?.session_state != RegistryState.InProgress) return;
            CloseReference(reason, "System");
            if (Pause("application")) { Log("SESSION_PAUSED", "application", "Paused", after: PauseTiming(), source: "System", reason: reason); Save(); }
            BlockInteractions(reason); view?.Refresh();
        }
        void OnDisable()
        {
            if (shuttingDown) return; shuttingDown = true;
            if (FloorPolicy) FloorPolicy.Changed -= FloorReadinessChanged;
            if (view) view.ReferenceVisibilityChanged -= ReferenceVisibilityChanged;
            CloseReference("runtime_stopped", "System"); BlockInteractions("runtime_stopped");
            foreach (var pair in objectListeners) if (pair.Key && pair.Key.grabbable) pair.Key.grabbable.WhenPointerEventRaised -= pair.Value;
            foreach (var pair in portListeners) if (pair.Key) pair.Key.Behavior -= pair.Value;
            foreach (var guard in guards) if (guard) guard.StateChanged -= TutorialInputChanged;
            GetComponent<ResearchMovementSampler>()?.FlushAndStop("runtime_stopped");
            if (Logger != null) { Logger.WriteFailed -= LoggerFailed; Logger.Dispose(); }
            // Let an in-flight snapshot reach its queue job; the frozen prefixes do not depend on the closed writer.
            if (pendingExports.Count > 0 && !WaitForExports(15000)) Debug.LogError("[ResearchExport] Snapshot still running at shutdown; the run stays on device for a saved-run export.");
        }
    }
}
