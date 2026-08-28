using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// Simple filter: only allow specific part name to be socketed.
    /// If wrong part enters, immediately eject.
    /// </summary>
    public class SocketPartFilter : MonoBehaviour
    {
        [Tooltip("Only this GameObject name is allowed to snap into this socket")]
        public string expectedPartName;

        private XRSocketInteractor _socket;

        void Awake()
        {
            _socket = GetComponent<XRSocketInteractor>();
            if (_socket == null)
            {
                Debug.LogError($"[SocketFilter] No XRSocketInteractor on {gameObject.name}");
                return;
            }
            _socket.selectEntered.AddListener(OnSelectEntered);
        }

        void OnDestroy()
        {
            if (_socket != null) _socket.selectEntered.RemoveListener(OnSelectEntered);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            var partName = args.interactableObject.transform.gameObject.name;
            if (partName != expectedPartName)
            {
                Debug.LogWarning($"[SocketFilter] Rejected {partName} for socket {gameObject.name}, expected {expectedPartName}");
                // Eject immediately - schedule for next frame to avoid reentrancy
                StartCoroutine(EjectNextFrame(args.interactableObject));
            }
            else
            {
                Debug.Log($"[SocketFilter] Accepted {partName} -> {gameObject.name}");
            }
        }

        System.Collections.IEnumerator EjectNextFrame(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable interactable)
        {
            yield return null;
            if (_socket != null && _socket.interactionManager != null && _socket.hasSelection)
            {
                _socket.interactionManager.SelectExit(_socket, interactable);
            }
        }
    }
}
