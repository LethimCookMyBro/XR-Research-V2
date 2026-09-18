using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Oculus.Interaction.GrabAPI;
using UnityEngine;

namespace VRMaintenanceResearch.HandsLabQuest3.Runtime
{
    /// <summary>Lab-owned interactor gate. Leaves provider updates, tracked poses and visuals running.
    /// The raw tracked hand must not be the controller-animated/synthetic hand.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class HandsLabInputGuard : MonoBehaviour
    {
        public enum InputMode { None, Controller, TrackedHand }
        public enum ModePolicy { Auto, Controllers, TrackedHands }
        public Handedness side;
        public ModePolicy policy;
        public MonoBehaviour controllerReference, rawTrackedHandReference;
        public GameObject interactionsRoot;
        public Transform comprehensiveRigRoot;
        public GameObject[] controllerOnlyRoots = Array.Empty<GameObject>();
        public GameObject[] trackedHandOnlyRoots = Array.Empty<GameObject>();
        public Collider[] uiClearanceColliders = Array.Empty<Collider>();
        [Min(.02f)] public float neutralSeconds = .15f;
        [Range(.01f,.3f)] public float analogReleaseThreshold = .1f;
        [Min(.01f)] public float uiClearanceMetres = .05f;
        public InputMode Mode { get; private set; }
        public bool TrackingValid { get; private set; }
        public bool WaitingForNeutral { get; private set; } = true;
        public bool SystemCancelling { get; private set; }
        public bool InteractionsAllowed { get; private set; }
        public string LastReason { get; private set; } = "initial_neutral_gate";
        public event Action<Handedness, string> SystemRelease;
        public event Action<Handedness, string> StateChanged;

        IController controller;
        IHand rawHand;
        HandGrabAPI handGrab;
        readonly HashSet<Rigidbody> held = new HashSet<Rigidbody>();
        readonly List<Rigidbody> cancelBodies = new List<Rigidbody>();
        bool started, suspended, subscribed, evaluating;
        float neutralSince = -1;
        ModePolicy lastPolicy;
        readonly List<(ActiveStateTracker tracker, IActiveState original)> modeTrackers = new List<(ActiveStateTracker, IActiveState)>();

        // Verified ISDK205 comprehensive-rig names. Creates only lab-owned read adapters;
        // never changes the SDK data-source components or their tracked transforms.
        public void ConfigureFromRig(Transform rig, Handedness handedness)
        {
            if (started) throw new InvalidOperationException("ConfigureFromRig must precede Start.");
            string suffix = handedness == Handedness.Left ? "Left" : "Right";
            var handSourceTransform = rig.Find("UnityXRHands/UnityXRHandDataSource" + suffix);
            var controllerSourceTransform = rig.Find("UnityXRControllers/UnityXRControllerDataSource" + suffix);
            var sideRoot = rig.Find("ComprehensiveInteractors" + suffix);
            if (!handSourceTransform || !controllerSourceTransform || !sideRoot)
                throw new InvalidOperationException("Expected standard ISDK205 UnityXR comprehensive rig hierarchy.");
            var handSource = handSourceTransform.GetComponent<Oculus.Interaction.Input.UnityXR.FromUnityXRHandDataSource>();
            var controllerSource = controllerSourceTransform.GetComponent<Oculus.Interaction.UnityXR.FromUnityXRControllerDataSource>();
            if (!handSource || !controllerSource) throw new InvalidOperationException("Raw UnityXR data sources missing.");
            var adapterTransform = transform.Find("HandsLabRawProviders_" + suffix);
            if (!adapterTransform)
            {
                adapterTransform = new GameObject("HandsLabRawProviders_" + suffix).transform;
                adapterTransform.SetParent(transform, false);
            }
            var hand = adapterTransform.GetComponent<Hand>();
            if (!hand) hand = adapterTransform.gameObject.AddComponent<Hand>();
            hand.InjectAllDataModifier(DataSource<HandDataAsset>.UpdateModeFlags.AfterPreviousStep, handSource, handSource, true);
            var controllerAdapter = adapterTransform.GetComponent<Controller>();
            if (!controllerAdapter) controllerAdapter = adapterTransform.gameObject.AddComponent<Controller>();
            controllerAdapter.InjectAllController(DataSource<ControllerDataAsset>.UpdateModeFlags.AfterPreviousStep,
                controllerSource, controllerSource, true);
            var interactionTransform = sideRoot.Find("Interactors");
            if (!interactionTransform) throw new InvalidOperationException("Standard per-hand Interactors subtree missing.");
            comprehensiveRigRoot = rig;
            Configure(handedness, controllerAdapter, hand, interactionTransform.gameObject);
        }

        sealed class ModeState : IActiveState
        {
            readonly HandsLabInputGuard guard;
            readonly IActiveState original;
            readonly InputMode required;
            public ModeState(HandsLabInputGuard guard, IActiveState original, InputMode required)
            { this.guard = guard; this.original = original; this.required = required; }
            public bool Active => original.Active && guard.TrackingValid
                && (required == InputMode.None || guard.Mode == required);
        }
        void BindModeTrackers()
        {
            if (!comprehensiveRigRoot || modeTrackers.Count != 0) return;
            var sideRoot = interactionsRoot.transform.parent;
            var trackers = interactionsRoot.transform.Find("ActiveStatesTrackers");
            if (!trackers) throw new InvalidOperationException("Standard ActiveStatesTrackers subtree missing.");
            foreach (Transform trackerTransform in trackers)
            {
                var tracker = trackerTransform.GetComponent<ActiveStateTracker>();
                if (!tracker) continue;
                IActiveState original;
                InputMode required;
                switch (trackerTransform.name)
                {
                    case "Hand": original = sideRoot.GetComponent<HandRef>(); required = InputMode.None; break;
                    case "Controller": original = sideRoot.GetComponent<ControllerRef>(); required = InputMode.Controller; break;
                    case "Hand and No Controller": original = trackerTransform.GetComponent<ActiveStateGroup>(); required = InputMode.TrackedHand; break;
                    case "Controller and No Hand":
                    case "Controller and Hand": original = trackerTransform.GetComponent<ActiveStateGroup>(); required = InputMode.Controller; break;
                    default: continue;
                }
                if (original == null) throw new InvalidOperationException("Standard tracker active-state source missing: " + trackerTransform.name);
                modeTrackers.Add((tracker, original));
                tracker.InjectActiveState(new ModeState(this, original, required));
            }
        }
        void RestoreModeTrackers()
        {
            foreach (var binding in modeTrackers) if (binding.tracker) binding.tracker.InjectActiveState(binding.original);
            modeTrackers.Clear();
        }

        public void Configure(Handedness handedness, MonoBehaviour controllerRef,
            MonoBehaviour trackedHandRef, GameObject perHandInteractions)
        {
            if (started) throw new InvalidOperationException("Configure guard before Start.");
            side = handedness; controllerReference = controllerRef;
            rawTrackedHandReference = trackedHandRef; interactionsRoot = perHandInteractions;
        }

        void Awake()
        {
            if (interactionsRoot) interactionsRoot.SetActive(false);
        }

        void Start()
        {
            controller = controllerReference as IController;
            rawHand = rawTrackedHandReference as IHand;
            if (!interactionsRoot || interactionsRoot == gameObject || transform.IsChildOf(interactionsRoot.transform)
                || controller == null || rawHand == null)
                throw new InvalidOperationException("Guard needs controller IController, RAW tracked IHand and separate interactions subtree.");
            // This API stays outside the disabled interaction subtree and uses the SDK's own
            // palm/pinch detectors, not a lab-created gesture recognizer.
            handGrab = gameObject.AddComponent<HandGrabAPI>();
            handGrab.InjectAllHandGrabAPI(rawHand);
            initializationFrame = Time.frameCount;
            started = true; lastPolicy = policy;
            Block("initial_neutral_gate");
            Subscribe();
        }

        void Subscribe()
        {
            if (!started || subscribed) return;
            controller.WhenUpdated += ProviderChanged;
            rawHand.WhenHandUpdated += ProviderChanged;
            subscribed = true;
        }
        void Unsubscribe()
        {
            if (!subscribed) return;
            controller.WhenUpdated -= ProviderChanged;
            rawHand.WhenHandUpdated -= ProviderChanged;
            subscribed = false;
        }
        void OnEnable() { Subscribe(); }
        void OnDisable()
        {
            Unsubscribe();
            if (started) Block("guard_disabled");
            RestoreModeTrackers();
        }
        void ProviderChanged() { Evaluate(false); }
        void Update() { Evaluate(true); }

        public void RegisterHeldBody(Rigidbody body) { if (body) held.Add(body); }
        public void UnregisterHeldBody(Rigidbody body) { if (body) held.Remove(body); }
        public void Suspend(string reason = "suspended") { suspended = true; if (started) Block(reason); }
        public void Resume() { suspended = false; neutralSince = -1; WaitingForNeutral = true; LastReason = "resume_neutral_gate"; }

        void Evaluate(bool mayRestore)
        {
            if (!started || !isActiveAndEnabled || evaluating) return;
            evaluating = true;
            try
            {
                var next = ChooseMode(policy, controller.IsConnected && controller.IsPoseValid,
                    rawHand.IsConnected && rawHand.IsTrackedDataValid && rawHand.IsHighConfidence);
                if (policy != lastPolicy || next != Mode)
                {
                    Block(policy != lastPolicy ? "mode_policy_changed" : "provider_mode_changed");
                    lastPolicy = policy; Mode = next;
                    TrackingValid = next != InputMode.None;
                    ApplyExclusiveRoots();
                    StateChanged?.Invoke(side, "mode_" + Mode);
                }
                TrackingValid = next != InputMode.None;
                if (suspended || !TrackingValid)
                {
                    if (InteractionsAllowed || !WaitingForNeutral) Block(suspended ? "suspended" : "tracking_invalid");
                    neutralSince = -1;
                    return;
                }
                if (!WaitingForNeutral || !mayRestore) return;
                // HandGrabAPI initializes in its Start after this component; never query it
                // during the frame it is added or from a provider callback in mid-update.
                bool neutral = Mode == InputMode.Controller ? ControllerNeutral() : HandNeutral();
                if (!neutral) { neutralSince = -1; return; }
                if (neutralSince < 0) neutralSince = Time.unscaledTime;
                if (Time.unscaledTime - neutralSince < neutralSeconds) return;
                WaitingForNeutral = false; InteractionsAllowed = true; LastReason = "neutral_restored";
                interactionsRoot.SetActive(true);
                // Activating first runs SDK Awake, which reads its serialized active-state
                // reference. Inject the runtime wrapper afterward, before Start/Update.
                BindModeTrackers();
                StateChanged?.Invoke(side, LastReason);
            }
            finally { evaluating = false; }
        }

        public static InputMode ChooseMode(ModePolicy requested, bool controllerValid, bool handValid)
        {
            if (requested != ModePolicy.TrackedHands && controllerValid) return InputMode.Controller;
            if (requested != ModePolicy.Controllers && handValid) return InputMode.TrackedHand;
            return InputMode.None;
        }
        public static bool ControllerIsNeutral(float grip, float trigger, bool digitalPressed, float threshold)
            => !digitalPressed && grip < threshold && trigger < threshold;
        public static string RunPolicyChecks()
        {
            int checks = 0;
            void Check(bool value) { if (!value) throw new InvalidOperationException("Guard policy check " + checks); checks++; }
            Check(ChooseMode(ModePolicy.Auto, false, false) == InputMode.None);
            Check(ChooseMode(ModePolicy.Auto, true, true) == InputMode.Controller);
            Check(ChooseMode(ModePolicy.Auto, true, false) == InputMode.Controller);
            Check(ChooseMode(ModePolicy.Auto, false, true) == InputMode.TrackedHand);
            Check(ChooseMode(ModePolicy.Controllers, false, true) == InputMode.None);
            Check(ChooseMode(ModePolicy.TrackedHands, true, false) == InputMode.None);
            Check(ChooseMode(ModePolicy.TrackedHands, true, true) == InputMode.TrackedHand);
            Check(ControllerIsNeutral(0, 0, false, .1f));
            Check(!ControllerIsNeutral(1, 0, false, .1f));
            Check(!ControllerIsNeutral(0, 1, false, .1f));
            Check(!ControllerIsNeutral(0, 0, true, .1f));
            Check(!ControllerIsNeutral(.1f, 0, false, .1f));
            return "GUARD_POLICY_CHECK_PASS checks=" + checks + "; logic only, no hardware claim";
        }
        bool ControllerNeutral()
        {
            const ControllerButtonUsage buttons = ControllerButtonUsage.GripButton | ControllerButtonUsage.TriggerButton
                | ControllerButtonUsage.PrimaryButton | ControllerButtonUsage.SecondaryButton;
            return ControllerIsNeutral(controller.ControllerInput.Grip, controller.ControllerInput.Trigger,
                controller.IsButtonUsageAnyActive(buttons), analogReleaseThreshold);
        }
        bool HandNeutral()
        {
            if (!handGrab || !handGrab.isActiveAndEnabled || Time.frameCount <= initializationFrame + 1) return false;
            if (handGrab.HandPinchGrabbingFingers() != HandFingerFlags.None
                || handGrab.HandPalmGrabbingFingers() != HandFingerFlags.None) return false;
            if (!rawHand.GetJointPose(HandJointId.HandIndexTip, out var fingertip)) return false;
            foreach (var collider in uiClearanceColliders)
                if (collider && collider.enabled && collider.gameObject.activeInHierarchy
                    && Vector3.Distance(collider.ClosestPoint(fingertip.position), fingertip.position) < uiClearanceMetres)
                    return false;
            return true;
        }
        int initializationFrame;

        void ApplyExclusiveRoots()
        {
            foreach (var root in controllerOnlyRoots) if (root) root.SetActive(Mode == InputMode.Controller);
            foreach (var root in trackedHandOnlyRoots) if (root) root.SetActive(Mode == InputMode.TrackedHand);
        }

        void Block(string reason)
        {
            WaitingForNeutral = true; neutralSince = -1; LastReason = reason;
            var wasAllowed = InteractionsAllowed; InteractionsAllowed = false;
            cancelBodies.Clear(); cancelBodies.AddRange(held);
            SystemCancelling = true;
            try
            {
                if (wasAllowed || cancelBodies.Count != 0) SystemRelease?.Invoke(side, reason);
                // SDK OnDisable cancels selections. The callback above runs first so the
                // logger labels cancellation as System rather than participant release.
                if (interactionsRoot) interactionsRoot.SetActive(false);
                foreach (var body in cancelBodies)
                    if (body && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                held.Clear();
            }
            finally { SystemCancelling = false; }
            StateChanged?.Invoke(side, reason);
        }
    }
}
