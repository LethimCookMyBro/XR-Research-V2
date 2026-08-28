using UnityEngine;

namespace UnityEngine.XR.Content.Interaction.Research
{
    public class VRReferencePanelController : MonoBehaviour
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private VRInteractionEventsLogger _logger;

        void Awake()
        {
            if (_logger == null) _logger = FindFirstObjectByType<VRInteractionEventsLogger>();
            if (_panel == null) _panel = gameObject;
        }

        public void Toggle()
        {
            if (_panel == null) return;
            bool willOpen = !_panel.activeSelf;
            _panel.SetActive(willOpen);
            if (willOpen) _logger?.LogReferenceOpen(_panel.name);
            else _logger?.LogReferenceClose(_panel.name);
        }

        public void Open()
        {
            if (_panel != null && !_panel.activeSelf)
            {
                _panel.SetActive(true);
                _logger?.LogReferenceOpen(_panel.name);
            }
        }

        public void Close()
        {
            if (_panel != null && _panel.activeSelf)
            {
                _panel.SetActive(false);
                _logger?.LogReferenceClose(_panel.name);
            }
        }
    }
}
