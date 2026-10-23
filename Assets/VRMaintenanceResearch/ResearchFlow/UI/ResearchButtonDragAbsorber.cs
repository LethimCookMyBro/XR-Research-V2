using UnityEngine;
using UnityEngine.EventSystems;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Buttons outside scroll lists must not transfer trigger jitter to panel drag handles.
    [DisallowMultipleComponent]
    public sealed class ResearchButtonDragAbsorber : MonoBehaviour, IDragHandler, IInitializePotentialDragHandler
    {
        public void OnDrag(PointerEventData eventData) { }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = true;
            var scroll = GetComponentInParent<UnityEngine.UI.ScrollRect>();
            if (scroll && scroll.IsActive())
            {
                eventData.pointerDrag = scroll.gameObject;
                ExecuteEvents.Execute(scroll.gameObject, eventData, ExecuteEvents.initializePotentialDrag);
            }
        }
    }
}
