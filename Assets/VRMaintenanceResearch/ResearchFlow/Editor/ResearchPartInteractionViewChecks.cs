#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Run only on the caller's disposable, active-task V4 TEST fixture, once per language/task.
    public static class ResearchPartInteractionViewChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden).GetValue(owner);
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        public static string RunOnFixture(ResearchFlowView view)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor TEST fixture required.");
            var controller = view.controller;
            var port = (ResearchTaskPort)typeof(ResearchSessionFlowController).GetProperty("CurrentPort", Hidden).GetValue(controller);
            if (!controller.Record.test_data || !controller.UsesDirectPartFeedback || !port.demoMode || !port.InteractionAllowed)
                throw new InvalidOperationException("Active disposable V4 technical task fixture required.");
            Check(port.CurrentInteraction == null, "Fixture must not own an existing attempt.");
            var refresh = typeof(ResearchFlowView).GetMethod("RefreshReaderFeedback", Hidden);
            void Refresh() { refresh.Invoke(view, null); Canvas.ForceUpdateCanvases(); }
            TMP_Text Label() => Field<TMP_Text>(view, "readerMainFeedbackLabel");
            var originalPosition = view.mainPanel.transform.position;
            var originalRotation = view.mainPanel.transform.rotation;
            string target = port.taskId == "B" ? "B.front_guard" : "M01";
            var a = port.BeginAttempt(target, "SimulatedController");
            try
            {
                port.UpdateInteraction(a, PartOperation.Remove, PartBlockReason.ClipClosed);
                Refresh();
                var label = Label();
                Check(label && label.isActiveAndEnabled && !label.raycastTarget, "Main feedback must be visible and not intercept input.");
                Check(label.text.Contains(ResearchStrings.Get("interaction.reason.ClipClosed", controller.Language)), "Observed reason must render.");
                label.ForceMeshUpdate();
                Check(!label.isTextOverflowing, "Localized feedback must fit its existing viewport.");
                Check(label.rectTransform.rect.size == label.rectTransform.parent.GetComponent<RectTransform>().rect.size,
                    "Feedback must use the Main task viewport, not hidden body.");
                port.UpdateInteraction(a, PartOperation.Turn, PartBlockReason.EndStop);
                Refresh();
                Check(label.text.Contains(ResearchStrings.Get("interaction.reason.EndStop", controller.Language)) &&
                    !label.text.Contains(ResearchStrings.Get("interaction.reason.ClipClosed", controller.Language)), "Reason change must remove previous message.");
                port.UpdateInteraction(a, PartOperation.Turn, PartBlockReason.None);
                Refresh();
                Check(!label.text.Contains(ResearchStrings.Get("interaction.reason.EndStop", controller.Language)), "Accepted motion must clear block reason.");
                Check(!view.GetComponentsInChildren<ResearchUIAction>(false).Any(c => c.command == "examine_open"), "V4 has no Examine action.");
                port.EndAttempt(a);
                Refresh();
                Check(!label.gameObject.activeSelf, "Release must clear Main feedback.");
                Check(Field<RectTransform>(view, "taskBrief").gameObject.activeSelf, "Release restores task brief.");
                Check(view.mainPanel.transform.position == originalPosition && view.mainPanel.transform.rotation == originalRotation,
                    "Feedback must not move the Main panel.");
                return "PART_INTERACTION_VIEW_PASS " + controller.Language + "/" + port.taskId;
            }
            finally { port.EndAttempt(a, true); Refresh(); }
        }
    }
}
#endif
