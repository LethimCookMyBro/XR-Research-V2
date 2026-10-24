using System;
using TMPro;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Display 2 dropdown with disabled locked options, including popup and blocker canvases.</summary>
    public sealed class ResearcherParticipantDropdown : TMP_Dropdown
    {
        public bool[] allowed = Array.Empty<bool>();
        private int createdIndex;

        protected override GameObject CreateDropdownList(GameObject template)
        {
            createdIndex = 0;
            int display = GetComponentInParent<Canvas>().rootCanvas.targetDisplay;
            foreach (var canvas in template.GetComponentsInChildren<Canvas>(true)) canvas.targetDisplay = display;
            var list = base.CreateDropdownList(template);
            foreach (var canvas in list.GetComponentsInChildren<Canvas>(true)) canvas.targetDisplay = display;
            // Popup content must occlude the form beneath it; only the outer shell keeps glass styling.
            var surface = list.GetComponent<UnityEngine.UI.Image>();
            if (surface) surface.color = new Color(ResearchUITheme.Content.r, ResearchUITheme.Content.g, ResearchUITheme.Content.b, 1);
            return list;
        }

        protected override GameObject CreateBlocker(Canvas rootCanvas)
        {
            var blocker = base.CreateBlocker(rootCanvas);
            blocker.GetComponent<Canvas>().targetDisplay = rootCanvas.targetDisplay;
            return blocker;
        }

        protected override DropdownItem CreateItem(DropdownItem template)
        {
            var item = base.CreateItem(template);
            ResearchUITheme.Button(item.toggle, ResearchButtonVariant.Ghost);
            if (createdIndex < allowed.Length && !allowed[createdIndex])
            {
                item.toggle.interactable = false;
                if (item.text != null) item.text.color = ResearchUITheme.Muted;
            }
            createdIndex++;
            return item;
        }
    }
}
