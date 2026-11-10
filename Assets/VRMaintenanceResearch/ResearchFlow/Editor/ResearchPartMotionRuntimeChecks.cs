#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchPartMotionRuntimeChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static void Set(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(name, Hidden | BindingFlags.DeclaredOnly);
                if (property != null) { property.SetValue(target, value); return; }
                var field = type.GetField(name, Hidden | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }
        static void Tick(ResearchPartMotionObserver observer) => typeof(ResearchPartMotionObserver).GetMethod("LateUpdate", Hidden).Invoke(observer, null);
        static JObject[] Rows(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(JObject.Parse).ToArray();
        }
        static JObject[] PartRows(string path) => Rows(path).Where(r => ((string)r["event"]).StartsWith("PART_", StringComparison.Ordinal)).ToArray();
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();

        public static string Run(string directory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !Path.IsPathRooted(directory) || Directory.Exists(directory))
                throw new InvalidOperationException("Stopped Editor and fresh absolute evidence directory required.");
            var donorA = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            var donorB = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
            if (!donorA || !donorB) throw new InvalidOperationException("Canonical A and B donors are required.");
            var originalScenes = Scenes(); var activeScene = SceneManager.GetActiveScene(); int previews = EditorSceneManager.previewSceneCount;
            var failures = new List<string>(); var checks = new List<object>(); var writers = new List<string>();
            Directory.CreateDirectory(directory);
            void Check(bool ok, string name, object observed = null)
            { checks.Add(new { name, ok, observed }); if (!ok) failures.Add(name); }
            foreach (string task in new[] { "A", "B" })
            {
                Scene scene = default; HandsLabLogger logger = null; ResearchSessionFlowController controller = null;
                try
                {
                    scene = EditorSceneManager.NewPreviewScene();
                    var context = new GameObject("OwnedPartMotionContext_" + task); SceneManager.MoveGameObjectToScene(context, scene);
                    controller = context.AddComponent<ResearchSessionFlowController>(); controller.enabled = false;
                    var port = context.AddComponent<ResearchTaskPort>(); port.taskId = task; port.demoMode = true;
                    port.BindSnapshot(() => new { task, fixture = true, state_marker = "owned_actual_observer_writer" }, value => false, value => { });
                    controller.taskPorts = new[] { port };
                    var state = task == "A" ? ResearchFlowState.Task1Active : ResearchFlowState.Task2Active;
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, state, 5));
                    logger = new HandsLabLogger(Path.Combine(directory, task), "SimulatedController", "StoppedEditorPartMotion", executionEnvironment: "Editor", phase: "ResearchSession", taskId: "session", technicalTest: true, testData: true);
                    Set(controller, "Logger", logger); writers.Add(logger.EventsPath);
                    port.SetInteractionAllowed(true);
                    var original = task == "A" ? donorA.ram[0].gameObject : donorB.spinner.gameObject;
                    var part = UnityEngine.Object.Instantiate(original); SceneManager.MoveGameObjectToScene(part, scene);
                    foreach (var behaviour in part.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                    var grab = part.GetComponent<Grabbable>(); var body = part.GetComponent<Rigidbody>();
                    if (!grab || !body) throw new InvalidOperationException("Donor has no grabbable rigidbody.");
                    foreach (var pointable in part.GetComponentsInChildren<PointableElement>(true))
                    {
                        Set(pointable, "_points", new List<Pose>()); Set(pointable, "_pointIds", new List<int>());
                        Set(pointable, "_selectingPoints", new List<Pose>()); Set(pointable, "_selectingPointIds", new List<int>());
                    }
                    var locker = body.GetComponent<RigidbodyKinematicLocker>() ?? body.gameObject.AddComponent<RigidbodyKinematicLocker>();
                    Set(locker, "_rigidbody", body); Set(locker, "_counter", 0);
                    string id;
                    if (task == "A")
                    {
                        var owner = context.AddComponent<ResearchS1TaskAMechanics>(); owner.enabled = false; owner.port = port; owner.controller = controller;
                        var input = part.GetComponent<ResearchS1TaskAInput>(); input.owner = owner; id = input.stableId;
                    }
                    else
                    {
                        var owner = context.AddComponent<ResearchS1TaskBMechanics>(); owner.enabled = false; owner.port = port; owner.controller = controller;
                        var fan = part.GetComponent<ResearchS1FanPart>(); fan.owner = owner; id = fan.stableId;
                    }
                    var physics = part.AddComponent<ResearchS1FreeBody>();
                    physics.Configure(body, grab, () => false, () => port.InteractionAllowed);
                    var observer = part.GetComponent<ResearchPartMotionObserver>();
                    Check(observer != null, task + "/actual Configure attaches observer");
                    controller.Log("TASK_STARTED", "task." + task, "Started", source: "System", taskIdOverride: task, phaseOverride: "Experiment", taskState: port.CaptureState());
                    var attempt = port.BeginAttempt(id, "SimulatedController");
                    Set(grab, "_selectingPoints", new List<Pose> { new Pose(body.position, body.rotation) });
                    Tick(observer);
                    var selected = PartRows(logger.EventsPath).Single(r => (string)r["event"] == "PART_SELECTED");
                    Check((string)selected["action_id"] == attempt.Id && (bool)selected["after"]["attempt_id_known"], task + "/selection retains actual attempt id", selected);
                    Check((bool)selected["after"]["held"] && (string)selected["source"] == "System", task + "/selection records observed held state");
                    body.position += Vector3.right * .04f; part.transform.position = body.position; Tick(observer);
                    int motionCount = PartRows(logger.EventsPath).Count(r => (string)r["event"] == "PART_MOTION_SAMPLE");
                    Check(motionCount == 1, task + "/moved donor produces one sampled pose", motionCount);
                    for (int i = 0; i < 20; i++) Tick(observer);
                    Check(PartRows(logger.EventsPath).Count(r => (string)r["event"] == "PART_MOTION_SAMPLE") == motionCount, task + "/unchanged pose does not flood");
                    Set(grab, "_selectingPoints", new List<Pose>()); body.linearVelocity = Vector3.right * .5f;
                    port.EndAttempt(attempt); Tick(observer);
                    var release = PartRows(logger.EventsPath).Single(r => (string)r["event"] == "PART_RELEASED");
                    Check((string)release["action_id"] == attempt.Id && !(bool)release["after"]["held"], task + "/release keeps correlation and no held state");
                    Check(Math.Abs((double)release["after"]["linear_velocity_world"][0] - .5) < .001, task + "/release writes actual rigidbody velocity");
                    Check(release["after"]["detail"]["boundary_position_world"] is JArray, task + "/boundary snapshot is persisted");
                    var cancellation = port.BeginAttempt(id, "SimulatedController"); Tick(observer);
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, state, 5, true)); port.SetInteractionAllowed(false); Tick(observer);
                    Check(PartRows(logger.EventsPath).Any(r => (string)r["event"] == "PART_CANCELLED" && (string)r["action_id"] == cancellation.Id), task + "/pause preserves cancellation boundary");
                    int pausedCount = PartRows(logger.EventsPath).Length; body.position += Vector3.right; Tick(observer);
                    Check(PartRows(logger.EventsPath).Length == pausedCount, task + "/paused motion is not attributed to active task");
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, task == "A" ? ResearchFlowState.Task2Active : ResearchFlowState.Task1Active, 5));
                    port.SetInteractionAllowed(true); body.position += Vector3.right; Tick(observer);
                    Check(PartRows(logger.EventsPath).Length == pausedCount, task + "/other task cannot write this part");
                    var rows = PartRows(logger.EventsPath);
                    Check(rows.All(r => (string)r["task_id"] == task && (string)r["target"] == id && (string)r["source"] == "System" && (string)r["execution_environment"] == "Editor"), task + "/all records retain observed provenance");
                    Check(rows.All(r => r["task_state"] != null && r["after"]["observation_epoch"] != null && (double)r["after"]["sample_period_seconds"] == .1), task + "/state and sampling metadata persisted");
                    if (task == "B") Check(rows.All(r => r["after"]["thread_progress_degrees"].Type == JTokenType.Float || r["after"]["thread_progress_degrees"].Type == JTokenType.Integer), "B/thread angle is numerical");
                    Check(logger.State == HandsLabLogState.Active && string.IsNullOrEmpty(logger.LastError), task + "/real logger has no write error", logger.LastError);
                    controller.Log("TASK_TECHNICAL_FAILURE", "task." + task, "FixtureEnded", source: "System", taskIdOverride: task, phaseOverride: "Experiment", taskState: port.CaptureState());
                }
                catch (Exception error) { Check(false, task + "/execution", (error.InnerException ?? error).ToString()); }
                finally
                {
                    if (controller) { Set(controller, "Logger", null); Set(controller, "Flow", null); }
                    logger?.Dispose();
                    if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                    SceneManager.SetActiveScene(activeScene);
                }
            }
            Check(originalScenes.SequenceEqual(Scenes()) && previews == EditorSceneManager.previewSceneCount, "caller scene state preserved");
            string report = Path.Combine(directory, "report.json");
            File.WriteAllText(report, JsonConvert.SerializeObject(new { status = failures.Count == 0 ? "PASS" : "ISSUES", failures, checks, writers,
                scope = "Stopped Editor donor bodies; real Port attempt lifecycle, observer callbacks, controller.Log and file writer. Selection list and poses are explicit synthetic fixtures. No native grab acquisition, collision callback, settled timing, wearer or Quest proof." }, Formatting.Indented));
            return "PART_MOTION_RUNTIME " + (failures.Count == 0 ? "PASS" : "ISSUES") + " checks=" + checks.Count + " failures=" + failures.Count;
        }
    }
}
#endif
