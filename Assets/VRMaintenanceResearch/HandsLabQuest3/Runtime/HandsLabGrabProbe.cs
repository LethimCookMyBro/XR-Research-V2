using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.XR;

namespace VRMaintenanceResearch.HandsLabQuest3.Runtime
{
    // Temporary read-only hardware probe. Never selects, moves, or enables an interaction.
    public sealed class HandsLabGrabProbe : MonoBehaviour
    {
        HandsLabInputGuard[] guards;
        HandsLabObject[] objects;
        string directory;
        Func<bool> systemChanging;
        static readonly System.Reflection.FieldInfo trackerActive = typeof(ActiveStateTracker).GetField("_active", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        StreamWriter output;
        float nextSample;
        float nextIdleSample;
        readonly List<Action> unsubscribe = new List<Action>();
        readonly Dictionary<HandsLabInputGuard, MonoBehaviour[]> nodes = new Dictionary<HandsLabInputGuard, MonoBehaviour[]>();
        readonly Dictionary<HandsLabObject, double> releaseUntil = new Dictionary<HandsLabObject, double>();
        readonly Dictionary<HandsLabObject, object> lastHeld = new Dictionary<HandsLabObject, object>();

        public void Initialize(HandsLabSession owner)
        {
            try { Initialize(owner.guards, owner.objects, owner.Logger.DirectoryPath, () => owner.SystemChanging); }
            catch (Exception ex) { Fail(ex); }
        }

        public void Initialize(HandsLabInputGuard[] inputGuards, HandsLabObject[] trainingObjects, string directoryPath, Func<bool> isSystemChanging = null)
        {
            guards = inputGuards;
            objects = trainingObjects;
            directory = directoryPath;
            systemChanging = isSystemChanging;
            try
            {
                if (guards == null || objects == null || string.IsNullOrWhiteSpace(directory))
                    throw new ArgumentException("Grab probe requires guards, objects and an output directory.");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "controller_grab_probe.jsonl");
                output = new StreamWriter(path, false) { AutoFlush = true };
                foreach (var guard in guards)
                {
                    nodes[guard] = guard.interactionsRoot.GetComponentsInChildren<MonoBehaviour>(true);
                    foreach (var node in nodes[guard])
                    {
                        if (node is IInteractor interactor)
                        {
                            Action<InteractorStateChangeArgs> changed = e => { if(e.PreviousState == InteractorState.Select || e.NewState == InteractorState.Select) Write("interactor_state", new { hand = guard.side.ToString(), path = PathOf(node), previous = e.PreviousState.ToString(), current = e.NewState.ToString() }); };
                            interactor.WhenStateChanged += changed;
                            unsubscribe.Add(() => interactor.WhenStateChanged -= changed);
                        }
                        if (node is ControllerSelector selector)
                        {
                            Action down = () => Write("selector_down", new { hand = guard.side.ToString(), path = PathOf(node), buttons = selector.ControllerButtonUsage.ToString() });
                            Action up = () => Write("selector_up", new { hand = guard.side.ToString(), path = PathOf(node) });
                            selector.WhenSelected += down;
                            selector.WhenUnselected += up;
                            unsubscribe.Add(() => { selector.WhenSelected -= down; selector.WhenUnselected -= up; });
                        }
                    }
                }
                foreach (var item in objects)
                {
                    Action<PointerEvent> pointer = e =>
                    {
                        if (e.Type == PointerEventType.Move && item.IsHeld) lastHeld[item] = PhysicsState(item);
                        if (e.Type != PointerEventType.Select && e.Type != PointerEventType.Unselect && e.Type != PointerEventType.Cancel) return;
                        lastHeld.TryGetValue(item, out var before);
                        Write("object_pointer", new { target = item.stableId, type = e.Type.ToString(), pointer = e.Identifier,
                            held = item.IsHeld, last_held = before, after = PhysicsState(item), system_reason = item.SystemReason, system_changing = systemChanging?.Invoke() ?? false });
                        if (e.Type == PointerEventType.Select) { releaseUntil.Remove(item); lastHeld.Remove(item); }
                        else releaseUntil[item] = Time.realtimeSinceStartupAsDouble + 1.0;
                    };
                    item.grabbable.WhenPointerEventRaised += pointer;
                    unsubscribe.Add(() => { if(item.grabbable) item.grabbable.WhenPointerEventRaised -= pointer; });
                    ThrowWhenUnselected.VelocitiesLoadedDelegate velocity = (linear, angular) => Write("sdk_release_velocity", new
                    { target = item.stableId, linear_velocity = V(linear), angular_velocity = V(angular), body = PhysicsState(item) });
                    item.grabbable.VelocityThrow.WhenThrown += velocity;
                    unsubscribe.Add(() => { if(item.grabbable) item.grabbable.VelocityThrow.WhenThrown -= velocity; });
                }
                Write("ready", new { path, sample_hz = 10, application = Application.identifier });
                Debug.Log("HANDSLAB_GRAB_PROBE_READY " + path);
            }
            catch (Exception ex) { Fail(ex); }
        }

