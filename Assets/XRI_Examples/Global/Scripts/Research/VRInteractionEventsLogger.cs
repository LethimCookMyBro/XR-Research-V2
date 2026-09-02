using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// Logs VR interaction events (grab, release, locomotion) for research analysis.
    /// Auto-discovers XR Grab Interactables in scene and subscribes to their events.
    /// </summary>
    public class VRInteractionEventsLogger : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private VRDataLogger _dataLogger;
        [SerializeField] private VRMovementTracker _movementTracker;

        [Header("Settings")]
        [SerializeField] private bool _autoDiscoverInteractables = true;
        [SerializeField] private bool _logLocomotionEvents = true;

        // Track active grabs for duration calculation
        private readonly Dictionary<XRGrabInteractable, float> _activeGrabStartTimes = new();
        private bool _isLogging;
        private float _sessionStartTime;

        public void StartLogging()
        {
            _isLogging = true;
            _sessionStartTime = Time.realtimeSinceStartup;

            if (_autoDiscoverInteractables)
                DiscoverAndSubscribeInteractables();

            Debug.Log("[VRResearch] Interaction events logging started.");
        }

        public void StopLogging()
        {
            _isLogging = false;

            // Log any still-active grabs as released
            foreach (var kvp in _activeGrabStartTimes)
            {
                LogGrabRelease(kvp.Key, kvp.Value);
            }
            _activeGrabStartTimes.Clear();

            Debug.Log("[VRResearch] Interaction events logging stopped.");
        }

        private void DiscoverAndSubscribeInteractables()
        {
            var grabInteractables = FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var interactable in grabInteractables)
            {
                SubscribeToInteractable(interactable);
            }
            Debug.Log($"[VRResearch] Subscribed to {grabInteractables.Length} XRGrabInteractables.");

            // Also subscribe to socket interactors for SocketSnap events
            var sockets = FindObjectsByType<XRSocketInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var socket in sockets)
            {
                SubscribeToSocket(socket);
            }
            Debug.Log($"[VRResearch] Subscribed to {sockets.Length} XRSocketInteractors.");
        }

        /// <summary>
        /// Call this to manually register an interactable for event logging.
        /// </summary>
        public void RegisterInteractable(XRGrabInteractable interactable)
        {
            if (interactable != null)
                SubscribeToInteractable(interactable);
        }

        private void SubscribeToInteractable(XRGrabInteractable interactable)
        {
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
            interactable.activated.AddListener(OnActivated);
            interactable.deactivated.AddListener(OnDeactivated);
        }

        private void SubscribeToSocket(XRSocketInteractor socket)
        {
            socket.selectEntered.AddListener(OnSocketSelectEntered);
            socket.selectExited.AddListener(OnSocketSelectExited);
        }

        private void OnSocketSelectEntered(SelectEnterEventArgs args)
        {
            if (!_isLogging) return;
            var socket = args.interactorObject as XRSocketInteractor;
            var interactable = args.interactableObject;
            if (socket == null || interactable == null) return;
            // Validate correct part for socket (defense in depth with SocketPartFilter)
            string partName = interactable.transform.gameObject.name;
            if (socket.gameObject.name == "Motherboard_CPU_Socket" && partName != "Mock_CPU_Part") return;
            if (socket.gameObject.name == "Fan_Shaft_Socket" && partName != "Mock_Fan_Blade") return;
            var entry = new InteractionLogEntry
            {
                TimestampMs = GetTimestamp(),
                EventType = "SocketSnap",
                ObjectName = interactable.transform.gameObject.name,
                ObjectId = interactable.transform.gameObject.GetInstanceID().ToString(),
                Hand = GetHandFromInteractor(args.interactorObject),
                Position = socket.transform.position,
                Rotation = socket.transform.rotation,
                Duration = 0f,
                ExtraData = $"{{\"socket\":\"{socket.gameObject.name}\",\"part\":\"{interactable.transform.gameObject.name}\"}}"
            };
            _dataLogger?.LogEvent(entry);
            Debug.Log($"[VRResearch] SocketSnap: {interactable.transform.gameObject.name} -> {socket.gameObject.name}");
        }

        private void OnSocketSelectExited(SelectExitEventArgs args)
        {
            if (!_isLogging) return;
            var socket = args.interactorObject as XRSocketInteractor;
            var interactable = args.interactableObject;
            if (socket == null || interactable == null) return;
            var entry = new InteractionLogEntry
            {
                TimestampMs = GetTimestamp(),
                EventType = "SocketUnsnap",
                ObjectName = interactable.transform.gameObject.name,
                ObjectId = interactable.transform.gameObject.GetInstanceID().ToString(),
                Hand = "System",
                Position = socket.transform.position,
                Rotation = socket.transform.rotation,
                Duration = 0f,
                ExtraData = $"{{\"socket\":\"{socket.gameObject.name}\"}}"
            };
            _dataLogger?.LogEvent(entry);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (!_isLogging) return;

            // Socket-driven selects are already logged as a dedicated "SocketSnap" event by
            // OnSocketSelectEntered; skip the generic entry here so a single physical snap
            // isn't double-counted as both "SocketSnap" and "Grab".
            if (args.interactorObject is XRSocketInteractor) return;

            var interactable = args.interactableObject as XRGrabInteractable;
            if (interactable == null) return;

            float timestamp = GetTimestamp();
            _activeGrabStartTimes[interactable] = timestamp;

            string hand = GetHandFromInteractor(args.interactorObject);
            var pos = args.interactorObject.transform.position;
            var rot = args.interactorObject.transform.rotation;

            var entry = new InteractionLogEntry
            {
                TimestampMs = timestamp,
                EventType = "Grab",
                ObjectName = interactable.gameObject.name,
                ObjectId = interactable.gameObject.GetInstanceID().ToString(),
                Hand = hand,
                Position = pos,
                Rotation = rot,
                Duration = 0f,
                ExtraData = $"{{\"motionType\":\"{interactable.movementType}\"}}"
            };

            _dataLogger?.LogEvent(entry);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (!_isLogging) return;

            var interactable = args.interactableObject as XRGrabInteractable;
            if (interactable == null) return;

            if (_activeGrabStartTimes.TryGetValue(interactable, out float startTime))
            {
                LogGrabRelease(interactable, startTime);
                _activeGrabStartTimes.Remove(interactable);
            }
        }

        private void LogGrabRelease(XRGrabInteractable interactable, float grabStartTime)
        {
            float timestamp = GetTimestamp();
            float duration = timestamp - grabStartTime;

            // Pre-format into its own string rather than embedding "{duration:F1}" directly
            // against the JSON object's closing "}}" - with nothing separating the format
            // specifier from the escaped closing braces, the interpolated hole silently
            // resolves to the literal format specifier text ("F1") instead of the value.
            string durationText = duration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

            var entry = new InteractionLogEntry
            {
                TimestampMs = timestamp,
                EventType = "Release",
                ObjectName = interactable.gameObject.name,
                ObjectId = interactable.gameObject.GetInstanceID().ToString(),
                Hand = "Unknown",
                Position = interactable.transform.position,
                Rotation = interactable.transform.rotation,
                Duration = duration,
                ExtraData = $"{{\"holdDuration\":{durationText}}}"
            };

            _dataLogger?.LogEvent(entry);
        }

        private void OnActivated(ActivateEventArgs args)
        {
            if (!_isLogging) return;

            var interactable = args.interactableObject as XRGrabInteractable;
            if (interactable == null) return;

            string hand = GetHandFromInteractor(args.interactorObject);

            var entry = new InteractionLogEntry
            {
                TimestampMs = GetTimestamp(),
                EventType = "Activate",
                ObjectName = interactable.gameObject.name,
                ObjectId = interactable.gameObject.GetInstanceID().ToString(),
                Hand = hand,
                Position = args.interactorObject.transform.position,
                Rotation = args.interactorObject.transform.rotation,
                Duration = 0f,
                ExtraData = ""
            };

            _dataLogger?.LogEvent(entry);
        }

        private void OnDeactivated(DeactivateEventArgs args)
        {
            if (!_isLogging) return;

            var interactable = args.interactableObject as XRGrabInteractable;
            if (interactable == null) return;

            string hand = GetHandFromInteractor(args.interactorObject);

            var entry = new InteractionLogEntry
            {
                TimestampMs = GetTimestamp(),
                EventType = "Deactivate",
                ObjectName = interactable.gameObject.name,
                ObjectId = interactable.gameObject.GetInstanceID().ToString(),
                Hand = hand,
                Position = args.interactorObject.transform.position,
                Rotation = args.interactorObject.transform.rotation,
                Duration = 0f,
                ExtraData = ""
            };

            _dataLogger?.LogEvent(entry);
        }

        public void LogReferenceOpen(string panelName)
        {
            if (!_isLogging) return;
            var entry = new InteractionLogEntry
            {
                TimestampMs = GetTimestamp(),
                EventType = "ReferenceOpen",
                ObjectName = panelName,
                ObjectId = panelName,
                Hand = "System",
                Position = Vector3.zero,
                Rotation = Quaternion.identity,
                Duration = 0f,
                ExtraData = $"{{\"panel\":\"{panelName}\"}}"
            };
            _dataLogger?.LogEvent(entry);
            Debug.Log($"[VRResearch] ReferenceOpen: {panelName}");
        }

        public void LogReferenceClose(string panelName)
        {
            if (!_isLogging) return;
            var entry = new InteractionLogEntry
            {
                TimestampMs = GetTimestamp(),
                EventType = "ReferenceClose",
                ObjectName = panelName,
                ObjectId = panelName,
                Hand = "System",
                Position = Vector3.zero,
                Rotation = Quaternion.identity,
                Duration = 0f,
                ExtraData = $"{{\"panel\":\"{panelName}\"}}"
            };
            _dataLogger?.LogEvent(entry);
            Debug.Log($"[VRResearch] ReferenceClose: {panelName}");
        }

        /// <summary>
        /// Log a locomotion event (teleport, continuous move start/end).
        /// Call this from locomotion providers.
        /// </summary>
        public void LogLocomotionEvent(string eventType, Vector3 position, string extraData = "")
        {
            if (!_isLogging || !_logLocomotionEvents) return;

            var entry = new InteractionLogEntry
            {
                TimestampMs = GetTimestamp(),
                EventType = eventType,
                ObjectName = "Locomotion",
                ObjectId = "",
                Hand = "System",
                Position = position,
                Rotation = Quaternion.identity,
                Duration = 0f,
                ExtraData = extraData
            };

            _dataLogger?.LogEvent(entry);
        }

        private string GetHandFromInteractor(IXRInteractor interactor)
        {
            if (interactor == null) return "Unknown";

            var go = interactor.transform.gameObject;
            string name = go.name.ToLower();

            if (name.Contains("left")) return "Left";
            if (name.Contains("right")) return "Right";

            // Walk up hierarchy to find hand designation
            var parent = go.transform.parent;
            while (parent != null)
            {
                string parentName = parent.gameObject.name.ToLower();
                if (parentName.Contains("left")) return "Left";
                if (parentName.Contains("right")) return "Right";
                parent = parent.parent;
            }

            return "Unknown";
        }

        private float GetTimestamp()
        {
            return (Time.realtimeSinceStartup - _sessionStartTime) * 1000f;
        }

        private void OnDestroy()
        {
            // Unsubscribe from all interactables
            var interactables = FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var interactable in interactables)
            {
                interactable.selectEntered.RemoveListener(OnSelectEntered);
                interactable.selectExited.RemoveListener(OnSelectExited);
                interactable.activated.RemoveListener(OnActivated);
                interactable.deactivated.RemoveListener(OnDeactivated);
            }
            var sockets = FindObjectsByType<XRSocketInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var socket in sockets)
            {
                socket.selectEntered.RemoveListener(OnSocketSelectEntered);
                socket.selectExited.RemoveListener(OnSocketSelectExited);
            }
        }
    }
}
