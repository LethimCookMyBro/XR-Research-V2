using UnityEngine;
using UnityEngine.XR;
using Oculus.Interaction.Input;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Hover/press scale (1.8% / 2.5%) was removed: sub-pixel rescaling of button text shimmers in the headset.
    // Button ColorBlock transitions carry hover/press state; haptics confirm input, never task correctness.
    [DisallowMultipleComponent]
    public sealed class ResearchUIFeedback : MonoBehaviour
    {
        // Shared physical calibration; keep pulses short until wearer comparison establishes comfortable values.
        public static float HapticAmplitude = .2f, HapticSeconds = .03f;

        public static bool TryPulse(ResearchSessionFlowController controller, int pointer)
            => TryGetHapticDevice(controller, pointer, out var device) && TryPulse(device);

        // Resolve before dispatch: an accepted transition can disable the pointer's interactor synchronously.
        public static bool TryGetHapticDevice(ResearchSessionFlowController controller, int pointer, out InputDevice device)
        {
            device = default;
            if (!controller || controller.InputSourceForPointer(pointer) != "Controller") return false;
            var guard = controller.GuardForPointer(pointer);
            if (!guard || !guard.TrackingValid || guard.Mode != HandsLabInputGuard.InputMode.Controller) return false;
            device = InputDevices.GetDeviceAtXRNode(guard.side == Handedness.Left ? XRNode.LeftHand : XRNode.RightHand);
            return device.isValid && (device.characteristics & InputDeviceCharacteristics.Controller) != 0
                && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        public static bool TryPulse(InputDevice device)
        {
            if (!device.isValid || !float.IsFinite(HapticAmplitude) || !float.IsFinite(HapticSeconds)) return false;
            float amplitude = Mathf.Clamp01(HapticAmplitude), seconds = Mathf.Clamp(HapticSeconds, 0, .08f);
            if (amplitude <= 0 || seconds <= 0) return false;
            return device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse
                && capabilities.numChannels > 0 && device.SendHapticImpulse(0, amplitude, seconds);
        }

        void OnEnable() => transform.localScale = Vector3.one;
    }
}