        void FixedUpdate()
        {
            if (output == null) return;
            foreach (var item in objects)
                if (releaseUntil.TryGetValue(item, out var until) && Time.realtimeSinceStartupAsDouble <= until)
                    Write("post_release_physics", PhysicsState(item));
        }

        static object PhysicsState(HandsLabObject item)
        {
            return new { time = Time.realtimeSinceStartupAsDouble, state = item.CaptureSnapshot(),
                is_kinematic = item.body.isKinematic, use_gravity = item.body.useGravity, sleeping = item.body.IsSleeping(),
                linear_velocity = V(item.body.linearVelocity), angular_velocity = V(item.body.angularVelocity) };
        }

        void LateUpdate()
        {
            if (output == null || Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + .1f;
            try
            {
                foreach (var guard in guards)
                {
                    var device = InputDevices.GetDeviceAtXRNode(guard.side == Handedness.Left ? XRNode.LeftHand : XRNode.RightHand);
                    bool hasGrip = device.TryGetFeatureValue(CommonUsages.grip, out float grip);
                    device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripButton);
                    device.TryGetFeatureValue(CommonUsages.trigger, out float trigger);
                    if (grip < .05f && trigger < .05f && !objects.Any(o => o.IsHeld) && Time.unscaledTime < nextIdleSample) continue;
                    bool hasPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position);
                    bool hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation);
                    bool hasVelocity = device.TryGetFeatureValue(CommonUsages.deviceVelocity, out Vector3 velocity);
                    bool hasAngular = device.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out Vector3 angular);
                    Write("sample", new
                    {
                        hand = guard.side.ToString(), mode = guard.Mode.ToString(), tracking = guard.TrackingValid,
                        allowed = guard.InteractionsAllowed, waiting_for_neutral = guard.WaitingForNeutral, reason = guard.LastReason,
                        interaction_root = new { path = PathOf(guard.interactionsRoot.transform), active_self = guard.interactionsRoot.activeSelf, active_in_hierarchy = guard.interactionsRoot.activeInHierarchy },
                        active_state_trackers = nodes[guard].OfType<ActiveStateTracker>().Select(t => new { path = PathOf(t), enabled = t.enabled, active_in_hierarchy = t.gameObject.activeInHierarchy, applied_active = trackerActive?.GetValue(t) }).ToArray(),
                        raw = new { provider = device.name, valid = device.isValid, has_grip = hasGrip, grip, grip_button = gripButton, trigger,
                            position = hasPosition ? V(position) : null, rotation = hasRotation ? new[] { rotation.x, rotation.y, rotation.z, rotation.w } : null,
                            linear_velocity = hasVelocity ? V(velocity) : null, angular_velocity = hasAngular ? V(angular) : null },
                        guard_controller = ControllerState(guard.controllerReference as IController),
                        rig_controller = ControllerState(guard.interactionsRoot.transform.parent.GetComponent<ControllerRef>()),
                        interactors = nodes[guard].Where(n => n is IInteractor && !(n is InteractorGroup)).Select(InteractorStateOf).ToArray(),
                        selectors = nodes[guard].OfType<ControllerSelector>().Select(s => new { path = PathOf(s), active = s.isActiveAndEnabled, buttons = s.ControllerButtonUsage.ToString(), controller = ControllerState(s.Controller) }).ToArray(),
                        objects = objects.Select(o => new { state = o.CaptureSnapshot(), physics = PhysicsState(o), grabbable_enabled = o.grabbable.isActiveAndEnabled,
                            hand_interactables = o.GetComponentsInChildren<HandGrabInteractable>(true).Select(h => new { path = PathOf(h), active = h.isActiveAndEnabled, state = h.State.ToString(), types = h.SupportedGrabTypes.ToString(), poses = h.HandGrabPoses.Count }).ToArray(),
                            colliders = o.GetComponentsInChildren<Collider>(true).Select(ColliderState).ToArray() }).ToArray()
                    });
                }
                if(Time.unscaledTime >= nextIdleSample) nextIdleSample = Time.unscaledTime + 2f;
            }
            catch (Exception ex) { Fail(ex); }
        }

        static object ControllerState(IController c) => c == null ? null : new
        {
            connected = c.IsConnected, tracked = c.IsPoseValid, grip = c.ControllerInput.Grip, trigger = c.ControllerInput.Trigger,
            grip_button = c.IsButtonUsageAnyActive(ControllerButtonUsage.GripButton), trigger_button = c.IsButtonUsageAnyActive(ControllerButtonUsage.TriggerButton)
        };

        static object InteractorStateOf(MonoBehaviour node)
        {
            var i = (IInteractor)node;
            object grab = null;
            if (node is HandGrabInteractor h && h.isActiveAndEnabled && h.Hand != null)
                grab = new { candidate = PathOf(h.Candidate), hovered = PathOf(h.Interactable), selected = PathOf(h.SelectedInteractable),
                    reachable = HandGrabInteractable.Registry.List(h).Select(PathOf).ToArray(),
                    hand_connected = h.Hand.IsConnected, hand_valid = h.Hand.IsTrackedDataValid,
                    fingers = h.FingersStrength, wrist = h.WristStrength,
                    palm_fingers = h.HandGrabApi.HandPalmGrabbingFingers().ToString(), pinch_fingers = h.HandGrabApi.HandPinchGrabbingFingers().ToString(),
                    wrist_position = h.WristPoint ? V(h.WristPoint.position) : null,
                    wrist_rotation = h.WristPoint ? new[] { h.WristPoint.rotation.x, h.WristPoint.rotation.y, h.WristPoint.rotation.z, h.WristPoint.rotation.w } : null,
                    palm = h.PalmPoint ? V(h.PalmPoint.position) : null,
                    pinch = h.PinchPoint ? V(h.PinchPoint.position) : null,
                    colliders = h.Rigidbody.GetComponentsInChildren<Collider>(true).Select(ColliderState).ToArray() };
            else if (node is GrabInteractor g && g.isActiveAndEnabled)
                grab = new { candidate = PathOf(g.Candidate), hovered = PathOf(g.Interactable), selected = PathOf(g.SelectedInteractable),
                    reachable = GrabInteractable.Registry.List(g).Select(PathOf).ToArray(),
                    colliders = g.Rigidbody.GetComponentsInChildren<Collider>(true).Select(ColliderState).ToArray() };
            return new { path = PathOf(node), type = node.GetType().Name, enabled = node.enabled, active = node.gameObject.activeInHierarchy,
                state = i.State.ToString(), candidate = i.HasCandidate, hovered = i.HasInteractable, selected = i.HasSelectedInteractable,
                should_select = i.ShouldSelect, grab };
        }

        static object ColliderState(Collider c) => new { path = PathOf(c), enabled = c.enabled, active = c.gameObject.activeInHierarchy,
            layer = c.gameObject.layer, trigger = c.isTrigger, centre = V(c.bounds.center), size = V(c.bounds.size) };

        static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };

        static string PathOf(Component c)
        {
            if (!c) return null;
            string path = c.name;
            for (var t = c.transform.parent; t; t = t.parent) path = t.name + "/" + path;
            return path;
        }

        void Write(string kind, object data)
        {
            if (output == null) return;
            try
            {
                // ponytail: cap a temporary diagnostic capture at 64 MiB; restart the app for another capture.
                if (output.BaseStream.Position >= 64 * 1024 * 1024) { output.Dispose(); output = null; Debug.LogWarning("HANDSLAB_GRAB_PROBE_LIMIT"); return; }
                output.WriteLine(JsonConvert.SerializeObject(new { time = Time.realtimeSinceStartupAsDouble, run = directory, kind, data }));
            }
            catch (Exception ex) { Fail(ex); }
        }

        void Fail(Exception ex)
        {
            Debug.LogError("HANDSLAB_GRAB_PROBE_FAILED " + ex);
            output?.Dispose(); output = null; enabled = false;
        }

        void OnDestroy()
        {
            foreach (var action in unsubscribe) action();
            output?.Dispose(); output = null;
        }
    }
}
