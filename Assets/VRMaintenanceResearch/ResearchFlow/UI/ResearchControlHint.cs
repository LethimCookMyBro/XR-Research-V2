using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VRMaintenanceResearch.ResearchFlow
{
    public sealed class ResearchControlHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public ResearchFlowView view;
        public TMP_Text label;
        public GameObject background;
        public string key;
        readonly HashSet<int> pointers = new HashSet<int>();
        // Hover-only: a pinned copy duplicated the Tutorial coach instruction as floating text.
        public void OnPointerEnter(PointerEventData data) { pointers.Add(data.pointerId); Show(); }
        public void OnPointerExit(PointerEventData data) { pointers.Remove(data.pointerId); Show(); }
        public void Show()
        {
            bool on = pointers.Count > 0;
            if (on) { label.font = view.Font; label.text = view.Text(key); }
            label.gameObject.SetActive(on);
            if (background) background.SetActive(on);
        }
        void OnDisable() { pointers.Clear(); if (label) label.gameObject.SetActive(false); if (background) background.SetActive(false); }
    }
}
