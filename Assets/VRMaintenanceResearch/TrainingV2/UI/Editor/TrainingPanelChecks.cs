#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace VRMaintenanceResearch.TrainingV2.Editor
{
    public static class TrainingPanelChecks
    {
        // Editor logic/layout checks are not XR Input-driven evidence or a substitute for image review.
        public static string Check(TrainingPanel panel)
        {
            if (panel == null) throw new ArgumentNullException(nameof(panel));
            string previous = panel.CurrentPage; int previousManual = panel.ManualPage;
            int checkedLabels = 0; var failures = new List<string>();
            try
            {
                foreach (string page in new[] { "start", "training", "manual", "examine", "inspect", "pause", "passed", "aborted" })
                {
                    panel.ShowPage(page);
                    for (int manualPage = 0; manualPage < (page == "manual" ? 3 : 1); manualPage++)
                    {
                        if (page == "manual") panel.ShowManualPage(manualPage);
                        Canvas.ForceUpdateCanvases();
                        foreach (var text in panel.GetComponentsInChildren<TMP_Text>())
                        {
                            text.ForceMeshUpdate(); checkedLabels++;
                            var rect = text.rectTransform.rect;
                            if (text.preferredHeight > rect.height + 1) failures.Add(page + ": height " + text.preferredHeight + " > " + rect.height + ": " + text.text);
                            foreach (char c in text.text)
                                if (!char.IsControl(c) && !text.font.HasCharacter(c)) failures.Add(page + ": missing glyph U+" + ((int)c).ToString("X4"));
                        }
                    }
                }
                if (panel.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length != 0) failures.Add("Panel created EventSystem.");
                if (panel.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>(true).Length != 1) failures.Add("Expected one tracked raycaster.");
                foreach (var graphic in panel.GetComponentsInChildren<TrainingRoundedGraphic>(true))
                    if (graphic.GetComponent<CanvasRenderer>() == null) failures.Add("Missing CanvasRenderer: " + graphic.name);
            }
            finally { panel.ShowManualPage(previousManual); panel.ShowPage(previous); }
            if (failures.Count != 0) throw new InvalidOperationException("TRAINING_UI_LAYOUT_FAIL\n" + string.Join("\n", failures));
            string result = "TRAINING_UI_LAYOUT_GLYPH_PASS labels=" + checkedLabels + "; Visual Thai shaping and input acceptance remain separate.";
            Debug.Log(result); return result;
        }
    }
}
#endif
