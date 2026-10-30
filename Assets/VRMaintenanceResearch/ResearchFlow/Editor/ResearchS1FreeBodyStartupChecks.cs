#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1FreeBodyStartupChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Hidden | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(name);
        }
        static void Set(object value, string name, object data) => Field(value.GetType(), name).SetValue(value, data);
        static void Start(object value) => value.GetType().GetMethod("Start", Hidden).Invoke(value, null);
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();

        public static string Run(string directory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor with XR inactive required.");
            if (!Path.IsPathRooted(directory) || Directory.Exists(directory) || File.Exists(directory))
                throw new IOException("Fresh absolute evidence directory required.");
            var donorA = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            var donorB = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
            if (!donorA || !donorB) throw new InvalidOperationException("Canonical A and B donors are required.");
            var beforeScenes = Scenes();
            var beforeActive = SceneManager.GetActiveScene();
            var beforeSockets = QASocket.All.ToArray();
            var failures = new List<string>();
            var rows = new List<object>();
            int checks = 0;
            Scene scene = default;
            void Check(bool ok, string name, object value = null)
            {
                checks++; rows.Add(new { name, passed = ok, value });
                if (!ok) failures.Add(name);
            }
            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                var rootA = UnityEngine.Object.Instantiate(donorA.gameObject);
                SceneManager.MoveGameObjectToScene(rootA, scene);
                rootA.name = "TEST_STARTUP_A"; rootA.SetActive(true);
                var rootB = UnityEngine.Object.Instantiate(donorB.gameObject);
                SceneManager.MoveGameObjectToScene(rootB, scene);
                rootB.name = "TEST_STARTUP_B"; rootB.SetActive(true);
                var context = new GameObject("TEST_STARTUP_CONTEXT");
                SceneManager.MoveGameObjectToScene(context, scene);
                var controller = context.AddComponent<ResearchSessionFlowController>();
                var a = rootA.GetComponent<ResearchS1TaskAMechanics>();
                var b = rootB.GetComponent<ResearchS1TaskBMechanics>();
                var portA = new GameObject("TEST_PORT_A").AddComponent<ResearchTaskPort>();
                portA.transform.SetParent(context.transform, false);
                var portB = new GameObject("TEST_PORT_B").AddComponent<ResearchTaskPort>();
                portB.transform.SetParent(context.transform, false);
                portA.taskId = "A"; portB.taskId = "B";
                portA.demoMode = portB.demoMode = true;
                a.port = portA; b.port = portB;
                a.controller = b.controller = controller;
                foreach (var root in new[] { rootA, rootB })
                {
                    foreach (var pointable in root.GetComponentsInChildren<PointableElement>(true))
                    {
                        Set(pointable, "_points", new List<Pose>());
                        Set(pointable, "_pointIds", new List<int>());
                        Set(pointable, "_selectingPoints", new List<Pose>());
                        Set(pointable, "_selectingPointIds", new List<int>());
                    }
                    foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                    {
                        var locker = body.GetComponent<RigidbodyKinematicLocker>() ?? body.gameObject.AddComponent<RigidbodyKinematicLocker>();
                        Set(locker, "_rigidbody", body);
                        Set(locker, "_counter", 0);
                    }
                }
                Check(a.AuthoredBindingsValid(), "owned complete A bindings valid");
                bool bValid = (bool)b.GetType().GetMethod("AuthoredBindingsValid", Hidden).Invoke(b, null);
                Check(bValid, "owned complete B bindings valid");
                if (!a.AuthoredBindingsValid() || !bValid)
                    throw new InvalidOperationException("Owned clone binding failed before startup.");
                Start(b);
                foreach (var plug in rootA.GetComponentsInChildren<ResearchS1TaskAPlugMotion>(true))
                {
                    plug.Initialize(plug.plug.grabbable);
                    Start(plug);
                }
                foreach (var fan in rootB.GetComponentsInChildren<ResearchS1FanPart>(true))
                {
                    fan.Initialize(fan.grabbable);
                    Start(fan);
                }
                foreach (var sled in rootA.GetComponentsInChildren<ResearchS1Sled>(true))
                {
                    sled.Initialize(sled.part.grabbable);
                    Start(sled);
                }
                foreach (var fastener in rootA.GetComponentsInChildren<ResearchS1TaskAFastener>(true))
                {
                    fastener.Initialize(fastener.grabbable);
                    Start(fastener);
                }
                Start(a);
                foreach (var input in rootA.GetComponentsInChildren<ResearchS1TaskAInput>(true)) Start(input);
                var helpersA = rootA.GetComponentsInChildren<ResearchS1FreeBody>(true);
                var helpersB = rootB.GetComponentsInChildren<ResearchS1FreeBody>(true);
                Check(helpersA.Length == 18, "all 18 A body callsites configured", helpersA.Length);
                Check(helpersB.Length == 6, "all 6 B body callsites configured", helpersB.Length);
                var helpers = helpersA.Concat(helpersB).ToArray();
                foreach (var helper in helpers)
                {
                    var body = helper.GetComponent<Rigidbody>();
                    Check(body.isKinematic && !helper.IsDynamic, "inactive body frozen " + helper.name);
                    var solids = body.GetComponentsInChildren<Collider>(true)
                        .Count(c => c.enabled && !c.isTrigger && c.attachedRigidbody == body);
                    Check(solids > 0, "body owns solid colliders " + helper.name, solids);
                }
                var beforeA = ResearchS1TaskEvaluator.Evaluate(a.Capture()).Category;
                var beforeB = ResearchS1TaskEvaluator.Evaluate(b.Capture()).Category;
                portA.SetInteractionAllowed(true);
                portB.SetInteractionAllowed(true);
                foreach (var helper in helpers)
                {
                    helper.Refresh();
                    bool shouldBeFree = helpersA.Contains(helper) && !(helper.GetComponent<ResearchS1TaskAFastener>()?.Captive ?? false);
                    Check(helper.IsFree == shouldBeFree && helper.IsDynamic == shouldBeFree &&
                        helper.GetComponent<Rigidbody>().isKinematic != shouldBeFree,
                        "active domain state " + helper.name, new { shouldBeFree, helper.IsFree, helper.IsDynamic });
                }
                Check(helpersA.Count(h => h.IsDynamic) == 15, "A has 15 free and 3 captive bodies");
                Check(helpersB.All(h => !h.IsDynamic), "all initial B parts remain assembly bound");
                Check(ResearchS1TaskEvaluator.Evaluate(a.Capture()).Category == beforeA &&
                    ResearchS1TaskEvaluator.Evaluate(b.Capture()).Category == beforeB,
                    "startup physics does not change task evaluation category");
                for (int i = 0; i < 10; i++)
                {
                    portA.SetInteractionAllowed(false); portB.SetInteractionAllowed(false);
                    Check(helpers.All(h => h.GetComponent<Rigidbody>().isKinematic), "pause freezes every real body " + i);
                    portA.SetInteractionAllowed(true); portB.SetInteractionAllowed(true);
                    Check(helpersA.Count(h => h.IsDynamic) == 15 && helpersB.All(h => !h.IsDynamic),
                        "resume restores domain states " + i);
                }
                portA.SetInteractionAllowed(false); portB.SetInteractionAllowed(false);
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                if (beforeActive.IsValid() && beforeActive.isLoaded) SceneManager.SetActiveScene(beforeActive);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
            }
            Check(beforeScenes.SequenceEqual(Scenes()) && SceneManager.GetActiveScene() == beforeActive,
                "caller scenes active scene dirty flags preserved");
            Check(new HashSet<QASocket>(beforeSockets).SetEquals(QASocket.All), "caller socket registry preserved");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonConvert.SerializeObject(new
            { evidence = "EDITOR_FULL_CLONE_REAL_START_AND_GATE_NOT_QUEST", checks, failed = failures.Count, failures, rows }, Formatting.Indented));
            return "FREE_BODY_STARTUP " + (failures.Count == 0 ? "PASS" : "FAIL") + " checks=" + checks + " failed=" + failures.Count;
        }
    }
}
#endif

