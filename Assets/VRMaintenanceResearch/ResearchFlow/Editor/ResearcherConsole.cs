#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Desktop-only operator controls when no secondary display is available.</summary>
    public sealed class ResearcherConsole : EditorWindow
    {
        private ResearchSessionFlowController controller;
        private ParticipantGroup group = ParticipantGroup.Dev;
        private ParticipantLanguage language = ParticipantLanguage.Thai;
        private ExperimentInputMode input;
        private int cell;
        private string selectedId = "", error = "", summary = "", assignment = "", shownId = "", confirmEnd = "";
        private int endReason = 3; // index into ResearcherTerminationReasons; "other" until the researcher picks
        static readonly string[] EndReasonLabels = { "Withdrawal", "Safety stop", "Technical failure", "Other" };
        private IReadOnlyList<ParticipantRecord> records = Array.Empty<ParticipantRecord>();
        private Vector2 scroll;
        private double nextRefresh;

        // Same sampling cells as the Display 2 ResearcherSetupView.
        static readonly string[] Cells = { "Auto", "AB_P", "AB_Q", "BA_P", "BA_Q" };
        static TaskOrder? CellOrder(int value) => value == 0 ? (TaskOrder?)null : value <= 2 ? TaskOrder.AB : TaskOrder.BA;
        static string CellLayout(int value) => value == 0 ? null : value % 2 == 1 ? "P" : "Q";
        static int CellIndex(ParticipantRecord record) => (record.task_order == TaskOrder.AB ? 1 : 3) + (record.task_a_layout == "Q" ? 1 : 0);

        // Rects of the drawn researcher controls (last Repaint) so Editor tests can dispatch IMGUI mouse events at what is shown.
        public readonly Dictionary<string, Rect> ControlRects = new Dictionary<string, Rect>();
        public string SelectedId => selectedId;
        public string AssignmentText => assignment;
        public ParticipantGroup Group => group;
        public bool Bound => Application.isPlaying && controller != null && controller.Registry != null;
        void Record(string id) { if (Event.current.type == EventType.Repaint) ControlRects[id] = GUILayoutUtility.GetLastRect(); }

        [MenuItem("VR Research/Researcher Session Console")]
        public static void Open() => GetWindow<ResearcherConsole>("Researcher Console");

        // Without a second display the Display 2 researcher canvas is disabled; open the Editor route instead of leaving no researcher control.
        [InitializeOnLoadMethod]
        static void OpenWhenNoResearcherDisplay() => EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && Display.displays.Length < 2 && UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>())
                GetWindow<ResearcherConsole>("Researcher Console", false);
        };

        bool Button(string id, string label, params GUILayoutOption[] options)
        {
            bool clicked = GUILayout.Button(label, options);
            Record(id);
            return clicked;
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.timeSinceStartup < nextRefresh) return;
            nextRefresh = EditorApplication.timeSinceStartup + .5;
            Refresh();
            Repaint();
        }

        private void Refresh()
        {
            if (!Application.isPlaying || controller == null || controller.Registry == null) return;
            try
            {
                records = controller.Registry.List(group);
                // Default to a fresh ID; an unfinished saved session is resumed only when the researcher picks it.
                if (!records.Any(r => r.participant_id == selectedId && (r.session_state == RegistryState.Available || r.session_state == RegistryState.InProgress)))
                    selectedId = (records.FirstOrDefault(r => r.session_state == RegistryState.Available) ?? records.FirstOrDefault(r => r.session_state == RegistryState.InProgress))?.participant_id ?? "";
                summary = controller.Record == null ? "No session started. No real participant results are present." : controller.SummaryText();
                var selected = records.FirstOrDefault(r => r.participant_id == selectedId);
                if (controller.Record != null) assignment = Assignment(controller.Record.participant_id, controller.Record.language, controller.Record.task_order, controller.Record.task_a_layout, "locked");
                else if (selected != null && selected.session_state == RegistryState.InProgress) assignment = Assignment(selected.participant_id, selected.language, selected.task_order, selected.task_a_layout, "saved");
                else if (selected != null)
                {
                    var (previewOrder, previewLayout) = controller.Registry.PreviewAssignment(group, CellOrder(cell), CellLayout(cell));
                    assignment = Assignment(selected.participant_id, language, previewOrder, previewLayout, cell == 0 ? "Auto picks" : "chosen");
                }
                else assignment = "";
            }
            catch (Exception exception) { error = exception.Message; }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("RESEARCH SESSION", EditorStyles.boldLabel);
            if (Application.isPlaying && controller == null) { controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>(); Refresh(); }
            EditorGUILayout.LabelField("Desktop only - this window is never participant UI.", EditorStyles.wordWrappedMiniLabel);
            controller = (ResearchSessionFlowController)EditorGUILayout.ObjectField("Flow Controller", controller, typeof(ResearchSessionFlowController), true);
            if (!Application.isPlaying) { EditorGUILayout.HelpBox("Enter Play Mode, then select the session flow controller. No registry changes are made in Edit Mode.", MessageType.Info); return; }
            if (controller == null || controller.Registry == null) { EditorGUILayout.HelpBox("Select an initialized ResearchSessionFlowController.", MessageType.Info); return; }

            bool running = controller.Record != null && controller.Record.session_state == RegistryState.InProgress;
            var selected = records.FirstOrDefault(r => r.participant_id == selectedId);
            bool resuming = selected != null && selected.session_state == RegistryState.InProgress;
            using (new EditorGUI.DisabledScope(controller.Record != null))
            {
                EditorGUI.BeginChangeCheck();
                group = (ParticipantGroup)EditorGUILayout.EnumPopup("Participant Group", group);
                if (EditorGUI.EndChangeCheck()) { language = group == ParticipantGroup.Japanese ? ParticipantLanguage.Japanese : ParticipantLanguage.Thai; selectedId = ""; Refresh(); selected = records.FirstOrDefault(r => r.participant_id == selectedId); resuming = selected != null && selected.session_state == RegistryState.InProgress; }
                var participantRect = EditorGUILayout.GetControlRect();
                participantRect = EditorGUI.PrefixLabel(participantRect, new GUIContent("Participant ID"));
                if (EditorGUI.DropdownButton(participantRect, new GUIContent(selected == null ? "Select participant" : Label(selected)), FocusType.Keyboard))
                {
                    var menu = new GenericMenu();
                    foreach (var record in records)
                    {
                        var id = record.participant_id;
                        if (record.session_state == RegistryState.Available || record.session_state == RegistryState.InProgress)
                            menu.AddItem(new GUIContent(Label(record)), id == selectedId, () => { selectedId = id; Repaint(); });
                        else menu.AddDisabledItem(new GUIContent(Label(record)));
                    }
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Create next ID"), false, CreateNextId);
                    menu.DropDown(participantRect);
                }

                bool technical = ResearchParticipantRegistry.IsTechnical(group);
                if (technical) EditorGUILayout.HelpBox("DEV / TEST / PILOT: test data only. Study TH / JP IDs are independent.", MessageType.Info);
                if (resuming) { language = selected.language; input = selected.input_mode; cell = CellIndex(selected); }
                // A saved session's settings must not carry over into the next fresh ID.
                else if (selectedId != shownId) { input = ExperimentInputMode.Controller; cell = 0; language = group == ParticipantGroup.Japanese ? ParticipantLanguage.Japanese : ParticipantLanguage.Thai; }
                shownId = selectedId;
                using (new EditorGUI.DisabledScope(resuming || !technical))
                {
                    EditorGUI.BeginChangeCheck();
                    language = (ParticipantLanguage)GUILayout.Toolbar((int)language, technical ? new[] { "Thai", "Japanese", "English" } : new[] { "Thai", "Japanese" });
                    Record("language");
                    if (EditorGUI.EndChangeCheck()) Refresh();
                }
                using (new EditorGUI.DisabledScope(resuming))
                {
                    EditorGUI.BeginChangeCheck();
                    cell = GUILayout.Toolbar(cell, Cells);
                    Record("cell");
                    if (EditorGUI.EndChangeCheck()) Refresh();
                    if (technical) { input = (ExperimentInputMode)GUILayout.Toolbar((int)input, new[] { "Controller", "Hand Tracking", "Technical Both" }); Record("input"); }
                    else
                    {
                        if (!resuming) input = ExperimentInputMode.Controller;
                        EditorGUILayout.LabelField(resuming ? "Saved input" : "Input", input.ToString());
                    }
                }
                string startBlocker = controller.ResearcherStartBlocker(selected);
                using (new EditorGUI.DisabledScope(startBlocker != null))
                    if (Button("start", resuming ? "Resume Saved Session" : "Start Session", GUILayout.Height(32)))
                        Invoke(() => controller.StartSession(selectedId, language, input, CellOrder(cell), CellLayout(cell)));
                if (startBlocker != null && controller.Record == null) EditorGUILayout.HelpBox((resuming ? "Resume Saved Session" : "Start") + " not available: " + startBlocker, MessageType.Warning);
            }
            if (!string.IsNullOrEmpty(assignment)) EditorGUILayout.LabelField(assignment, EditorStyles.boldLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Training", controller.Record?.training_status ?? "Pending");
            EditorGUILayout.LabelField("Device", controller.DeviceReady ? "Ready" : "Not Ready");
            EditorGUILayout.LabelField("Data Logger", controller.LoggerReady ? "Ready" : "Not Ready");
            EditorGUILayout.LabelField("Current state", controller.Flow == null ? "ResearcherSetup" : controller.Flow.State.ToString());
            var flow = controller.Flow;
            bool checkpoint = flow != null && (flow.State == ResearchFlowState.PostTask1Checkpoint || flow.State == ResearchFlowState.PostTask2Checkpoint);
            string resumeBlocker = controller.ResearcherResumeBlocker();
            if (checkpoint) EditorGUILayout.HelpBox("Participant is waiting for you: break and questionnaire for task " + flow.CurrentTask + ". Press below when they are done.", MessageType.Info);
            else if (flow != null && flow.Paused) EditorGUILayout.HelpBox("Participant paused (" + controller.PauseKind + "). Check safety, then resume.", MessageType.Info);
            using (new EditorGUI.DisabledScope(resumeBlocker != null))
                if (Button("resume", checkpoint ? "NASA-TLX / break complete - Resume" : "Safety check complete - Resume")) Invoke(() => controller.ResearcherResume());
            if (resumeBlocker != null && running) EditorGUILayout.LabelField("Resume not available: " + resumeBlocker, EditorStyles.wordWrappedMiniLabel);

            if (controller.Record != null && controller.Record.test_data && flow != null && flow.IsTaskActive)
            {
                EditorGUILayout.LabelField("TECHNICAL DEMO ONLY", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Demo evaluator: Fail")) Invoke(() => controller.SetDemoPass(false));
                if (GUILayout.Button("Demo evaluator: Pass")) Invoke(() => controller.SetDemoPass(true));
                EditorGUILayout.EndHorizontal();
            }
            var reasons = ResearchParticipantRegistry.ResearcherTerminationReasons;
            using (new EditorGUI.DisabledScope(!running))
            {
                // Emergency stop never waits for a category; record the real one below afterwards.
                if (Button("emergency", "Emergency stop now (safety stop)")) { confirmEnd = ""; Invoke(() => controller.ResearcherAbort(false, "safety_stop")); }
                endReason = GUILayout.Toolbar(endReason, EndReasonLabels);
                EditorGUILayout.BeginHorizontal();
                // Same two-step confirm as the Display 2 setup view; a modal dialog would freeze the participant view.
                if (Button("abort", confirmEnd == "abort" ? "Confirm Abort: " + EndReasonLabels[endReason] + " (ID stays locked)" : "Abort Session"))
                { if (confirmEnd == "abort") { confirmEnd = ""; Invoke(() => controller.ResearcherAbort(false, reasons[endReason])); } else confirmEnd = "abort"; }
                if (Button("invalid", confirmEnd == "invalid" ? "Confirm Mark Invalid" : "Mark Invalid"))
                { if (confirmEnd == "invalid") { confirmEnd = ""; Invoke(() => controller.ResearcherAbort(true, reasons[endReason])); } else confirmEnd = "invalid"; }
                EditorGUILayout.EndHorizontal();
            }
            var ended = controller.Record;
            if (ended != null && (ended.session_state == RegistryState.Aborted || ended.session_state == RegistryState.Invalid))
            {
                string current = ended.termination_amendments?.LastOrDefault()?.to_reason ?? ended.termination_reason ?? "other";
                EditorGUILayout.LabelField("Ended " + ended.participant_id + " as: " + current + (ended.termination_amendments?.Count > 0 ? " (amended " + ended.termination_amendments.Count + "x)" : ""));
                endReason = GUILayout.Toolbar(endReason, EndReasonLabels);
                using (new EditorGUI.DisabledScope(reasons[endReason] == current))
                    if (Button("amend", "Record ending as: " + EndReasonLabels[endReason]))
                        Invoke(() => error = controller.AmendTermination(ended.participant_id, reasons[endReason], "ResearcherConsole") ?? "Ending recorded; export again so the PC copy carries it.");
            }
            using (new EditorGUI.DisabledScope(controller.Record == null))
                if (GUILayout.Button("Export Session Log")) Invoke(() => error = "Export: " + controller.ExportSession());
            var visibleError = string.IsNullOrEmpty(controller.Error) ? error : controller.Error;
            if (!string.IsNullOrEmpty(visibleError)) EditorGUILayout.HelpBox(visibleError, MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Session Summary", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.SelectableLabel(summary, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(Mathf.Max(180, EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(summary), Mathf.Max(200, position.width - 40)))));
            EditorGUILayout.EndScrollView();
        }

        // The "Create next ID" menu action. Public because GenericMenu items cannot receive synthetic test events.
        public void CreateNextId() => Invoke(() => { selectedId = controller.Registry.CreateNext(group).participant_id; });

        private static string Assignment(string id, ParticipantLanguage lang, TaskOrder order, string layout, string how) =>
            id + "  |  " + lang + "  |  " + order + "_" + layout + " (" + how + ")";

        private static string Label(ParticipantRecord record) => record.participant_id + "    " + (record.session_state == RegistryState.InProgress ? "In Progress / Resume" : record.session_state == RegistryState.Available ? "Available" : record.session_state + " / locked");
        private void Invoke(Action action)
        {
            try { error = ""; action(); }
            catch (Exception exception) { error = exception.Message; }
            Refresh();
            Repaint();
        }
    }
}
#endif
