using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// Attach to a GameObject with a BoxCollider (Is Trigger = true) to define
    /// a research zone (e.g., Station 1: Locomotion, Station 2: Grab).
    /// Logs Zone_Enter and Zone_Exit events when the XR Origin enters/exits.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class VRResearchZoneTrigger : MonoBehaviour
    {
        [Header("Zone Settings")]
        [SerializeField] private string _zoneName = "UnnamedZone";
        [SerializeField] private string _zoneDescription = "";

        [Header("References")]
        [SerializeField] private VRInteractionEventsLogger _eventsLogger;
        [SerializeField] private VRResearchScenarioManager _scenarioManager;

        [Header("Visual Settings (Editor Only)")]
        [SerializeField] private Color _zoneColor = new Color(0f, 1f, 0f, 0.15f);

        private bool _playerInside;
        private float _enterTime;
        private int _playerLayer;

        public string ZoneName => _zoneName;
        public bool PlayerInside => _playerInside;
        public float TimeInside => _playerInside ? (Time.realtimeSinceStartup - _enterTime) : 0f;

        private void Awake()
        {
            // Ensure collider is a trigger
            var col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                col.isTrigger = true;
                Debug.LogWarning($"[VRResearchZone] Collider on {_zoneName} was not a trigger. Set to true automatically.");
            }

            _playerLayer = LayerMask.NameToLayer("Default");
        }

        private void Start()
        {
            // Auto-find events logger if not assigned
            if (_eventsLogger == null)
                _eventsLogger = FindFirstObjectByType<VRInteractionEventsLogger>();

            // Visualize zone in editor
            VisualizeZone();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayerOrigin(other)) return;
            if (_playerInside) return; // Prevent re-entry logs

            _playerInside = true;
            _enterTime = Time.realtimeSinceStartup;

            LogZoneEvent("Zone_Enter");

            // Notify scenario manager
            if (_scenarioManager != null)
                _scenarioManager.OnZoneEntered(_zoneName);

            Debug.Log($"[VRResearchZone] Entered: {_zoneName}");
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayerOrigin(other)) return;
            if (!_playerInside) return;

            float duration = Time.realtimeSinceStartup - _enterTime;
            _playerInside = false;

            var extraData = $"{{\"zoneName\":\"{_zoneName}\",\"duration\":{duration:F1},\"description\":\"{_zoneDescription}\"}}";
            LogZoneEvent("Zone_Exit", extraData);
            Debug.Log($"[VRResearchZone] Exited: {_zoneName} (Duration: {duration:F1}s)");
        }

        private bool IsPlayerOrigin(Collider other)
        {
            // Check if the collider belongs to the XR Origin hierarchy
            var go = other.gameObject;
            if (go.GetComponent<XROrigin>() != null) return true;
            if (go.GetComponentInChildren<XROrigin>() != null) return true;

            // Walk up hierarchy to check
            var parent = go.transform.parent;
            while (parent != null)
            {
                if (parent.GetComponent<XROrigin>() != null) return true;
                parent = parent.parent;
            }

            // Fallback: check by tag or name
            return go.CompareTag("Player") || go.name.Contains("XR Origin") || go.name.Contains("XROrigin");
        }

        private void LogZoneEvent(string eventType, string extraData = "")
        {
            if (_eventsLogger == null)
            {
                Debug.LogWarning($"[VRResearchZone] No events logger found for zone {_zoneName}");
                return;
            }

            _eventsLogger.LogLocomotionEvent(
                eventType,
                transform.position,
                extraData != "" ? extraData : $"{{\"zoneName\":\"{_zoneName}\"}}"
            );
        }

        private void VisualizeZone()
        {
            // Create a visual representation in editor
            #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var visual = transform.Find("_ZoneVisual");
                if (visual == null)
                {
                    visual = new GameObject("_ZoneVisual").transform;
                    visual.SetParent(transform);
                    visual.localPosition = Vector3.zero;
                    visual.localRotation = Quaternion.identity;
                }
            }
            #endif
        }

        #if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var col = GetComponent<BoxCollider>();
            if (col == null) return;

            Gizmos.color = _zoneColor;
            Vector3 center = transform.position + col.center;
            Vector3 size = Vector3.Scale(transform.lossyScale, col.size);

            Gizmos.DrawCube(center, size);
            Gizmos.color = new Color(_zoneColor.r, _zoneColor.g, _zoneColor.b, 0.8f);
            Gizmos.DrawWireCube(center, size);

            // Draw label
            UnityEditor.Handles.Label(center + Vector3.up * (size.y * 0.5f + 0.2f),
                $"{_zoneName}\n{_zoneDescription}");
        }
        #endif
    }
}
