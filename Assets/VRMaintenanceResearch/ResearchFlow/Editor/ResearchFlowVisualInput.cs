#if UNITY_EDITOR
using System;
using System.Linq;
using Oculus.Interaction;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Opt-in visual fixture. Drives the installed rig through its real InputSystem bindings.
    // Creates no rig, hand, renderer, provider or camera. Never represents Quest/tracked-hand evidence.
    public static class ResearchFlowVisualInput
    {
        const string Layout = "HandsLabSimulatedResearchTouch";
        static InputDevice left, right;
        static XRSimulatedHMD head;
        static ResearchSessionFlowController controller;
        static Transform trackingSpace;
        static Vector3 headLocalPosition;
        static Quaternion headLocalRotation;
        static readonly Pose[] controllerPoses = new Pose[2];
        static readonly bool[] manualControllers = new bool[2];
        static readonly bool[] controllerTracked = { true, true };
        static readonly float[] grips = new float[2], triggers = new float[2];
        static readonly Vector2[] thumbsticks = new Vector2[2];
        static string pose = "nearUi", lastError = "";
        static double nextTick;
        static int baselineCameras, baselineOrigins, baselineRenderers;
        public static bool Running => head != null && head.added;

        // Simulated physical head motion in tracking space (walk/lean/crouch/turn); moves no rig or world transform.
        public static void SetHeadMotion(Vector3 trackingOffset, float yawDegrees)
        {
            if (!Running) throw new InvalidOperationException("Start the visual fixture first.");
            headMotion = new Pose(trackingOffset, Quaternion.Euler(0, yawDegrees, 0)); nextTick = 0; Tick();
        }
        public static void SetHeadFacing(Vector3 worldTarget)
        {
            if (!Running) throw new InvalidOperationException("Start the visual fixture first.");
            RequireTechnical();
            var direction = worldTarget - controller.origin.Camera.transform.position;
            float lengthSquared = direction.sqrMagnitude;
            if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) || lengthSquared < 1e-8f)
                throw new ArgumentException("Head target must define a finite nonzero direction.", nameof(worldTarget));
            var localRotation = Quaternion.Inverse(trackingSpace.rotation) * Quaternion.LookRotation(direction, trackingSpace.up);
            var baselineRotation = headLocalRotation * Quaternion.Euler(pose == "workstation" ? 12 : 0, 0, 0);
            headMotion = new Pose(headMotion.position, localRotation * Quaternion.Inverse(baselineRotation));
            nextTick = 0; Tick();
        }

        static Pose headMotion = Pose.identity;

        public static object Begin(float? syntheticEyeHeight = null)
        {
            if (Running) throw new InvalidOperationException("Visual input already running.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) throw new InvalidOperationException("Enter unpaused Play Mode first.");
            controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            RequireTechnical();
            if (InputSystem.devices.Any(d => d is XRController || d is XRHMD))
                throw new InvalidOperationException("XR devices already exist. Visual fixture will not take ownership.");
            var session = controller.GetComponentInParent<Transform>().root.GetComponentsInChildren<HandsLabSession>(true).FirstOrDefault();
            if (!session) session = UnityEngine.Object.FindFirstObjectByType<HandsLabSession>(FindObjectsInactive.Include);
            if (!session || session.enabled || !session.trackingSpace) throw new InvalidOperationException("Expected retained disabled HandsLabSession tracking-space reference.");
            trackingSpace = session.trackingSpace;
            var camera = controller.origin ? controller.origin.Camera : null;
            if (!camera) throw new InvalidOperationException("Existing research origin camera missing.");
            // Preserve current eye height exactly: the rig already includes its camera floor offset.
            headLocalPosition = trackingSpace.InverseTransformPoint(camera.transform.position);
            // Optional synthetic HMD input in tracking space, never a rig/camera-floor offset.
            if (syntheticEyeHeight.HasValue)
            {
                if (float.IsNaN(syntheticEyeHeight.Value) || float.IsInfinity(syntheticEyeHeight.Value) || syntheticEyeHeight.Value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(syntheticEyeHeight));
                headLocalPosition.y = syntheticEyeHeight.Value;
            }
            headLocalRotation = Quaternion.Inverse(trackingSpace.rotation) * camera.transform.rotation;
            baselineCameras = Count<Camera>(); baselineOrigins = Count<Unity.XR.CoreUtils.XROrigin>(); baselineRenderers = Count<Renderer>();
            pose = "nearUi"; lastError = ""; nextTick = 0; headMotion = Pose.identity;
            for (int side = 0; side < 2; side++) { manualControllers[side] = false; controllerTracked[side] = true; grips[side] = triggers[side] = 0; thumbsticks[side] = Vector2.zero; }
            try
            {
                if (!InputSystem.ListLayouts().Contains(Layout)) InputSystem.RegisterLayout(@"{""name"":""HandsLabSimulatedResearchTouch"",""extend"":""XRController"",""format"":""HLTC"",""controls"":[
{""name"":""primaryButton"",""layout"":""Button"",""offset"":0,""bit"":0},{""name"":""primaryTouched"",""layout"":""Button"",""offset"":0,""bit"":1},
{""name"":""secondaryButton"",""layout"":""Button"",""offset"":0,""bit"":2},{""name"":""secondaryTouched"",""layout"":""Button"",""offset"":0,""bit"":3},
{""name"":""gripPressed"",""layout"":""Button"",""offset"":0,""bit"":4},{""name"":""triggerPressed"",""layout"":""Button"",""offset"":0,""bit"":5},
{""name"":""triggerTouched"",""layout"":""Button"",""offset"":0,""bit"":6},{""name"":""thumbstickClicked"",""layout"":""Button"",""offset"":0,""bit"":7},
{""name"":""thumbstickTouched"",""layout"":""Button"",""offset"":1,""bit"":0},{""name"":""thumbrestTouched"",""layout"":""Button"",""offset"":1,""bit"":1},{""name"":""menu"",""layout"":""Button"",""offset"":1,""bit"":2},
{""name"":""thumbstick"",""layout"":""Vector2"",""offset"":4},{""name"":""grip"",""layout"":""Axis"",""offset"":12},{""name"":""trigger"",""layout"":""Axis"",""offset"":16},
{""name"":""devicePose"",""layout"":""Pose"",""offset"":32},{""name"":""pointer"",""layout"":""Pose"",""offset"":96},
{""name"":""isTracked"",""useStateFrom"":""devicePose/isTracked""},{""name"":""trackingState"",""useStateFrom"":""devicePose/trackingState""},
{""name"":""devicePosition"",""useStateFrom"":""devicePose/position""},{""name"":""deviceRotation"",""useStateFrom"":""devicePose/rotation""}]}");
                left = InputSystem.AddDevice(Layout, "HandsLabSimulatedResearchFlowLeft");
                right = InputSystem.AddDevice(Layout, "HandsLabSimulatedResearchFlowRight");
                InputSystem.SetDeviceUsage(left, CommonUsages.LeftHand); InputSystem.SetDeviceUsage(right, CommonUsages.RightHand);
                head = (XRSimulatedHMD)InputSystem.AddDevice("XRSimulatedHMD", "HandsLabSimulatedResearchFlowHMD");
                EditorApplication.update += Tick;
                EditorApplication.playModeStateChanged += PlayState;
                AssemblyReloadEvents.beforeAssemblyReload += Stop;
                Tick();
                if (!Running) throw new InvalidOperationException(lastError);
                return Audit();
            }
            catch { Stop(); throw; }
        }

        public static void SetPose(string value)
        {
            if (!Running) throw new InvalidOperationException("Start the visual fixture first.");
            if (value != "nearUi" && value != "workstation") throw new ArgumentException("Use nearUi or workstation.");
            RequireTechnical(); pose = value; nextTick = 0; Tick();
        }

        public static Pose ControllerWorldPose(int side)
        {
            if (!Running || side < 0 || side > 1) throw new InvalidOperationException("Running fixture and side 0/1 required.");
            float down = pose == "workstation" ? -.33f : -.20f;
            var local = manualControllers[side] ? controllerPoses[side] : new Pose(
                headLocalPosition + headLocalRotation * new Vector3(side == 0 ? -.24f : .24f, down, side == 0 ? .50f : .55f), headLocalRotation);
            var rotation = trackingSpace.rotation * local.rotation;
            float normSquared = Quaternion.Dot(rotation, rotation);
            if (float.IsNaN(normSquared) || float.IsInfinity(normSquared) || normSquared < 1.17549435e-38f)
                throw new InvalidOperationException("Controller rotation must have a finite normal squared norm.");
            return new Pose(trackingSpace.TransformPoint(local.position), rotation.normalized);
        }

        public static void SetControllerWorldPose(int side, Pose world, float grip = 0, float trigger = 0, Vector2 thumbstick = default)
        {
            ControllerWorldPose(side); RequireTechnical();
            var rotation = Quaternion.Inverse(trackingSpace.rotation) * world.rotation;
            float normSquared = Quaternion.Dot(rotation, rotation);
            if (float.IsNaN(normSquared) || float.IsInfinity(normSquared) || normSquared < 1.17549435e-38f)
                throw new ArgumentException("Controller rotation must have a finite normal squared norm.", nameof(world));
            controllerPoses[side] = new Pose(trackingSpace.InverseTransformPoint(world.position), rotation.normalized);
            manualControllers[side] = true; grips[side] = Mathf.Clamp01(grip); triggers[side] = Mathf.Clamp01(trigger);
            thumbsticks[side] = Vector2.ClampMagnitude(thumbstick, 1);
            nextTick = 0; Tick();
        }

        // Exercise the installed SDK's real tracking-validity path through the owned simulated PoseControl.
        public static void SetControllerTracked(int side, bool tracked)
        {
            ControllerWorldPose(side); RequireTechnical();
            controllerTracked[side] = tracked; nextTick = 0; Tick();
        }

        static void RequireTechnical()
        {
            // Headset technical setup: no record yet, and its only start path creates a DEV test-data record.
            if (controller && controller.Record == null && controller.TechnicalSelfSetup) return;
            if (!controller || controller.Record == null || !controller.Record.test_data || !controller.Record.participant_id.StartsWith("DEV", StringComparison.Ordinal))
                throw new InvalidOperationException("Visual fixture requires an active DEV test-data session.");
        }

        static void Tick()
        {
            if (!Running || !EditorApplication.isPlaying) return;
            bool inputBlocked = !Application.isFocused || EditorApplication.isPaused;
            if (inputBlocked || (controller && controller.Flow?.Paused == true && controller.PauseKind == "application"))
            {
                Array.Clear(grips, 0, grips.Length); Array.Clear(triggers, 0, triggers.Length);
                Array.Clear(thumbsticks, 0, thumbsticks.Length);
            }
            if (inputBlocked) return;
            try
            {
                RequireTechnical();
                if (!trackingSpace) throw new InvalidOperationException("Tracking space was removed.");
                if (InputSystem.devices.Any(d => (d is XRController || d is XRHMD) && d != left && d != right && d != head))
                    throw new InvalidOperationException("Another XR device appeared; stopping owned visual fixture.");
                if (EditorApplication.timeSinceStartup < nextTick) return;
                nextTick = EditorApplication.timeSinceStartup + 1d / 60;
                // Local pose follows the existing origin during standardized station transitions.
                var rotation = headMotion.rotation * headLocalRotation * Quaternion.Euler(pose == "workstation" ? 12 : 0, 0, 0);
                var headPosition = headLocalPosition + headMotion.position;
                var eyeOffset = rotation * Vector3.right * .032f;
                InputSystem.QueueStateEvent(head, new XRSimulatedHMDState
                {
                    isTracked = true, trackingState = 3, devicePosition = headPosition, deviceRotation = rotation,
                    centerEyePosition = headPosition, centerEyeRotation = rotation,
                    leftEyePosition = headPosition - eyeOffset, rightEyePosition = headPosition + eyeOffset,
                    leftEyeRotation = rotation, rightEyeRotation = rotation
                });
                for (int side = 0; side < 2; side++)
                {
                    var world = ControllerWorldPose(side);
                    var controllerRotation = Quaternion.Inverse(trackingSpace.rotation) * world.rotation;
                    float normSquared = Quaternion.Dot(controllerRotation, controllerRotation);
                    if (float.IsNaN(normSquared) || float.IsInfinity(normSquared) || normSquared < 1.17549435e-38f)
                        throw new InvalidOperationException("Controller rotation must have a finite normal squared norm.");
                    WriteController(side == 0 ? left : right, trackingSpace.InverseTransformPoint(world.position),
                        controllerRotation.normalized, grips[side], triggers[side], thumbsticks[side], controllerTracked[side]);
                }
            }
            catch (Exception error) { lastError = error.Message; Stop(); Debug.LogError("ResearchFlow visual fixture stopped: " + lastError); }
        }

        static void WriteController(InputDevice device, Vector3 position, Quaternion rotation, float grip, float trigger, Vector2 thumbstick, bool tracked)
        {
            using (StateEvent.From(device, out var state))
            {
                foreach (var name in new[] { "devicePose", "pointer" })
                {
                    var p = device.GetChildControl<PoseControl>(name);
                    p.isTracked.WriteValueIntoEvent(tracked ? 1f : 0f, state); p.trackingState.WriteValueIntoEvent(tracked ? 3 : 0, state);
                    p.position.WriteValueIntoEvent(position, state); p.rotation.WriteValueIntoEvent(rotation, state);
                }
                device.GetChildControl<AxisControl>("grip").WriteValueIntoEvent(grip, state);
                device.GetChildControl<AxisControl>("trigger").WriteValueIntoEvent(trigger, state);
                device.GetChildControl<Vector2Control>("thumbstick").WriteValueIntoEvent(thumbstick, state);
                foreach (var name in new[] { "gripPressed", "triggerPressed", "primaryButton", "secondaryButton", "primaryTouched", "triggerTouched" })
                    device.GetChildControl<ButtonControl>(name).WriteValueIntoEvent(0f, state);
                device.GetChildControl<ButtonControl>("gripPressed").WriteValueIntoEvent(grip > .7f ? 1f : 0f, state);
                device.GetChildControl<ButtonControl>("triggerPressed").WriteValueIntoEvent(trigger > .7f ? 1f : 0f, state);
                InputSystem.QueueEvent(state);
            }
        }

        public static object Audit() => new
        {
            running = Running, pose, error = lastError, input_source = "SimulatedController", hardware_verified = false,
            controller_tracked = controllerTracked.ToArray(),
            scope = "Existing Meta controller-driven hand visuals only; no tracked-hand or Quest verification",
            participant = controller ? controller.Record?.participant_id : null,
            technical_test = controller && controller.Record != null && controller.Record.test_data,
            head_local_position = Position(headLocalPosition), head_local_rotation = new { headLocalRotation.x, headLocalRotation.y, headLocalRotation.z, headLocalRotation.w },
            camera_world_position = Position(controller && controller.origin && controller.origin.Camera ? controller.origin.Camera.transform.position : Vector3.zero),
            no_extra_cameras = Count<Camera>() == baselineCameras,
            no_extra_origins = Count<Unity.XR.CoreUtils.XROrigin>() == baselineOrigins,
            no_extra_renderers = Count<Renderer>() == baselineRenderers,
            devices = new InputDevice[] { left, right, head }.Where(d => d != null).Select(d => new { d.name, d.layout, d.native, d.added, usages = d.usages.Select(u => u.ToString()).ToArray() }).ToArray(),
            guards = controller ? controller.guards.Where(g => g).Select(g => new { side = g.side.ToString(), mode = g.Mode.ToString(), g.TrackingValid, g.InteractionsAllowed, g.WaitingForNeutral, g.LastReason }).ToArray() : null,
            hand_meshes = controller ? controller.guards.Where(g => g && g.comprehensiveRigRoot).SelectMany(g => g.comprehensiveRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)).Distinct().Select(r => new { r.name, active = r.gameObject.activeInHierarchy, r.enabled, r.isVisible }).ToArray() : null
        };

        static int Count<T>() where T : UnityEngine.Object => UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        static object Position(Vector3 p) => new { p.x, p.y, p.z };
        static void PlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Stop(); }
        public static void Stop()
        {
            EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= PlayState;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            foreach (var device in new InputDevice[] { left, right, head }) if (device != null && device.added) InputSystem.RemoveDevice(device);
            left = null; right = null; head = null;
        }
    }
}
#endif
