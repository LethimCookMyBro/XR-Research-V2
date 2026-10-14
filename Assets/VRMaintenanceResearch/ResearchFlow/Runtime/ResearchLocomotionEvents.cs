using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction.Locomotion;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Observe the existing Meta locomotion graph; never move the player or create an input provider.</summary>
    public sealed class ResearchLocomotionEvents : MonoBehaviour
    {
        public ResearchSessionFlowController controller;
        readonly List<Action> unsubscribe = new List<Action>();
        readonly Dictionary<ulong, Pending> pending = new Dictionary<ulong, Pending>();
        readonly HashSet<ulong> completedTutorialEvents = new HashSet<ulong>();
        Oculus.Interaction.Locomotion.CharacterController character;
        bool started;
        public bool PlacementBusy => pending.Values.Any(item => item.teleport) || Time.frameCount <= teleportCompletedFrame;
        int teleportCompletedFrame = -1;

        sealed class Pending
        {
            public bool teleport;
            public string target, source, hand, action, phase, task;
            public bool tracking;
            public long instructionEpoch;
            public object before, taskState;
            public HandsLabInputGuard guard;
        }

        void Start() { started = true; Bind(); }
        void OnEnable() { if (started) Bind(); }
        void Bind()
        {
            if (!controller || !controller.locomotor || unsubscribe.Count != 0) return;
            character = controller.origin ? controller.origin.GetComponentInChildren<Oculus.Interaction.Locomotion.CharacterController>(true) : null;
            foreach (var guard in controller.guards)
            {
                if (!guard || !guard.interactionsRoot) continue;
                foreach (var teleport in guard.interactionsRoot.GetComponentsInChildren<TeleportInteractor>(true))
                {
                    var input = teleport; var owner = guard;
                    Action<LocomotionEvent> request = e => Requested(e, owner, input.Interactable ? Target(input.Interactable.transform) : "locomotion.unresolved_anchor");
                    input.WhenLocomotionPerformed += request;
                    unsubscribe.Add(() => { if (input) input.WhenLocomotionPerformed -= request; });
                }
                foreach (var turner in guard.interactionsRoot.GetComponentsInChildren<TurnerEventBroadcaster>(true))
                {
                    var input = turner; var owner = guard;
                    Action<LocomotionEvent> request = e => Requested(e, owner, "locomotion.snap." + owner.side);
                    input.WhenLocomotionPerformed += request;
                    unsubscribe.Add(() => { if (input) input.WhenLocomotionPerformed -= request; });
                }
            }
            controller.locomotor.WhenLocomotionEventHandled += Completed;
            unsubscribe.Add(() => { if (controller && controller.locomotor) controller.locomotor.WhenLocomotionEventHandled -= Completed; });
        }

        string Target(Transform target)
        {
            foreach (var station in controller.stations)
                if (station && (target == station || target.IsChildOf(station))) return "station." + station.name;
            string path = target.name;
            for (var parent = target.parent; parent && parent != controller.transform; parent = parent.parent) path = parent.name + "/" + path;
            return "teleport." + path;
        }

        void Requested(LocomotionEvent e, HandsLabInputGuard guard, string target)
        {
            if (controller.Flow?.CanInteract != true || controller.Transitioning || !string.IsNullOrEmpty(controller.Error)) return;
            bool teleport = e.Translation == LocomotionEvent.TranslationType.Absolute || e.Translation == LocomotionEvent.TranslationType.AbsoluteEyeLevel;
            bool snap = e.Translation == LocomotionEvent.TranslationType.None && e.Rotation == LocomotionEvent.RotationType.Relative;
            if ((!teleport && !snap) || pending.ContainsKey(e.EventId)) return; // Ignore denied/velocity events; no smooth locomotion classification.
            var item = new Pending
            {
                teleport = teleport, target = target, hand = guard.side.ToString(), source = InputSource(guard), tracking = guard.TrackingValid,
                action = "locomotion_" + e.EventId, before = Snapshot(), guard = guard,
                phase = controller.Flow.IsTutorial ? "Tutorial" : "Experiment",
                task = controller.Flow.IsTutorial ? "training" : controller.Flow.CurrentTask,
                taskState = controller.CaptureTaskState()
            };
            item.instructionEpoch = controller.TutorialActionAttempted(teleport ? "teleport" : "snap", item.action, item.source, item.tracking);
            pending.Add(e.EventId, item);
            // ISDK205 FirstPersonLocomotor queues these request types until LateUpdate. This callback is before application regardless of listener order.
            if (teleport) Record("TELEPORT_INITIATED", "Requested", item, new { destination = PoseValue(e.Pose), sdk_event_id = e.EventId }, "User");
        }

        void Completed(LocomotionEvent e, Pose appliedDelta)
        {
            if (!pending.TryGetValue(e.EventId, out var item))
            {
                if (completedTutorialEvents.Contains(e.EventId)) controller.Log("TUTORIAL_ACTION_DUPLICATE", "locomotion.sdk", "IgnoredDuplicateCommit",
                    after: new { sdk_event_id = e.EventId }, source: "System", actionId: "locomotion_" + e.EventId, reason: "duplicate_sdk_callback");
                return;
            }
            pending.Remove(e.EventId);
            if (item.teleport) teleportCompletedFrame = Time.frameCount;
            if (item.phase == "Tutorial") completedTutorialEvents.Add(e.EventId);
            Record(item.teleport ? "TELEPORT_COMPLETED" : "SNAP_TURN", "Completed", item,
                new { observed = Snapshot(), sdk_applied_delta = PoseValue(appliedDelta), sdk_event_id = e.EventId,
                    requested_yaw_delta_degrees = item.teleport ? (float?)null : Mathf.DeltaAngle(0, e.Pose.rotation.eulerAngles.y) }, "User");
            controller.TutorialActionCommitted(item.teleport ? "teleport" : "snap", item.instructionEpoch, item.action, item.source,
                item.tracking && item.guard && item.guard.TrackingValid && item.guard.InteractionsAllowed && !item.guard.SystemCancelling);
            if (item.teleport && isActiveAndEnabled) StartCoroutine(RecenterAfterTeleport());
        }

        IEnumerator RecenterAfterTeleport()
        {
            // The SDK applies the XR Origin after its handled callback; use the next frame's head pose.
            yield return null;
            if (!controller || !controller.view) yield break;
            if (controller.Flow?.CanInteract == true && !controller.Transitioning)
                controller.view.OnSpatialDiscontinuity("teleport_committed");
        }

        void Record(string name, string result, Pending item, object after, string source)
        {
            controller.Logger?.Record(name, item.target, result, item.taskState, item.hand, item.before, after, source,
                actionId: item.action, inputSourceOverride: item.source, trackingValid: item.guard ? item.guard.TrackingValid : item.tracking,
                phaseOverride: item.phase, taskIdOverride: item.task);
        }

        object Snapshot() => new
        {
            sdk_character_pose = character ? PoseValue(character.Pose) : null,
            // Meta updates XR Origin at end of frame after its handled callback. Label this observation instead of claiming a final HMD pose.
            origin_pose_at_callback = controller.origin ? PoseValue(new Pose(controller.origin.transform.position, controller.origin.transform.rotation)) : null,
            coordinate_space = "UnityWorld"
        };
        static object PoseValue(Pose pose) => new
        {
            position = new[] { pose.position.x, pose.position.y, pose.position.z },
            rotation = new[] { pose.rotation.x, pose.rotation.y, pose.rotation.z, pose.rotation.w }
        };
        static string InputSource(HandsLabInputGuard guard)
        {
            if (guard.Mode == HandsLabInputGuard.InputMode.TrackedHand) return "HandTracking";
            if (guard.Mode != HandsLabInputGuard.InputMode.Controller) return "None";
            string usage = guard.side == Oculus.Interaction.Input.Handedness.Left ? "LeftHand" : "RightHand";
            bool simulated = UnityEngine.InputSystem.InputSystem.devices.Any(d => d is UnityEngine.InputSystem.XR.XRController &&
                d.usages.Any(u => u.ToString() == usage) && (d.name.StartsWith("HandsLabSimulated") || d.layout.StartsWith("XRSimulated")));
            return simulated ? "SimulatedController" : "Controller";
        }
        void OnDisable()
        {
            foreach (var item in pending.Values)
                if (item.teleport) Record("TELEPORT_CANCELLED", "ObserverDisabledBeforeCompletion", item, null, "System");
            StopAllCoroutines();
            pending.Clear();
            completedTutorialEvents.Clear();
            foreach (var detach in unsubscribe) detach();
            unsubscribe.Clear();
        }
    }
}
