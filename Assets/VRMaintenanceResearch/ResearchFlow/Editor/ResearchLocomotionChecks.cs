#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Oculus.Interaction.Input;
using Oculus.Interaction.Locomotion;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchLocomotionChecks
    {
        // P8: every authored teleport destination stands on floor with body/head clearance; locomotion is teleport + snap 45 only.
        [UnityEditor.MenuItem("VR Research/Research Flow/Check Teleport Destinations")]
        public static void RunDestinations()
        {
            int checks = 0;
            void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("FAIL " + name); checks++; }
            var controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>(FindObjectsInactive.Include);
            var rig = controller.origin.transform;
            bool Static(Collider c) => !c.attachedRigidbody && !c.transform.IsChildOf(rig) && !c.transform.IsChildOf(controller.view.transform);
            var destinations = UnityEngine.Object.FindObjectsByType<TeleportInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Check(destinations.Length == 30, "30 destinations (3 stations + 27 neutral), got " + destinations.Length);
            var points = new System.Collections.Generic.List<Vector3>();
            foreach (var destination in destinations)
            {
                var serialized = new UnityEditor.SerializedObject(destination);
                var target = serialized.FindProperty("_targetPoint").objectReferenceValue as Transform;
                var p = target ? target.position : destination.transform.position; string id = destination.transform.parent.name;
                Check(destination.isActiveAndEnabled && serialized.FindProperty("_allowTeleport").boolValue, id + " enabled");
                Check(Physics.Raycast(p + Vector3.up * .5f, Vector3.down, out var floor, 1f, ~0, QueryTriggerInteraction.Ignore) && Static(floor.collider)
                    && Mathf.Abs(floor.point.y) < .02f && floor.normal.y > .99f, id + " on level floor");
                var body = Physics.OverlapCapsule(p + Vector3.up * .3f, p + Vector3.up * 1.65f, .25f, ~0, QueryTriggerInteraction.Ignore).Where(Static).ToArray();
                Check(body.Length == 0, id + " body clear of " + string.Join(",", body.Select(c => c.name)));
                Check(!Physics.Raycast(p + Vector3.up * .5f, Vector3.up, out var ceiling, 1.7f, ~0, QueryTriggerInteraction.Ignore) || !Static(ceiling.collider), id + " head clearance 2.2 m");
                Check(points.All(q => Vector3.Distance(p, q) >= .5f), id + " distinct");
                points.Add(p);
            }
            var moving = UnityEngine.Object.FindFirstObjectByType<MovingSetting>(FindObjectsInactive.Include);
            var teleportObjects = new UnityEditor.SerializedObject(moving).FindProperty("_teleportGameObjects");
            Check(moving.ControllerMovement.Value == MovingSetting.MovementStyle.Teleport, "controller movement Teleport (slide objects disabled at Start)");
            Check(Enumerable.Range(0, teleportObjects.arraySize).All(i => ((GameObject)teleportObjects.GetArrayElementAtIndex(i).objectReferenceValue).name != "ControllerStepInteractor"), "unlogged thumbstick step not enabled by Teleport mode");
            Check(rig.GetComponentsInChildren<StepLocomotionBroadcaster>(true).All(s => !s.gameObject.activeSelf), "step interactors inactive");
            var turning = UnityEngine.Object.FindFirstObjectByType<TurningSetting>(FindObjectsInactive.Include);
            Check(turning.ControllerTurn.Value == TurningSetting.RotationStyle.Snap && Mathf.Approximately(turning.RotationSnapAngle.Value, 45), "controller turn Snap 45 (applied to controller turners at Start)");
            Check(new UnityEditor.SerializedObject(turning).FindProperty("_controllerTurners").arraySize == 2, "both controller turners driven by setting");
            Check(rig.GetComponentsInChildren<TurnerEventBroadcaster>(true).All(t => Mathf.Approximately(t.SnapTurnDegrees, 45)), "all turners 45 degrees");
            Debug.Log("RESEARCH_TELEPORT_DESTINATIONS_PASS checks=" + checks + "; scene colliders only, not headset comfort");
        }

        // Direct callback fixture only; does not assert headset tracking or SDK locomotion/input execution.
        public static string Run()
        {
            string evidence = Path.GetFullPath("Logs/ResearchFlowEvidence/2026-09-22-UXSecondPass/locomotion-callbacks");
            Directory.CreateDirectory(evidence);
            int checks = 0;
            void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("FAIL " + name); checks++; }
            var root = new GameObject("ResearchLocomotion_CallbackFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            using (var logger = new HandsLabLogger(evidence, "None", "Editor callback fixture", phase: "ResearchSession", taskId: "session", technicalTest: true, testData: true))
            {
                try
                {
                    var controller = root.AddComponent<ResearchSessionFlowController>();
                    typeof(ResearchSessionFlowController).GetProperty("Flow").SetValue(controller, new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions));
                    typeof(ResearchSessionFlowController).GetProperty("Logger").SetValue(controller, logger);
                    var guard = root.AddComponent<HandsLabInputGuard>();
                    guard.side = Handedness.Right;
                    typeof(HandsLabInputGuard).GetProperty("Mode").SetValue(guard, HandsLabInputGuard.InputMode.TrackedHand);
                    typeof(HandsLabInputGuard).GetProperty("TrackingValid").SetValue(guard, true);
                    var bridge = root.AddComponent<ResearchLocomotionEvents>(); bridge.controller = controller;
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var requested = typeof(ResearchLocomotionEvents).GetMethod("Requested", flags);
                    var completed = typeof(ResearchLocomotionEvents).GetMethod("Completed", flags);
                    JObject[] Events()
                    {
                        var rows = new System.Collections.Generic.List<JObject>();
                        using (var reader = new StreamReader(new FileStream(logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                            for (string line; (line = reader.ReadLine()) != null;) rows.Add(JObject.Parse(line));
                        return rows.ToArray();
                    }
                    var teleport = new LocomotionEvent(71, new Pose(new Vector3(1, 0, 2), Quaternion.identity), LocomotionEvent.TranslationType.Absolute, LocomotionEvent.RotationType.None);
                    requested.Invoke(bridge, new object[] { teleport, guard, "station.Training" });
                    Check(Events().Count(e => (string)e["event"] == "TELEPORT_INITIATED") == 1 && !Events().Any(e => (string)e["event"] == "TELEPORT_COMPLETED"), "real request boundary precedes completion");
                    requested.Invoke(bridge, new object[] { teleport, guard, "station.Training" });
                    Check(Events().Count(e => (string)e["event"] == "TELEPORT_INITIATED") == 1, "duplicate SDK request ignored");
                    typeof(HandsLabInputGuard).GetProperty("TrackingValid").SetValue(guard, false);
                    completed.Invoke(bridge, new object[] { teleport, new Pose(new Vector3(1, 0, 2), Quaternion.identity) });
                    var first = Events().Single(e => (string)e["event"] == "TELEPORT_INITIATED");
                    var last = Events().Single(e => (string)e["event"] == "TELEPORT_COMPLETED");
                    Check((string)first["action_id"] == (string)last["action_id"] && (long)first["seq"] < (long)last["seq"], "request and completion correlate in order");
                    Check((string)last["target"] == "station.Training" && (string)last["input_source"] == "HandTracking" && (string)last["handedness"] == "Right" && !(bool)last["tracking_valid"], "target hand and request source survive tracking loss");
                    Check((double)last["after"]["sdk_applied_delta"]["position"][0] == 1, "actual handled SDK delta retained");
                    completed.Invoke(bridge, new object[] { teleport, Pose.identity });
                    Check(Events().Count(e => (string)e["event"] == "TELEPORT_COMPLETED") == 1, "duplicate completion ignored");
                    var snap = new LocomotionEvent(72, Quaternion.Euler(0, -45, 0), LocomotionEvent.RotationType.Relative);
                    requested.Invoke(bridge, new object[] { snap, guard, "locomotion.snap.Right" });
                    completed.Invoke(bridge, new object[] { snap, new Pose(Vector3.zero, Quaternion.Euler(0, -45, 0)) });
                    Check(Mathf.Abs((float)Events().Single(e => (string)e["event"] == "SNAP_TURN")["after"]["requested_yaw_delta_degrees"] + 45) < .001f, "snap preserves signed angle");
                    var velocity = new LocomotionEvent(73, Quaternion.Euler(0, 20, 0), LocomotionEvent.RotationType.Velocity);
                    int before = Events().Length;
                    requested.Invoke(bridge, new object[] { velocity, guard, "locomotion.unsupported" });
                    completed.Invoke(bridge, new object[] { velocity, Pose.identity });
                    Check(Events().Length == before, "smooth motion never misclassified as snap");
                    var canceled = new LocomotionEvent(74, Vector3.one, LocomotionEvent.TranslationType.Absolute);
                    requested.Invoke(bridge, new object[] { canceled, guard, "station.Fan" });
                    typeof(ResearchLocomotionEvents).GetMethod("OnDisable", flags).Invoke(bridge, null);
                    Check((string)Events().Single(e => (string)e["event"] == "TELEPORT_CANCELLED")["source"] == "System", "uncompleted request cancellation is system work");
                    var locomotionRoot = new GameObject("LocomotionInputFixture"); locomotionRoot.transform.SetParent(root.transform);
                controller.locomotionInputRoots = new[] { locomotionRoot };
                typeof(ResearchSessionFlowController).GetField("locomotionGates", flags).SetValue(controller, new[] { locomotionRoot });
                    typeof(ResearchSessionFlowController).GetProperty("Record").SetValue(controller,
                        new ParticipantRecord { group = ParticipantGroup.Test, test_data = true, session_state = RegistryState.InProgress });
                    var refresh = typeof(ResearchSessionFlowController).GetMethod("Refresh", flags);
                    foreach (var state in new[] { ResearchFlowState.Task1Active, ResearchFlowState.Task2Active })
                    {
                        var flow = new ResearchSessionFlow(TaskOrder.AB, state, 5);
                        typeof(ResearchSessionFlowController).GetProperty("Flow").SetValue(controller, flow);
                        refresh.Invoke(controller, null);
                        Check(locomotionRoot.activeSelf, flow.CurrentTask + " permits task locomotion");
                        int start = Events().Length;
                        var taskTeleport = new LocomotionEvent(80 + (int)state, Vector3.one, LocomotionEvent.TranslationType.Absolute);
                        requested.Invoke(bridge, new object[] { taskTeleport, guard, "floor.neutral" });
                        completed.Invoke(bridge, new object[] { taskTeleport, Pose.identity });
                        var taskSnap = new LocomotionEvent(100 + (int)state, Quaternion.Euler(0, 45, 0), LocomotionEvent.RotationType.Relative);
                        requested.Invoke(bridge, new object[] { taskSnap, guard, "locomotion.snap.Right" });
                        completed.Invoke(bridge, new object[] { taskSnap, Pose.identity });
                        var taskEvents = Events().Skip(start).ToArray();
                        Check(taskEvents.Select(e => (string)e["event"]).SequenceEqual(new[] { "TELEPORT_INITIATED", "TELEPORT_COMPLETED", "SNAP_TURN" }), flow.CurrentTask + " logs task locomotion boundaries");
                        Check(taskEvents.All(e => (string)e["phase"] == "Experiment" && (string)e["task_id"] == flow.CurrentTask), flow.CurrentTask + " retains experiment task attribution");
                        flow.Pause(); refresh.Invoke(controller, null);
                        Check(!locomotionRoot.activeSelf, flow.CurrentTask + " pause disables locomotion");
                        start = Events().Length;
                        requested.Invoke(bridge, new object[] { taskTeleport, guard, "floor.neutral" });
                        completed.Invoke(bridge, new object[] { taskTeleport, Pose.identity });
                        Check(Events().Length == start, flow.CurrentTask + " pause emits no locomotion events");
                        flow.ResearcherResume(); flow.Inspect(false); refresh.Invoke(controller, null);
                        Check(!locomotionRoot.activeSelf, flow.CurrentTask + " inspection modal disables locomotion");
                        flow.ContinueInspect();
                        typeof(ResearchSessionFlowController).GetField("referenceNavigationHeld", flags).SetValue(controller, true);
                        refresh.Invoke(controller, null);
                        Check(!locomotionRoot.activeSelf, flow.CurrentTask + " reference navigation retains its input gate");
                        typeof(ResearchSessionFlowController).GetField("referenceNavigationHeld", flags).SetValue(controller, false);
                    }
                    foreach (var state in new[] { ResearchFlowState.ResearcherSetup, ResearchFlowState.Task1Ready, ResearchFlowState.PostTask1Checkpoint, ResearchFlowState.ParticipantComplete })
                    {
                        typeof(ResearchSessionFlowController).GetProperty("Flow").SetValue(controller, new ResearchSessionFlow(TaskOrder.AB, state, state == ResearchFlowState.ResearcherSetup ? 0 : 5));
                        refresh.Invoke(controller, null);
                        Check(!locomotionRoot.activeSelf, state + " retains locomotion gate");
                        before = Events().Length;
                        requested.Invoke(bridge, new object[] { teleport, guard, "floor.neutral" });
                        completed.Invoke(bridge, new object[] { teleport, Pose.identity });
                        Check(Events().Length == before, state + " emits no locomotion events");
                    }
                    typeof(ResearchSessionFlowController).GetProperty("Flow").SetValue(controller, new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                    foreach (string gate in new[] { "Transitioning", "Error" })
                    {
                        var property = typeof(ResearchSessionFlowController).GetProperty(gate);
                        property.SetValue(controller, gate == "Error" ? (object)"fixture_error" : true);
                        refresh.Invoke(controller, null);
                        Check(!locomotionRoot.activeSelf, gate + " disables locomotion");
                        before = Events().Length;
                        requested.Invoke(bridge, new object[] { teleport, guard, "floor.neutral" });
                        completed.Invoke(bridge, new object[] { teleport, Pose.identity });
                        Check(Events().Length == before, gate + " emits no locomotion events");
                        property.SetValue(controller, gate == "Error" ? (object)"" : false);
                    }
                    Check(Events().All(e => (bool)e["test_data"] && (bool)e["technical_test"]), "fixture never creates study data");
                    logger.Complete("isolated_callback_fixture");
                    string result = "RESEARCH_LOCOMOTION_CALLBACK_PASS checks=" + checks + "; direct callback fixture only, not hardware/input evidence";
                    File.WriteAllText(Path.Combine(evidence, "result.txt"), result + "\n" + logger.EventsPath);
                    return result;
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
    }
}
#endif
