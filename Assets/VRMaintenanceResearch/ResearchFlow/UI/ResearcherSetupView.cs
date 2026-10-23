using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Existing operator setup. Quest access is technical and pre-session only.</summary>
    public sealed class ResearcherSetupView : MonoBehaviour
    {
        public ResearchSessionFlowController controller;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private TMP_Dropdown group, language, order, input;
        [SerializeField] private ResearcherParticipantDropdown participant;
        [SerializeField] private TMP_Text status, error, summary, startLabel, abortLabel;
        [SerializeField] private Button start, resume, abort, export;
        private IReadOnlyList<ParticipantRecord> records = Array.Empty<ParticipantRecord>();
        private string recordSignature = "", localError = "";
        private float nextRefresh;
        private float nextOperatorPoll;
        public string OperatorDirectoryOverride { get; set; }
        public string OperatorDirectory => string.IsNullOrEmpty(OperatorDirectoryOverride)
            ? Path.Combine(Application.persistentDataPath, "OperatorControl") : Path.GetFullPath(OperatorDirectoryOverride);
        public string OperatorStatus { get; private set; } = "No operator authorization loaded";
        private Canvas canvas;
        private ResearchFlowView questOwner;
        private Transform desktopParent;
        private string selectedRecordId;
        private Button back;
        private TMP_Text lockedInputLabel;
        [SerializeField] private TMP_Text setupHeading, setupScope;
        private TMP_Text questBrand;
        [SerializeField] private TMP_Text[] setupFieldLabels = new TMP_Text[5];
        private string R(string key) => ResearchStrings.Get("researcher." + key, controller ? controller.Language : "English");
        private bool confirmAbort, confirmInvalid;
        private Button stop, details;
        private TMP_Text delivery;
        private bool detailsExpanded;
        private ResearchSessionFlowController detailsController;
        private string detailsIdentity;
        private void ToggleDetails() { detailsExpanded = !detailsExpanded; Refresh(); }
        private void StopSession() => Invoke(() => controller.ResearcherStop());
        private string DeliveryCaption()
        {
            switch (controller.ExportDeliveryStatus)
            {
                case "saved": return R("delivery_saved");
                case "queued": return R("delivery_queued");
                case "received": return R("delivery_received");
                default: return R("delivery_attention");
            }
        }
        private ResearchSessionFlowController endingController;
        private string endingIdentity;
        private string EndingIdentity => controller?.Record == null || string.IsNullOrWhiteSpace(controller.Record.session_id) || string.IsNullOrWhiteSpace(controller.Record.run_id)
            || string.IsNullOrWhiteSpace(controller.Record.participant_id) ? null : controller.Record.participant_id + "/" + controller.Record.session_id + "/" + controller.Record.run_id;
        private bool EndingRequestMatches => endingIdentity != null && ReferenceEquals(controller, endingController) && EndingIdentity == endingIdentity && controller?.Record?.session_state == RegistryState.InProgress;

        private void ClearEndingRequest() { confirmAbort = confirmInvalid = false; endingController = null; endingIdentity = null; }
        private void ToggleEndingRequest(bool invalidate)
        {
            bool arm = !(invalidate ? confirmInvalid : confirmAbort);
            ClearEndingRequest();
            if (!arm || EndingIdentity == null || controller?.Record?.session_state != RegistryState.InProgress) return;
            confirmInvalid = invalidate; confirmAbort = !invalidate;
            endingController = controller; endingIdentity = EndingIdentity;
        }
        private UnityEngine.UI.Button invalid;
        // One per researcher termination category; shown to pick the ending when aborting, and to amend it afterwards.
        private readonly UnityEngine.UI.Button[] reasonButtons = new UnityEngine.UI.Button[4];
        private TMP_Text reasonCaption;
        static readonly string[] ReasonLabels = { "Withdrawal", "Safety stop", "Technical failure", "Other" };
        private readonly ParticipantGroup[] groups = { ParticipantGroup.Thai, ParticipantGroup.Japanese, ParticipantGroup.Dev, ParticipantGroup.Test, ParticipantGroup.Pilot };

        public static ResearcherSetupView Build(Transform parent, TMP_FontAsset font)
        {
            var root = new GameObject("Researcher Display 2", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(parent, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = 1;
            // No second monitor means no researcher canvas; the Editor console remains available.
            canvas.enabled = Display.displays.Length > 1;
            canvas.sortingOrder = 10;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 1000);
            scaler.matchWidthOrHeight = 1;
            var view = root.AddComponent<ResearcherSetupView>();
            view.font = font;
            view.Construct();
            view.ApplyVisualTheme();
            view.Wire();
            root.AddComponent<ResearcherDesktopPointer>().researcherCanvas = canvas;
            return view;
        }

        private void Awake()
        {
            canvas = GetComponent<Canvas>();
            if (canvas != null && !questOwner) { canvas.targetDisplay = 1; canvas.enabled = Display.displays.Length > 1; }
            if (start != null) ApplyVisualTheme();
        }

        private void OnEnable() => Wire();
        private void OnDisable() => Unwire();
        private void Wire()
        {
            Unwire();
            if (start == null) return; // Build assigns the serialized references after AddComponent.
            start.onClick.AddListener(StartSelected);
            resume.onClick.AddListener(ResumeSession);
            abort.onClick.AddListener(AbortSelected);
            export.onClick.AddListener(ExportSession);
            group.onValueChanged.AddListener(GroupChanged);
            participant.onValueChanged.AddListener(ParticipantSelected);
        }

        private void Unwire()
        {
            if (start == null) return;
            start.onClick.RemoveListener(StartSelected);
            resume.onClick.RemoveListener(ResumeSession);
            abort.onClick.RemoveListener(AbortSelected);
            export.onClick.RemoveListener(ExportSession);
            group.onValueChanged.RemoveListener(GroupChanged);
            participant.onValueChanged.RemoveListener(ParticipantSelected);
        }

        private void ResumeSession() => InvokeOperator("resume", controller?.Record, () => controller.ResearcherResume());
        private void ExportSession() => InvokeOperator("export", controller?.Record, () => localError = controller.ExportSession() ? "" : !string.IsNullOrEmpty(controller.ExportDeliveryError) ? controller.ExportDeliveryError : R("delivery_attention"));
        private void GroupChanged(int index)
        {
            recordSignature = ""; ClearEndingRequest();
            if (index >= 0 && index < groups.Length)
            {
                var g = groups[index];
                if (g == ParticipantGroup.Thai)
                {
                    language.SetValueWithoutNotify((int)ParticipantLanguage.Thai);
                    if (controller) controller.SetLanguage("Thai");
                }
                else if (g == ParticipantGroup.Japanese)
                {
                    language.SetValueWithoutNotify((int)ParticipantLanguage.Japanese);
                    if (controller) controller.SetLanguage("Japanese");
                }
            }
            Refresh();
        }

        public bool ShowOnQuest(ResearchFlowView owner)
        {
            if (!owner || owner.controller != controller || !controller || !controller.TechnicalSelfSetup
                || controller.Record != null || controller.Flow != null || !string.IsNullOrEmpty(controller.Error)) return false;
            if (!canvas) canvas = GetComponent<Canvas>();
            if (!questOwner) desktopParent = transform.parent;
            questOwner = owner;
            transform.SetParent(owner.mainPanel.transform, false);
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(920, 620); rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one * .82f; rect.localRotation = Quaternion.identity;
            
            // On Quest in WorldSpace, disable child canvas & GraphicRaycaster
            // so all UI children share owner.mainPanel's root Canvas and PointableCanvasModule.
            if (canvas) canvas.enabled = false;
            var gr = GetComponent<GraphicRaycaster>();
            if (gr) gr.enabled = false;

            if (!back) back = Button(start.transform.parent, "Back", new Vector2(360, 260), new Vector2(100, 48), out _);
            back.onClick.RemoveAllListeners(); back.onClick.AddListener(owner.CloseResearcherSetup);
            back.gameObject.SetActive(true); ResearchUITheme.Button(back, ResearchButtonVariant.Secondary);
            ApplyVisualTheme(); Refresh();
            return true;
        }

        public void ReturnToDesktop()
        {
            questOwner = null;
            transform.SetParent(desktopParent, false);
            if (canvas)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.targetDisplay = 1;
                canvas.enabled = Display.displays.Length > 1;
            }
            var gr = GetComponent<GraphicRaycaster>();
            if (gr) gr.enabled = true;
            transform.localScale = Vector3.one;
            if (back) back.gameObject.SetActive(false);
            foreach (var dropdown in new[] { group, participant, language, order, input })
                if (dropdown && dropdown.template && dropdown.template.GetComponent<Canvas>())
                    dropdown.template.GetComponent<Canvas>().targetDisplay = 1;
        }

        private void Construct()
        {
            var panel = Box(transform, "Setup", new Vector2(900, 950), Vector2.zero, new Color(.035f, .055f, .085f));
            Text(panel, "Researcher Setup", new Vector2(820, 50), new Vector2(0, 420), 30);
            Text(panel, "RESEARCHER DISPLAY  |  DEV / TEST / PILOT are test data", new Vector2(820, 36), new Vector2(0, 374), 17);
            group = Dropdown(panel, "Participant Group", 314, new[] { "Thai", "Japanese", "DEV - test data" });
            participant = (ResearcherParticipantDropdown)Dropdown(panel, "Participant ID", 256, new[] { "Select participant", "Create next ID" }, true);
            language = Dropdown(panel, "Language", 198, new[] { "Thai", "Japanese", "English" });
            order = Dropdown(panel, "Task Order", 140, Cells);
            input = Dropdown(panel, "Input Mode", 82, new[] { "Controller", "HandTracking", "TechnicalBoth - test only" });
            status = Text(panel, "Waiting for controller", new Vector2(820, 60), new Vector2(0, 16), 19);
            start = Button(panel, "Start Session", new Vector2(-215, -58), new Vector2(390, 48), out startLabel);
            resume = Button(panel, "Researcher Resume", new Vector2(215, -58), new Vector2(390, 48), out _);
            error = Text(panel, "", new Vector2(820, 52), new Vector2(0, -119), 17);
            error.color = new Color(1, .65f, .45f);
            summary = ScrollText(panel, new Vector2(820, 218), new Vector2(0, -264));
            export = Button(panel, "Export Session Log", new Vector2(-215, -415), new Vector2(390, 46), out _);
            abort = Button(panel, "Abort Session", new Vector2(215, -415), new Vector2(390, 46), out abortLabel);
        }

        private void Update()
        {
            // The fixed local file route also runs when Display 2 is hidden. No participant UI or ADB study unlock.
            if (Time.unscaledTime >= nextOperatorPoll) { nextOperatorPoll = Time.unscaledTime + .5f; PollOperatorCommand(); }
            // Hidden on Quest (no Display 2). Refresh re-reads the registry and re-parses the whole
            // events.jsonl via SummaryText: a 50-150 ms main-thread hitch every 0.5 s, growing with
            // the session (Perfetto 2026-09-24, perf-audit-20260924). Only a visible canvas needs it.
            if (!canvas) canvas = GetComponent<Canvas>();
            if (canvas && !canvas.enabled) return;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .5f;
            Refresh();
        }

        public void Refresh()
        {
            if ((confirmAbort || confirmInvalid) && !EndingRequestMatches) ClearEndingRequest();
            if (!ReferenceEquals(detailsController, controller) || detailsIdentity != EndingIdentity)
            {
                detailsExpanded = false; detailsController = controller; detailsIdentity = EndingIdentity;
            }
            if (questOwner && (!controller || !controller.TechnicalSelfSetup || controller.Record != null || controller.Flow != null))
                questOwner.CloseResearcherSetup();
            if (controller == null || controller.Registry == null || participant == null) return;
            try
            {
                var attachedRecord = controller.Record;
                if (attachedRecord != null) group.SetValueWithoutNotify(Array.IndexOf(groups, attachedRecord.group));
                var selectedId = attachedRecord?.participant_id ?? Selected?.participant_id;
                var latest = controller.Registry.List(groups[group.value]);
                var signature = controller.Language + "|" + string.Join("|", latest.Select(r => r.participant_id + ":" + r.session_state + ":" + r.revision));
                if (signature != recordSignature || participant.options.Count != latest.Count + 2)
                {
                    records = latest;
                    recordSignature = signature;
                    participant.ClearOptions();
                    participant.AddOptions(new[] { R("select") }.Concat(records.Select(r => r.participant_id + "    " + Status(r.session_state))).Concat(new[] { R("create") }).ToList());
                    participant.allowed = new[] { false }.Concat(records.Select(r => Selectable(r))).Concat(new[] { true }).ToArray();
                    var index = records.ToList().FindIndex(r => r.participant_id == selectedId && (attachedRecord != null || Selectable(r)));
                    if (index < 0) index = records.ToList().FindIndex(Selectable);
                    participant.SetValueWithoutNotify(index >= 0 ? index + 1 : 0);
                }
                if (attachedRecord != null) participant.SetValueWithoutNotify(records.ToList().FindIndex(r => r.participant_id == attachedRecord.participant_id) + 1);
                var record = attachedRecord ?? Selected;
                if (record != null && record.participant_id != selectedRecordId)
                {
                    selectedRecordId = record.participant_id;
                    order.SetValueWithoutNotify(CellIndex(record));
                }
                bool running = controller.Record != null && controller.Record.session_state == RegistryState.InProgress;
                bool resuming = record != null && record.session_state == RegistryState.InProgress;
                if (resuming || attachedRecord != null)
                {
                    language.SetValueWithoutNotify((int)record.language);
                    input.SetValueWithoutNotify((int)record.input_mode);
                    order.SetValueWithoutNotify(CellIndex(record));
                }
                bool technical = ResearchParticipantRegistry.IsTechnical(groups[group.value]);
                group.interactable = attachedRecord == null;
                participant.interactable = attachedRecord == null;
                language.interactable = attachedRecord == null && !resuming && technical;
                input.interactable = attachedRecord == null && !resuming && technical && !questOwner;
                order.interactable = attachedRecord == null && !resuming;
                if ((!technical || questOwner) && !resuming && attachedRecord == null) input.SetValueWithoutNotify(0);
                start.interactable = !running && record != null && Selectable(record) && controller.DeviceReady && controller.LoggerReady
                    && controller.ResearcherStartBlocker(record, OperatorDirectory) == null
                    && (!questOwner || (ExperimentInputMode)input.value == ExperimentInputMode.Controller);
                startLabel.text = "▶  " + (resuming ? R("resume_saved") : R("start"));
                var flow = controller.Flow;
                resume.interactable = running && controller.ResearcherResumeBlocker() == null;
                abort.interactable = running;
                abortLabel.text = confirmAbort ? R("cancel") : R("abort");
                export.interactable = controller.Record != null;
                string Badge(string value, bool ready) => "<color=#" + ColorUtility.ToHtmlStringRGB(ready ? ResearchUITheme.Success : new Color(.48f, .27f, .06f)) + ">" + value + "</color>";
                // Training is intentionally pending before a new session; displaying that as a
                // startup readiness failure confuses the researcher. Keep it on active-session views.
                string trainingStatus = R("training") + "  " + Badge(controller.Record?.training_status == "Passed" ? R("passed")
                    : controller.Record?.training_status == null || controller.Record.training_status == "Pending" ? R("pending")
                    : controller.Record.training_status, controller.Record?.training_status == "Passed");
                status.text = (questOwner && attachedRecord == null ? "" : trainingStatus + "     ")
                    + R("device") + "  " + Badge(controller.DeviceReady ? R("ready") : R("not_ready"), controller.DeviceReady)
                    + "     " + R("logger") + "  " + Badge(controller.LoggerReady ? R("ready") : R("not_ready"), controller.LoggerReady);
                // Assignment goes above readiness; it must not replace the Device/Logger reasons Start depends on.
                if (attachedRecord == null && record != null)
                {
                    var (previewOrder, previewLayout) = resuming ? (record.task_order, record.task_a_layout)
                        : controller.Registry.PreviewAssignment(groups[group.value], CellOrder(order.value), CellLayout(order.value));
                    status.text = record.participant_id + "  |  " + ((ParticipantLanguage)language.value) + "  |  " + previewOrder + "_" + previewLayout
                        + (resuming ? " " + R("saved") : order.value == 0 ? " (" + R("auto_picks") + ")" : "") + "  |  " + ((ExperimentInputMode)input.value)
                        + "  |  " + (technical ? R("test_data") : R("study")) + "\n" + status.text;
                }
                else if (attachedRecord != null && attachedRecord.session_state == RegistryState.InProgress)
                    status.text = attachedRecord.participant_id + "  |  " + attachedRecord.language + "  |  " + attachedRecord.task_order + "_" + attachedRecord.task_a_layout
                        + "  |  " + (flow == null ? "" : flow.State + (flow.Paused ? " (" + R("paused") + ")" : "")) + "\n" + status.text;
                if (attachedRecord != null && attachedRecord.session_state == RegistryState.Completed)
                    status.text = R("session_label") + "  " + Badge(R("completed"), true) + "     " + R("log") + "  " + Badge(controller.Logger != null && controller.Logger.DataComplete ? R("saved") : R("review"), controller.Logger != null && controller.Logger.DataComplete);
                string resumeBlocker = running ? controller.ResearcherResumeBlocker() : null;
                bool activeWithoutResume = running && flow?.IsTaskActive == true && !flow.Paused
                    && resumeBlocker == "Nothing to resume: participant is in " + flow.State + ".";
                string blocker = attachedRecord != null && attachedRecord.session_state != RegistryState.InProgress ? null : running ? (resume.interactable || activeWithoutResume ? null : R("resume_unavailable") + LocalizeBlocker(resumeBlocker))
                    : start.interactable ? null : R("start_unavailable") + LocalizeBlocker(controller.ResearcherStartBlocker(record, OperatorDirectory) ?? "Quest setup requires Controller mode.");
                error.text = !string.IsNullOrEmpty(controller.Error) ? controller.Error : !string.IsNullOrEmpty(localError) ? localError : blocker ?? "";
                summary.text = controller.Record == null ? R("empty") : detailsExpanded ? controller.SummaryText(controller.Language) : "";
                if (attachedRecord != null)
                {
                    status.text = attachedRecord.participant_id + "  |  " + (running
                        ? string.Format(R("live_task"), flow?.CurrentTask ?? "-", controller.TaskActiveElapsed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture), flow?.Paused == true ? R("paused") : R("in_progress"))
                        : R("state_" + attachedRecord.session_state.ToString().ToLowerInvariant()));
                    delivery.text = DeliveryCaption();
                }
                LayoutSession(controller.Record != null);
                ResearchUITheme.Button(abort, ResearchButtonVariant.Ghost);
            }
            catch (Exception exception) { error.text = exception.Message; }
        }

        private ParticipantRecord Selected => participant && participant.value > 0 && participant.value <= records.Count ? records[participant.value - 1] : null;
        private static bool Selectable(ParticipantRecord record) => record.session_state == RegistryState.Available || record.session_state == RegistryState.InProgress;
        private string Status(RegistryState state) => state == RegistryState.InProgress ? R("in_progress") : state == RegistryState.Available ? R("available") : state + " / " + R("locked");

        private void ParticipantSelected(int index)
        {
            if (controller == null || controller.Registry == null) return;
            if (index == records.Count + 1)
                Invoke(() => { var created = controller.Registry.CreateNext(groups[group.value]); recordSignature = ""; Refresh(); participant.SetValueWithoutNotify(records.ToList().FindIndex(r => r.participant_id == created.participant_id) + 1); });
            else if (index > 0 && (index > records.Count || !Selectable(records[index - 1]))) { recordSignature = ""; Refresh(); }
            Refresh();
        }

        // Sampling-plan cells: task order x Task A layout.
        static readonly string[] Cells = { "Auto", "AB_P", "AB_Q", "BA_P", "BA_Q" };
        static TaskOrder? CellOrder(int cell) => cell == 0 ? (TaskOrder?)null : cell <= 2 ? TaskOrder.AB : TaskOrder.BA;
        static string CellLayout(int cell) => cell == 0 ? null : cell % 2 == 1 ? "P" : "Q";
        static int CellIndex(ParticipantRecord record) => (record.task_order == TaskOrder.AB ? 1 : 3) + (record.task_a_layout == "Q" ? 1 : 0);

        private void StartSelected()
        {
            var record = Selected;
            if (controller == null || controller.Record != null || record == null || !Selectable(record)) return;
            if (questOwner && (ExperimentInputMode)input.value != ExperimentInputMode.Controller)
            { localError = R("quest_saved"); Refresh(); return; }
            InvokeOperator("start", record, () => controller.StartSession(record.participant_id, (ParticipantLanguage)language.value, (ExperimentInputMode)input.value, CellOrder(order.value), CellLayout(order.value)));
        }

        private void AbortSelected()
        {
            ToggleEndingRequest(false);
            Refresh();
        }

        private void ReasonSelected(int index)
        {
            var record = controller?.Record;
            if (record == null) return;
            string reason = ResearchParticipantRegistry.ResearcherTerminationReasons[index];
            if (record.session_state == RegistryState.InProgress)
            {
                if ((!confirmAbort && !confirmInvalid) || !EndingRequestMatches) { ClearEndingRequest(); Refresh(); return; }
                bool invalidate = confirmInvalid;
                ClearEndingRequest();
                InvokeOperator(invalidate ? "invalidate" : "abort", record, () => controller.ResearcherAbort(invalidate, reason));
            }
            else if (record.session_state == RegistryState.Aborted || record.session_state == RegistryState.Invalid)
            {
                string failure = controller.AmendTermination(record.participant_id, reason, "ResearcherDisplay");
                localError = failure ?? "";
                Refresh();
            }
        }

        static string CurrentReason(ParticipantRecord record) =>
            record?.termination_amendments?.LastOrDefault()?.to_reason ?? record?.termination_reason ?? "other";

        private void Invoke(Action action)
        {
            try { localError = ""; action(); }
            catch (Exception exception) { localError = exception.Message; }
            Refresh();
        }

        private void InvokeOperator(string command, ParticipantRecord record, Action action)
        {
            try { localError = ""; ExecuteGuarded(command, record, action); }
            catch (Exception exception) { localError = exception.Message; OperatorStatus = "Blocked: " + exception.Message; }
            Refresh();
        }

        private void ExecuteGuarded(string command, ParticipantRecord record, Action action)
        {
            var authorization = ResearchOperatorGuard.LoadAuthorization(OperatorDirectory, record, out string hash);
            string scope = ResearchOperatorGuard.Demand(command, record, authorization);
            if (!ResearchOperatorGuard.IsTechnical(record) && (command == "start" || command == "resume"))
            {
                var hmd = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
                bool nativeHead = hmd.isValid && hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && tracked;
                bool absent = hmd.isValid && hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool present) && !present;
                bool synthetic = UnityEngine.InputSystem.InputSystem.devices.Any(d => d.layout.IndexOf("Simulated", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!nativeHead || absent || !controller.DeviceReady || synthetic)
                    throw new InvalidOperationException("Production start/resume requires native headset/controller/floor readiness without simulated input.");
            }
            RecordOperatorAuthorization(command, record, authorization, hash, scope);
            action();
            if (command == "start" && controller.Record != null && string.IsNullOrEmpty(controller.Error))
            {
                if (controller.Logger?.State != VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogState.Active)
                    throw new IOException("Started session has no active logger; operator outcome requires review.");
                RecordOperatorAuthorization(command, controller.Record, authorization, hash, scope);
            }
            if (command == "start" && authorization?.link_status == "pre_session" && string.IsNullOrEmpty(controller.Error)
                && controller.Record?.session_state == RegistryState.InProgress)
            {
                var link = new ResearchOperatorSessionLink { authorization_sha256 = hash, operator_id = authorization.operator_id,
                    participant_id = controller.Record.participant_id, session_id = controller.Record.session_id, run_id = controller.Record.run_id,
                    linked_at = DateTime.UtcNow.ToString("O") };
                Directory.CreateDirectory(OperatorDirectory);
                File.WriteAllText(Path.Combine(OperatorDirectory, "research-operator-session-link.json"), JsonConvert.SerializeObject(link, Formatting.Indented));
            }
            OperatorStatus = scope + ": " + command;
        }

        private void RecordOperatorAuthorization(string command, ParticipantRecord record, ResearchOperatorAuthorization authorization, string hash, string scope)
        {
            var provenance = new { actor_role = "Researcher", operator_id = authorization?.operator_id, authorization_sha256 = hash,
                record.participant_id, record.session_id, record.run_id, record.revision, local_route = true,
                test_data = ResearchOperatorGuard.IsTechnical(record), approvals_are_external_assertions = !ResearchOperatorGuard.IsTechnical(record) };
            if (controller.Logger?.State == VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogState.Active)
            {
                if (!controller.Log("OPERATOR_COMMAND_AUTHORIZED", "operator." + command, scope, after: provenance, source: "System"))
                    throw new IOException("Operator authorization could not be recorded; command was not executed.");
                return;
            }
            if (command != "start" && command != "export")
                throw new IOException("An active session logger is required for this operator command.");
            // Start has no run writer yet; completed-export must preserve finalized raw files.
            // Require durable, reopened authorization evidence in the separate operator journal first.
            string directory = Path.Combine(OperatorDirectory, "journal"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".authorization.json");
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(new {
                schema = "research.operator-journal.1", event_name = "OPERATOR_COMMAND_AUTHORIZED", command, scope, provenance,
                utc = DateTime.UtcNow.ToString("O"), hardware_verified = false
            }, Formatting.Indented));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new IOException("Operator authorization journal verification failed; command was not executed.");
        }

        static string ReadBounded(string path) => ResearchOperatorGuard.ReadBounded(path, out _);

        // Exact record identities, immutable per-request result files and no network listener.
        public object ExecuteLocalOperatorCommand(ResearchOperatorCommand request)
        {
            if (!controller || controller.Registry == null) throw new InvalidOperationException("Research controller/registry is unavailable.");
            var record = request?.command == "start" && controller.Record == null
                ? Enum.GetValues(typeof(ParticipantGroup)).Cast<ParticipantGroup>().SelectMany(controller.Registry.List)
                    .Single(p => p.participant_id == request.participant_id) : controller.Record;
            ResearchOperatorGuard.DemandRequest(request, record);
            bool exportRequested = false;
            string beforeError = controller.Error, expectedRun = record.run_id;
            long beforeRevision = record.revision;
            var beforeRegistryState = record.session_state;
            bool terminalCommand = request.command == "abort" || request.command == "invalidate";
            var beforeState = controller.Flow?.State;
            bool beforePaused = controller.Flow?.Paused == true;
            ExecuteGuarded(request.command, record, () => {
                switch (request.command)
                {
                    case "start": controller.StartSession(record.participant_id, request.language ?? record.language,
                        request.input_mode ?? ExperimentInputMode.Controller, request.task_order, request.task_a_layout); break;
                    case "resume": controller.ResearcherResume(); break;
                    case "abort": controller.ResearcherAbort(false, request.termination_reason ?? "other"); break;
                    case "invalidate": controller.ResearcherAbort(true, request.termination_reason ?? "other"); break;
                    case "export": exportRequested = controller.ExportSession(); break;
                }
            });
            if (terminalCommand)
            {
                var expectedState = request.command == "invalidate" ? RegistryState.Invalid : RegistryState.Aborted;
                var saved = controller.Registry.List(record.group).SingleOrDefault(p => p.participant_id == request.participant_id);
                var current = controller.Record;
                if (beforeRegistryState != RegistryState.InProgress || saved == null || current == null
                    || saved.session_state != expectedState || current.session_state != expectedState
                    || saved.session_id != request.session_id || current.session_id != request.session_id
                    || saved.run_id != expectedRun || current.run_id != expectedRun || current.participant_id != request.participant_id
                    || saved.revision <= beforeRevision || current.revision != saved.revision)
                    throw new InvalidOperationException("Terminal operator outcome was not confirmed in the exact session registry.");
                if (!string.IsNullOrEmpty(controller.Logger?.LastError))
                    throw new IOException("Terminal operator log write failed: " + controller.Logger.LastError);
            }
            if (!string.IsNullOrEmpty(controller.Error)
                && (controller.Error != beforeError || (!terminalCommand && request.command != "export")))
                throw new InvalidOperationException(controller.Error);
            if (request.command == "resume" && beforeState == controller.Flow?.State && beforePaused == (controller.Flow?.Paused == true))
                throw new InvalidOperationException("No permitted resume transition was available.");
            if (request.command == "export" && !exportRequested)
                throw new InvalidOperationException("Export failed: " + controller.ExportDeliveryError);
            return new { schema = "research.operator-response.1", request_id = request.request_id, status = "Executed", utc = DateTime.UtcNow.ToString("O"),
                command = request.command, participant_id = controller.Record?.participant_id, session_id = controller.Record?.session_id,
                state = controller.Flow?.State.ToString(), registry_state = controller.Record?.session_state.ToString(),
                run_id = controller.Record?.run_id, revision = controller.Record?.revision,
                warnings = new[] { beforeError, controller.Error, controller.Logger?.LastError, controller.ExportDeliveryError }
                    .Where(value => !string.IsNullOrEmpty(value)).Distinct().ToArray(),
                export_path = controller.LastExportPath, scope = OperatorStatus,
                test_data = ResearchOperatorGuard.IsTechnical(controller.Record), hardware_verified = false };
        }

        private void PollOperatorCommand()
        {
            string path = Path.Combine(OperatorDirectory, "research-operator-command.json");
            if (!File.Exists(path)) return;
            string requestId = Guid.NewGuid().ToString("N");
            object response;
            string resultPath = null;
            try
            {
                string results = Path.Combine(OperatorDirectory, "results"); Directory.CreateDirectory(results);
                string claimed = Path.Combine(results, requestId + "." + DateTime.UtcNow.Ticks + ".request.json");
                // Claim before parsing: malformed/oversized input is consumed once and preserved for review.
                File.Move(path, claimed);
                var request = JsonConvert.DeserializeObject<ResearchOperatorCommand>(ReadBounded(claimed), new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
                if (request != null && Guid.TryParseExact(request.request_id, "N", out _)) requestId = request.request_id;
                string candidateResultPath = Path.Combine(results, requestId + ".response.json");
                if (File.Exists(candidateResultPath))
                    throw new InvalidOperationException("Local operator request ID was already consumed.");
                resultPath = candidateResultPath;
                // A crash after claim cannot replay an action whose outcome is unknown.
                File.WriteAllText(resultPath, JsonConvert.SerializeObject(new { schema = "research.operator-response.1", request_id = requestId,
                    status = "ClaimedOutcomePending", utc = DateTime.UtcNow.ToString("O"), hardware_verified = false }, Formatting.Indented));
                response = ExecuteLocalOperatorCommand(request);
            }
            catch (Exception exception)
            {
                OperatorStatus = "Blocked: " + exception.Message;
                response = new { schema = "research.operator-response.1", request_id = requestId, status = "Blocked", utc = DateTime.UtcNow.ToString("O"), error = exception.Message,
                    participant_id = controller?.Record?.participant_id, session_id = controller?.Record?.session_id,
                    run_id = controller?.Record?.run_id, registry_state = controller?.Record?.session_state.ToString(),
                    state = controller?.Flow?.State.ToString(),
                    warnings = new[] { controller?.Error, controller?.Logger?.LastError, controller?.ExportDeliveryError }
                        .Where(value => !string.IsNullOrEmpty(value)).Distinct().ToArray(), hardware_verified = false };
            }
            if (resultPath != null) File.WriteAllText(resultPath, JsonConvert.SerializeObject(response, Formatting.Indented));
            File.WriteAllText(Path.Combine(OperatorDirectory, "research-operator-response.json"), JsonConvert.SerializeObject(response, Formatting.Indented));
        }

        public void ApplyVisualTheme()
        {
            if (!start || !participant) return;
            var panel = start.transform.parent.GetComponent<RectTransform>();
            var surface = panel.GetComponent<Image>();
            surface.color = ResearchUITheme.Glass; ResearchUITheme.Surface(surface, false, 24);
            foreach (var label in GetComponentsInChildren<TMP_Text>(true)) label.color = ResearchUITheme.Ink;
            ResearchUITheme.Button(start, ResearchButtonVariant.Primary);
            ResearchUITheme.Button(resume, ResearchButtonVariant.Secondary);
            ResearchUITheme.Button(export, ResearchButtonVariant.Secondary);
            if (!stop) stop = Button(panel, "Researcher Stop", Vector2.zero, new Vector2(260, 48), out _);
            stop.onClick.RemoveListener(StopSession); stop.onClick.AddListener(StopSession);
            ResearchUITheme.Button(stop, ResearchButtonVariant.Destructive);
            if (!stop.transform.Find("Stop Square"))
            {
                var square = Box(stop.transform, "Stop Square", new Vector2(12, 12), new Vector2(-108, 0), ResearchUITheme.Ink);
                square.GetComponent<Image>().raycastTarget = false;
            }
            stop.transform.Find("Stop Square").GetComponent<Image>().color = stop.GetComponentInChildren<TMP_Text>(true).color;
            if (!details) details = Button(panel, "Session Details", Vector2.zero, new Vector2(390, 48), out _);
            details.onClick.RemoveListener(ToggleDetails); details.onClick.AddListener(ToggleDetails);
            ResearchUITheme.Button(details, ResearchButtonVariant.Secondary);
            if (!delivery) { delivery = Text(panel, "", new Vector2(820, 48), Vector2.zero, 20); delivery.name = "Export Delivery"; }
            delivery.alignment = TextAlignmentOptions.Center;
            ResearchUITheme.Button(abort, ResearchButtonVariant.Ghost);
            foreach (var dropdown in new[] { group, participant, language, order, input })
            {
                ResearchUITheme.Button(dropdown, ResearchButtonVariant.Secondary);
                dropdown.captionText.fontSize = 20;
                var template = dropdown.template;
                if (template)
                {
                    var c = template.GetComponent<Canvas>();
                    if (questOwner)
                    {
                        if (c) c.enabled = false;
                        var gr = template.GetComponent<GraphicRaycaster>();
                        if (gr) gr.enabled = false;
                    }
                    else
                    {
                        if (!c) c = template.gameObject.AddComponent<Canvas>();
                        c.enabled = true;
                        c.targetDisplay = 1;
                    }
                    var image = template.GetComponent<Image>();
                    if (image) { image.color = ResearchUITheme.Glass; ResearchUITheme.Surface(image, true, 16); }
                    AddScrollbar(template.GetComponent<ScrollRect>());
                    var toggle = template.GetComponentInChildren<Toggle>(true);
                    if (toggle) ResearchUITheme.Button(toggle, ResearchButtonVariant.Ghost);
                    if (dropdown.itemText) dropdown.itemText.fontSize = 19;
                }
            }
            var summaryRoot = summary.transform.parent.GetComponent<Image>();
            summaryRoot.color = Color.clear;
            var summaryScroll = summaryRoot.GetComponent<ScrollRect>();
            summaryScroll.enabled = true; summaryScroll.vertical = true; summaryScroll.horizontal = false;
            AddScrollbar(summaryScroll);
            var summaryFit = summary.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (summaryFit) summaryFit.enabled = true;
            status.fontSize = 18; status.alignment = TextAlignmentOptions.Center;
            error.color = new Color(.48f, .27f, .06f); error.alignment = TextAlignmentOptions.Center;
            EnsureChoices(language, new[] { "Thai", "Japanese", "English" });
            EnsureChoices(order, Cells);
            EnsureChoices(input, new[] { "Controller", "Hand Tracking", "Technical Both" });
            if (!invalid) invalid = panel.Find("Mark Invalid")?.GetComponent<UnityEngine.UI.Button>();
            if (!invalid) invalid = Button(panel, "Mark Invalid", Vector2.zero, new Vector2(240, 42), out _);
            invalid.onClick.RemoveListener(InvalidateSelected);
            invalid.onClick.AddListener(InvalidateSelected);
            ResearchUITheme.Button(invalid, ResearchButtonVariant.Ghost);
            for (int i = 0; i < reasonButtons.Length; i++)
            {
                int index = i;
                var button = reasonButtons[i] ? reasonButtons[i] : panel.Find("End Reason " + i)?.GetComponent<UnityEngine.UI.Button>();
                if (!button) { button = Button(panel, ReasonLabels[i], Vector2.zero, new Vector2(160, 42), out _); button.name = "End Reason " + i; }
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ReasonSelected(index));
                reasonButtons[i] = button;
            }
            if (!reasonCaption) reasonCaption = panel.Find("End Reason Caption")?.GetComponent<TMP_Text>();
            if (!reasonCaption) { reasonCaption = Text(panel, "", new Vector2(140, 42), Vector2.zero, 17); reasonCaption.name = "End Reason Caption"; }
            LayoutSession(controller && controller.Record != null);
        }

        private void InvalidateSelected()
        {
            ToggleEndingRequest(true);
            Refresh();
        }

        // Keep the existing serialized dropdown values and resume locks as the single source of truth.
        private void EnsureChoices(TMP_Dropdown source, string[] labels)
        {
            var parent = source.transform.parent;
            var row = parent.Find(source.name + " Choices") as RectTransform;
            if (!row)
            {
                row = new GameObject(source.name + " Choices", typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(parent, false);
                row.sizeDelta = new Vector2(570, 48);
            }
            for (int i = 0; i < labels.Length; i++)
            {
                int value = i;
                var button = row.Find(labels[i])?.GetComponent<UnityEngine.UI.Button>();
                if (!button) button = Button(row, labels[i], Vector2.zero, new Vector2(180, 48), out _);
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => { if (source.interactable) { source.value = value; Refresh(); } });
                button.transform.SetSiblingIndex(i);
            }
            // Scene-saved rows can hold buttons for retired labels; they would shift the value mapping.
            foreach (Transform stale in row.Cast<Transform>().Where(t => !labels.Contains(t.name)).ToArray())
                if (Application.isPlaying) Destroy(stale.gameObject); else DestroyImmediate(stale.gameObject);
        }

        private void LayoutSession(bool hasSession)
        {
            var panel = start.transform.parent.GetComponent<RectTransform>();
            bool ended = hasSession && controller.Record.session_state != RegistryState.InProgress;
            panel.sizeDelta = new Vector2(920, hasSession ? !ended && (confirmAbort || confirmInvalid) ? 760 : 680 : 620);
            var headings = panel.GetComponentsInChildren<TMP_Text>(true).Where(t => t.transform.parent == panel).ToArray();
            if (!setupHeading) setupHeading = headings.FirstOrDefault(t => t.text == "Researcher Setup" || t.text == "Researcher Session" || t.text == "Researcher Summary" || t.text == "Session Summary" || t.text == "RESEARCH SESSION");
            if (!setupScope) setupScope = headings.FirstOrDefault(t => t.text.StartsWith("RESEARCHER DISPLAY") || t.text.StartsWith("TECHNICAL OPERATOR"));
            foreach (var heading in headings)
            {
                if (heading == setupHeading)
                {
                    heading.text = ended ? R("summary") : hasSession ? R("session") : R("setup");
                    heading.rectTransform.anchoredPosition = new Vector2(0, hasSession ? 285 : 260);
                    heading.fontSize = 30;
                }
                if (heading == setupScope)
                {
                    heading.gameObject.SetActive(!hasSession);
                    heading.text = questOwner ? R("scope_quest") : R("scope_pc");
                    heading.rectTransform.anchoredPosition = new Vector2(0, 215);
                    heading.fontSize = 15;
                    heading.color = ResearchUITheme.Muted;
                }
            }
            var labels = new[] { "Participant Group", "Participant ID", "Language", "Task Order", "Input Mode" };
            var dropdowns = new TMP_Dropdown[] { group, participant, language, order, input };
            for (int i = 0; i < dropdowns.Length; i++)
            {
                var source = dropdowns[i];
                if (!setupFieldLabels[i]) setupFieldLabels[i] = headings.FirstOrDefault(t => t.text == labels[i]);
                var label = setupFieldLabels[i];
                if (label)
                {
                    label.text = R(new[] { "group", "id", "language", "order", "input" }[i]);
                    label.gameObject.SetActive(!hasSession);
                    label.fontSize = 18;
                    label.color = ResearchUITheme.Muted;
                    label.rectTransform.anchoredPosition = i < 2 ? new Vector2(i == 0 ? -285 : 145, 181) : new Vector2(-285, 45 - (i - 2) * 65);
                }
                source.gameObject.SetActive(!hasSession && i < 2);
                if (i < 2)
                {
                    var rect = source.GetComponent<RectTransform>();
                    rect.anchoredPosition = new Vector2(i == 0 ? -215 : 215, 137);
                    rect.sizeDelta = new Vector2(390, 48);
                    source.captionText.rectTransform.sizeDelta = new Vector2(330, 42);
                    var arrow = source.GetComponentsInChildren<TMP_Text>().FirstOrDefault(t => t.text == "v");
                    if (arrow) arrow.rectTransform.anchoredPosition = new Vector2(170, 0);
                    continue;
                }
                var row = panel.Find(source.name + " Choices") as RectTransform;
                if (!row) continue;
                row.gameObject.SetActive(!hasSession && (i != 4 || !questOwner));
                row.anchoredPosition = new Vector2(130, 45 - (i - 2) * 65);
                var buttons = row.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                bool technical = ResearchParticipantRegistry.IsTechnical(groups[group.value]);
                int count = i == 3 ? Cells.Length : technical ? 3 : 2;
                float width = 570f / count;
                for (int j = 0; j < buttons.Length; j++)
                {
                    buttons[j].gameObject.SetActive(j < count);
                    buttons[j].interactable = source.interactable;
                    var rect = buttons[j].GetComponent<RectTransform>();
                    rect.sizeDelta = new Vector2(width - 8, 48);
                    rect.anchoredPosition = new Vector2(-285 + width * (j + .5f), 0);
                    buttons[j].GetComponentInChildren<TMP_Text>().rectTransform.sizeDelta = new Vector2(width - 20, 42);
                    ResearchUITheme.Button(buttons[j], j == source.value ? ResearchButtonVariant.Primary : ResearchButtonVariant.Secondary);
                }
            }
            if (!lockedInputLabel)
            {
                lockedInputLabel = Text(panel, "", new Vector2(570, 48), new Vector2(150, -85), 22);
                lockedInputLabel.name = "QuestLockedInputMode";
            }
            lockedInputLabel.gameObject.SetActive(questOwner && !hasSession);
            lockedInputLabel.color = ResearchUITheme.Ink;
            lockedInputLabel.text = input.value == (int)ExperimentInputMode.Controller ? R("locked_controller")
                : string.Format(R("saved_mode"), (ExperimentInputMode)input.value);
            status.gameObject.SetActive(true);
            delivery.gameObject.SetActive(hasSession);
            delivery.rectTransform.anchoredPosition = new Vector2(0, 175);
            delivery.color = !controller ? ResearchUITheme.Muted : controller.ExportDeliveryStatus == "received" ? ResearchUITheme.Success
                : controller.ExportDeliveryStatus == "saved" || controller.ExportDeliveryStatus == "queued" ? ResearchUITheme.Muted : ResearchUITheme.Failure;
            details.gameObject.SetActive(hasSession);
            details.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 120);
            stop.gameObject.SetActive(hasSession && !ended);
            stop.interactable = controller && controller.ParticipantCanStop;
            stop.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -215);
            status.rectTransform.anchoredPosition = new Vector2(0, hasSession ? 230 : -155);
            start.gameObject.SetActive(!hasSession);
            start.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -220);
            start.GetComponent<RectTransform>().sizeDelta = new Vector2(820, 56);
            startLabel.rectTransform.sizeDelta = new Vector2(796, 50);
            // Shown disabled with its reason (error line) instead of disappearing while the participant waits.
            resume.gameObject.SetActive(hasSession && !ended);
            resume.GetComponent<RectTransform>().anchoredPosition = new Vector2(-280, -215);
            error.rectTransform.anchoredPosition = new Vector2(0, hasSession ? -152 : -275);
            summary.transform.parent.gameObject.SetActive(hasSession && detailsExpanded);
            var summaryRect = summary.transform.parent.GetComponent<RectTransform>();
            summaryRect.anchoredPosition = new Vector2(0, -15);
            summaryRect.sizeDelta = new Vector2(820, 210);
            // Top-anchored content sized by its ContentSizeFitter so the ScrollRect scrolls long summaries instead of clipping them.
            summary.rectTransform.anchorMin = new Vector2(0, 1);
            summary.rectTransform.anchorMax = Vector2.one;
            summary.rectTransform.pivot = new Vector2(.5f, 1);
            summary.rectTransform.anchoredPosition = new Vector2(0, -8);
            summary.rectTransform.sizeDelta = new Vector2(-40, summary.preferredHeight);
            summary.fontSize = 20;
            summary.lineSpacing = 8;
            export.gameObject.SetActive(hasSession);
            export.GetComponent<RectTransform>().anchoredPosition = new Vector2(280, -215);
            export.GetComponent<RectTransform>().sizeDelta = new Vector2(260, 48);
            resume.GetComponent<RectTransform>().sizeDelta = new Vector2(260, 48);
            foreach (var action in new[] { stop, resume, export })
                action.GetComponentInChildren<TMP_Text>(true).rectTransform.sizeDelta = new Vector2(236, 42);
            stop.GetComponentInChildren<TMP_Text>(true).rectTransform.sizeDelta = new Vector2(208, 42);
            stop.GetComponentInChildren<TMP_Text>(true).rectTransform.anchoredPosition = new Vector2(12, 0);
            abort.gameObject.SetActive(hasSession && !ended);
            abort.GetComponent<RectTransform>().anchoredPosition = new Vector2(-205, -282);
            abort.GetComponent<RectTransform>().sizeDelta = new Vector2(300, 42);
            if (invalid)
            {
                invalid.gameObject.SetActive(hasSession && !ended);
                invalid.GetComponentInChildren<TMP_Text>().text = confirmInvalid ? R("cancel_invalid") : R("invalid");
                invalid.interactable = hasSession && !ended;
                invalid.GetComponent<RectTransform>().anchoredPosition = new Vector2(205, -282);
            }
            // The reason row stays separate from both request buttons so a repeated click cannot terminate a run.
            var record = hasSession ? controller.Record : null;
            bool amendable = ended && (record.session_state == RegistryState.Aborted || record.session_state == RegistryState.Invalid);
            bool choosing = hasSession && !ended && (confirmAbort || confirmInvalid);
            if (reasonCaption)
            {
                reasonCaption.gameObject.SetActive(choosing || amendable);
                reasonCaption.text = R("ending");
                reasonCaption.color = ResearchUITheme.Muted;
                reasonCaption.rectTransform.anchoredPosition = new Vector2(-340, choosing ? -342 : -282);
            }
            ApplyLocalizedCaptions(panel);
            if (questOwner && !hasSession) LayoutQuestResearcherPreSession(panel);
            var stopLabel = stop.GetComponentInChildren<TMP_Text>(true);
            ((RectTransform)stop.transform.Find("Stop Square")).anchoredPosition = new Vector2(-stopLabel.GetPreferredValues(stopLabel.text).x / 2 - 16, 0);
            string current = amendable ? CurrentReason(record) : null;
            for (int i = 0; i < reasonButtons.Length; i++)
            {
                var button = reasonButtons[i];
                if (!button) continue;
                button.GetComponentInChildren<TMP_Text>(true).text = R("reason" + i);
                button.gameObject.SetActive(choosing || amendable);
                var rect = button.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(-180 + 170 * i, choosing ? -342 : -282);
                rect.sizeDelta = new Vector2(160, 42);
                bool selected = amendable && ResearchParticipantRegistry.ResearcherTerminationReasons[i] == current;
                ResearchUITheme.Button(button, choosing ? ResearchButtonVariant.Destructive : selected ? ResearchButtonVariant.Primary : ResearchButtonVariant.Secondary);
            }
        }

        // Quest technical pre-session uses the owner's researcher-reference structure: four stacked selectors
        // with a single Start action. This is NOT a participant task surface or approval to start a Study run.
        // Reuse the existing guarded researcher controls: no new ID/session/input state machine.
        private void LayoutQuestResearcherPreSession(RectTransform panel)
        {
            if (!questOwner || controller?.Record != null) return;

            panel.sizeDelta = new Vector2(920, 620);
            if (!questBrand)
            {
                questBrand = Text(panel, "VR Maintenance Research", new Vector2(680, 54), new Vector2(-60, 262), 31);
                questBrand.name = "Quest Research Brand";
                questBrand.raycastTarget = false;
            }
            questBrand.gameObject.SetActive(true);
            questBrand.text = "VR Maintenance Research";
            questBrand.color = ResearchUITheme.Ink;
            questBrand.fontSize = 31;
            if (controller?.view?.englishFont) questBrand.font = controller.view.englishFont;
            if (setupHeading)
            {
                setupHeading.text = R("setup");
                setupHeading.fontSize = 23;
                setupHeading.alignment = TextAlignmentOptions.MidlineLeft;
                setupHeading.rectTransform.sizeDelta = new Vector2(820, 39);
                setupHeading.rectTransform.anchoredPosition = new Vector2(0, 218);
            }
            if (setupScope)
            {
                setupScope.text = ResearchParticipantRegistry.IsTechnical(groups[group.value]) ? R("scope_quest") : R("study");
                setupScope.fontSize = 15;
                setupScope.alignment = TextAlignmentOptions.MidlineLeft;
                setupScope.rectTransform.sizeDelta = new Vector2(820, 31);
                setupScope.rectTransform.anchoredPosition = new Vector2(0, 187);
                setupScope.gameObject.SetActive(true);
            }

            var controls = new TMP_Dropdown[] { group, participant, language, order };
            var rows = new[] { 126f, 55f, -16f, -87f };
            for (int i = 0; i < controls.Length; i++)
            {
                var control = controls[i];
                control.gameObject.SetActive(true);
                var rect = control.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(170, rows[i]);
                rect.sizeDelta = new Vector2(420, 54);
                control.captionText.fontSize = 23;
                control.captionText.textWrappingMode = TextWrappingModes.NoWrap;
                control.captionText.rectTransform.sizeDelta = new Vector2(352, 50);
                // The original dropdowns were 540 wide. Keep popup, scrollbar and arrow aligned
                // with the new 420-wide controls; otherwise the v glyph floats outside the field.
                var oldArrow = control.GetComponentsInChildren<TMP_Text>(true)
                    .FirstOrDefault(t => t.transform.parent == control.transform && t.text == "v");
                if (oldArrow) oldArrow.gameObject.SetActive(false);
                var chevron = control.transform.Find("QuestChevron") as RectTransform;
                if (!chevron)
                {
                    var iconGo = new GameObject("QuestChevron", typeof(RectTransform), typeof(ResearchUtilityIcon));
                    chevron = iconGo.GetComponent<RectTransform>();
                    chevron.SetParent(control.transform, false);
                    var icon = iconGo.GetComponent<ResearchUtilityIcon>();
                    icon.kind = "chevron_down";
                    icon.raycastTarget = false;
                }
                chevron.sizeDelta = new Vector2(32, 32);
                chevron.anchoredPosition = new Vector2(185, 0);
                chevron.GetComponent<ResearchUtilityIcon>().color = ResearchUITheme.Ink;
                if (control.template)
                {
                    control.template.sizeDelta = new Vector2(420, 240);
                    var label = control.itemText;
                    if (label) label.rectTransform.sizeDelta = new Vector2(376, 38);
                }
                // The researcher reference uses small, consistently stroked symbols to scan
                // Group / ID / Language / Order. Icons are decorative only, never ray targets.
                var iconNames = new[] { "researcher_group", "researcher_person", "researcher_globe", "researcher_order" };
                var iconRoot = panel.Find("QuestSetupIcon_" + i) as RectTransform;
                if (!iconRoot)
                {
                    var iconGo = new GameObject("QuestSetupIcon_" + i, typeof(RectTransform), typeof(ResearchUtilityIcon));
                    iconRoot = iconGo.GetComponent<RectTransform>();
                    iconRoot.SetParent(panel, false);
                    iconRoot.GetComponent<ResearchUtilityIcon>().raycastTarget = false;
                }
                iconRoot.anchoredPosition = new Vector2(-407, rows[i]);
                iconRoot.sizeDelta = new Vector2(42, 42);
                var rowIcon = iconRoot.GetComponent<ResearchUtilityIcon>();
                rowIcon.kind = iconNames[i];
                rowIcon.color = ResearchUITheme.Blue;
                if (setupFieldLabels[i])
                {
                    var label = setupFieldLabels[i];
                    label.gameObject.SetActive(true);
                    label.fontSize = 23;
                    label.color = ResearchUITheme.Ink;
                    label.alignment = TextAlignmentOptions.MidlineLeft;
                    label.rectTransform.sizeDelta = new Vector2(270, 50);
                    label.rectTransform.anchoredPosition = new Vector2(-225, rows[i]);
                }
            }

            // The source has a second set of segmented choices. Do not show them under the same dropdowns.
            foreach (var control in new TMP_Dropdown[] { language, order, input })
            {
                var choices = panel.Find(control.name + " Choices");
                if (choices) choices.gameObject.SetActive(false);
            }
            input.gameObject.SetActive(false); // Quest input is Controller; never silently expose technical hand modes.
            if (setupFieldLabels[4]) setupFieldLabels[4].gameObject.SetActive(false);
            // Read-only input policy stays visible to the researcher as a small explanatory row.
            if (lockedInputLabel)
            {
                lockedInputLabel.gameObject.SetActive(true);
                lockedInputLabel.fontSize = 19;
                lockedInputLabel.alignment = TextAlignmentOptions.Center;
                lockedInputLabel.rectTransform.sizeDelta = new Vector2(840, 32);
                lockedInputLabel.rectTransform.anchoredPosition = new Vector2(0, -134);
            }

            // Avoid repeating participant identity and assignment in a dense second paragraph:
            // the four actual selectors already show those choices.
            status.text = (status.text ?? "").Split('\n').LastOrDefault() ?? "";
            status.fontSize = 18;
            status.alignment = TextAlignmentOptions.Center;
            status.rectTransform.sizeDelta = new Vector2(840, 38);
            status.rectTransform.anchoredPosition = new Vector2(0, -172);
            error.fontSize = 17;
            error.rectTransform.sizeDelta = new Vector2(840, 38);
            error.rectTransform.anchoredPosition = new Vector2(0, -211);
            start.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -267);
            start.GetComponent<RectTransform>().sizeDelta = new Vector2(820, 60);
            startLabel.fontSize = 26;
            startLabel.rectTransform.sizeDelta = new Vector2(790, 54);
        }

        private void ApplyLocalizedCaptions(RectTransform panel)
        {
            var selectedFont = controller && controller.view ? controller.view.Font : font;
            if (selectedFont) foreach (var label in GetComponentsInChildren<TMP_Text>(true)) label.font = selectedFont;
            resume.GetComponentInChildren<TMP_Text>(true).text = R("resume");
            export.GetComponentInChildren<TMP_Text>(true).text = R("manual_export");
            stop.GetComponentInChildren<TMP_Text>(true).text = R("stop");
            details.GetComponentInChildren<TMP_Text>(true).text = R(detailsExpanded ? "hide_details" : "details");
            if (back) back.GetComponentInChildren<TMP_Text>(true).text = R("back");
            if (invalid) invalid.GetComponentInChildren<TMP_Text>(true).text = R(confirmInvalid ? "cancel_invalid" : "invalid");
            int visibleGroupCount = (detailsExpanded && !questOwner) ? groups.Length : 3;
            var groupLabels = new[] { R("thai"), R("japanese"), "DEV - " + R("test_data"), "TEST - " + R("test_data"), "PILOT - " + R("test_data") };
            if (group.options.Count != visibleGroupCount)
            {
                int currentVal = group.value;
                group.ClearOptions();
                for (int i = 0; i < visibleGroupCount; i++) group.options.Add(new TMP_Dropdown.OptionData(groupLabels[i]));
                group.SetValueWithoutNotify(Mathf.Clamp(currentVal, 0, visibleGroupCount - 1));
            }
            else
            {
                for (int i = 0; i < group.options.Count && i < groupLabels.Length; i++) group.options[i].text = groupLabels[i];
            }
            group.RefreshShownValue();
            var sources = new[] { language, order, input };
            var captions = new[] { new[] { R("thai"), R("japanese"), R("english") }, new[] { R("auto"), "AB_P", "AB_Q", "BA_P", "BA_Q" }, new[] { R("controller"), R("hands"), R("both") } };
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                for (int j = 0; j < source.options.Count && j < captions[i].Length; j++) source.options[j].text = captions[i][j];
                source.RefreshShownValue();
                var row = panel.Find(source.name + " Choices");
                if (!row) continue;
                var buttons = row.GetComponentsInChildren<Button>(true);
                for (int j = 0; j < buttons.Length && j < captions[i].Length; j++) buttons[j].GetComponentInChildren<TMP_Text>(true).text = captions[i][j];
            }
        }

        private string LocalizeBlocker(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            switch (value)
            {
                case "Select or create a participant ID.": return R("select_block");
                case "Device not ready: floor/boundary is not confirmed.": return R("floor_block");
                case "Device not ready: both controllers must be tracked.": return R("tracking_block");
                case "Data logger not ready.": return R("logger_block");
                case "No open session.": return R("no_session");
                case "Station transition in progress.": return R("transition");
                case "Participant app is not focused.": return R("focus");
                case "Headset or controllers not tracked.": return R("tracking");
                case "Quest setup requires Controller mode.": return R("quest_mode");
            }
            const string open = "A session is already open: ";
            const string nothing = "Nothing to resume: participant is in ";
            if (value.StartsWith(open, StringComparison.Ordinal)) return string.Format(R("open_session"), value.Substring(open.Length));
            if (value.StartsWith(nothing, StringComparison.Ordinal) && value.EndsWith(".", StringComparison.Ordinal)) return string.Format(R("nothing_resume"), value.Substring(nothing.Length, value.Length - nothing.Length - 1));
            int locked = value.IndexOf(" is ", StringComparison.Ordinal);
            if (locked > 0 && value.EndsWith(" and stays locked.", StringComparison.Ordinal)) return string.Format(R("state_locked"), value.Substring(0, locked), value.Substring(locked + 4, value.Length - locked - 4 - " and stays locked.".Length));
            const string session = "Session is ";
            int logger = value.IndexOf("; logger ", StringComparison.Ordinal);
            if (value.StartsWith(session, StringComparison.Ordinal) && logger > session.Length && value.EndsWith(".", StringComparison.Ordinal)) return string.Format(R("session_logger"), value.Substring(session.Length, logger - session.Length), value.Substring(logger + 9, value.Length - logger - 10));
            return value;
        }

        public bool SummaryContentVisible()
        {
            if (!summary || !summary.isActiveAndEnabled || string.IsNullOrWhiteSpace(summary.text) || summary.color.a <= 0) return false;
            summary.ForceMeshUpdate();
            var viewport = summary.transform.parent.GetComponent<RectTransform>();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, summary.rectTransform);
            return summary.textInfo.characterCount > 0 && viewport.rect.Overlaps(new Rect(bounds.min, bounds.size))
                && !summary.canvasRenderer.cull;
        }

        private void AddScrollbar(ScrollRect scroll)
        {
            if (!scroll || scroll.verticalScrollbar) return;
            var track = Box(scroll.transform, "Vertical Scrollbar", new Vector2(12, 0), Vector2.zero, new Color(.65f, .8f, 1, .14f));
            track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one;
            track.pivot = new Vector2(1, .5f); track.offsetMin = new Vector2(-18, 12); track.offsetMax = new Vector2(-6, -12);
            var handle = Box(track, "Handle", Vector2.zero, Vector2.zero, new Color(.65f, .82f, 1, .75f));
            handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one; handle.sizeDelta = Vector2.zero;
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle; bar.targetGraphic = handle.GetComponent<Image>(); bar.direction = Scrollbar.Direction.BottomToTop;
            ResearchUITheme.Surface(handle.GetComponent<Image>(), false, 6);
            scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            if (scroll.viewport != scroll.transform) scroll.viewport.offsetMax = new Vector2(-28, scroll.viewport.offsetMax.y);
            else scroll.content.sizeDelta = new Vector2(-42, scroll.content.sizeDelta.y);
        }

        private RectTransform Box(Transform parent, string name, Vector2 size, Vector2 position, Color color)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.sizeDelta = size; rect.anchoredPosition = position;
            rect.GetComponent<Image>().color = color;
            return rect;
        }

        private TMP_Text Text(Transform parent, string text, Vector2 size, Vector2 position, float fontSize)
        {
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false); label.rectTransform.sizeDelta = size; label.rectTransform.anchoredPosition = position;
            label.font = font; label.fontSize = fontSize; label.text = text; label.color = new Color(.9f, .94f, 1);
            label.alignment = TextAlignmentOptions.MidlineLeft; label.raycastTarget = false;
            return label;
        }

        private Button Button(Transform parent, string text, Vector2 position, Vector2 size, out TMP_Text label)
        {
            var rect = Box(parent, text, size, position, new Color(.11f, .28f, .44f));
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors; colors.highlightedColor = new Color(.7f, .86f, 1); colors.pressedColor = new Color(.45f, .65f, .9f); colors.disabledColor = new Color(.35f, .35f, .35f); colors.fadeDuration = .1f; button.colors = colors;
            label = Text(rect, text, size - new Vector2(24, 6), Vector2.zero, 19); label.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private TMP_Dropdown Dropdown(Transform parent, string label, float y, string[] options, bool lockItems = false)
        {
            Text(parent, label, new Vector2(250, 46), new Vector2(-285, y), 21);
            var rect = Box(parent, label + " Dropdown", new Vector2(540, 46), new Vector2(140, y), new Color(.1f, .15f, .22f));
            TMP_Dropdown dropdown = rect.gameObject.AddComponent<ResearcherParticipantDropdown>();
            dropdown.targetGraphic = rect.GetComponent<Image>();
            dropdown.captionText = Text(rect, "", new Vector2(480, 40), new Vector2(-10, 0), 20);
            Text(rect, "v", new Vector2(24, 40), new Vector2(248, 0), 18);
            var template = Box(rect, "Template", new Vector2(540, 240), new Vector2(0, -46), new Color(.07f, .1f, .16f));
            template.anchorMin = template.anchorMax = new Vector2(.5f, 0); template.pivot = new Vector2(.5f, 1); template.anchoredPosition = Vector2.zero;
            var viewport = Box(template, "Viewport", new Vector2(540, 240), Vector2.zero, Color.clear);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.sizeDelta = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>(); content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = new Vector2(0, 42);
            var item = Box(content, "Item", new Vector2(0, 42), Vector2.zero, new Color(.13f, .2f, .3f));
            item.anchorMin = new Vector2(0, .5f); item.anchorMax = new Vector2(1, .5f);
            var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = item.GetComponent<Image>();
            var itemText = Text(item, "", new Vector2(480, 38), new Vector2(0, 0), 19);
            dropdown.itemText = itemText;
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            dropdown.template = template; dropdown.AddOptions(options.ToList()); template.gameObject.SetActive(false);
            return dropdown;
        }

        private TMP_Text ScrollText(Transform parent, Vector2 size, Vector2 position)
        {
            var root = Box(parent, "Session Summary", size, position, new Color(.06f, .09f, .14f));
            root.gameObject.AddComponent<RectMask2D>();
            var label = Text(root, "", new Vector2(size.x - 24, size.y), Vector2.zero, 18);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.rectTransform.anchorMin = new Vector2(0, 1); label.rectTransform.anchorMax = new Vector2(1, 1); label.rectTransform.pivot = new Vector2(.5f, 1); label.rectTransform.anchoredPosition = Vector2.zero; label.rectTransform.sizeDelta = new Vector2(-24, size.y);
            var fit = label.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = root.gameObject.AddComponent<ScrollRect>(); scroll.content = label.rectTransform; scroll.viewport = root; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            return label;
        }
    }

}
