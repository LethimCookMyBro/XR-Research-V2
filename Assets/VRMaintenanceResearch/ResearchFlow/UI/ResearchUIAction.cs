using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

namespace VRMaintenanceResearch.ResearchFlow
{
    public sealed class ResearchUIAction : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public ResearchFlowView view;
        public string command, labelKey;
        string input = "None";
        string releasedCommand, releasedContext;
        int pointer = int.MinValue;
        readonly Dictionary<int, (string command, string context, string input)> presses = new Dictionary<int, (string, string, string)>();
        public bool PointerHeld => presses.Count > 0;
        Button button;
        void OnEnable() { button = GetComponent<Button>(); button.onClick.AddListener(Click); }
        void OnDisable() { if (button) button.onClick.RemoveListener(Click); presses.Clear(); input = "None"; pointer = int.MinValue; }
        string Context() => view && view.controller ? (view.controller.Record?.run_id ?? "setup") + "/" + view.controller.Flow?.State
            + "/" + view.controller.TaskStartConfirmation + "/" + view.controller.Flow?.Paused + "/" + view.controller.Flow?.InspectPassed + "/" + view.controller.TutorialFact + "/" + view.controller.TutorialInstructionEpoch + "/" + view.controller.InspectionId + "/" + view.controller.InspectionFeedbackVisible
            + "/" + view.controller.ReferenceOpen + "/" + view.controller.ReferenceMinimized + "/" + view.controller.ReferenceSection + "/" + view.controller.Error
            + "/" + (command == "reference_previous" || command == "reference_next" ? view.ReferencePageIndex : 0)
            + "/" + (command != null && command.StartsWith("reference", System.StringComparison.Ordinal) ? view.ReferenceMediaToken : "") : "";
        public void OnPointerDown(PointerEventData data)
        {
            input = "None"; pointer = int.MinValue;
            if (data == null || data.button != PointerEventData.InputButton.Left || !button.IsActive() || !button.IsInteractable() || presses.ContainsKey(data.pointerId)) return;
            presses[data.pointerId] = (command, Context(), view && view.controller ? view.controller.InputSourceForPointer(data.pointerId) : "None");
        }
        public void OnPointerUp(PointerEventData data)
        {
            input = "None"; pointer = int.MinValue;
            if (data == null || data.button != PointerEventData.InputButton.Left || !presses.TryGetValue(data.pointerId, out var press)) return;
            presses.Remove(data.pointerId);
            releasedCommand = releasedContext = null;
            if (data.clickCount > 1 && (press.command == "tutorial_start" || press.command == "tutorial_continue" || press.command == "task_start"
                || press.command == "task_start_confirm" || press.command == "inspect" || press.command == "inspect_continue" || press.command == "technical_resume")) return;
            if (!button.IsActive() || !button.IsInteractable())
            {
                view?.controller?.Log("UI_CLICK_REJECTED", press.command, "StaleAcquisition", source: "System", inputSource: press.input, reason: "control_unavailable");
                return;
            }
            string currentInput = view && view.controller ? view.controller.InputSourceForPointer(data.pointerId) : "None";
            if (press.command != command || press.context != Context() || press.input != currentInput)
            {
                view?.controller?.Log("UI_CLICK_REJECTED", press.command, "StaleAcquisition", source: "System", inputSource: press.input,
                    reason: press.command != command ? "command_changed" : press.context != Context() ? "context_changed" : "input_changed");
                return;
            }
            pointer = data.pointerId; input = press.input; releasedCommand = press.command; releasedContext = press.context;
        }
        void Click()
        {
            string clickedInput = input; int clickedPointer = pointer;
            string clickedCommand = releasedCommand, clickedContext = releasedContext;
            input = "None"; pointer = int.MinValue;
            releasedCommand = null; releasedContext = null;
            if (!view || !button.IsActive() || !button.IsInteractable() || clickedPointer == int.MinValue || clickedCommand != command || clickedContext != Context() || clickedInput != view.controller?.InputSourceForPointer(clickedPointer)) return;
            bool nativeController = ResearchUIFeedback.TryGetHapticDevice(view.controller, clickedPointer, out var device);
            bool accepted = view.LocalCommand(command, clickedInput);
            if (accepted && nativeController) ResearchUIFeedback.TryPulse(device);
        }
    }
}
