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
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Lost-object recovery: policy + controller fixture with explicit clock + real-scene geometry audit.
    // Editor evidence only; no claim of headset physics.
    public static class ResearchObjectRecoveryChecks
    {
        [MenuItem("VR Research/Research Flow/Check Lost Object Recovery")]
        public static void Run()
        {
            var checks = new List<string>();
            void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("Recovery check failed: " + name); checks.Add(name); }

            // Measured Training room: WallShell (5.0,0,-0.2)-(14.4,3.6,9.0), Training_Floor top y=0, table top y=0.92.
            var room = new Bounds(); room.SetMinMax(new Vector3(5f, 0f, -.2f), new Vector3(14.4f, 3.6f, 9f));
            string R(float x, float y, float z, bool embedded = false) => ResearchObjectRecovery.Reason(new Vector3(x, y, z), room, 0f, embedded);
            Check(R(9.97f, .94f, 4.3f) == null, "resting on table is not lost");
            Check(R(9f, .02f, 3f) == null, "dropped on floor within reach is not lost");
            Check(R(5.25f, .02f, 7.95f) == null, "floor corner inside room is not lost");
            Check(R(13.1f, .02f, -.1f) == null, "door threshold is not lost");
            Check(R(9.7f, -.6f, 4f) == "below_floor", "below floor");
            Check(R(9.7f, -40f, 4f) == "below_floor", "fell out of world");
            Check(R(9.7f, 1f, -3f) == "out_of_bounds", "thrown into corridor beyond margin");
            Check(R(30f, 1f, 4f) == "out_of_bounds", "far outside room");
            Check(R(9.7f, 4.5f, 4f) == "out_of_bounds", "above ceiling");
            Check(R(float.NaN, 1f, 4f) == "non_finite", "non-finite position");
            Check(R(9.7f, .5f, 4f, embedded: true) == "unreachable", "inside static geometry");
            Check(!ResearchObjectRecovery.Due(true, 999f), "never while held");
            Check(!ResearchObjectRecovery.Due(false, ResearchObjectRecovery.DwellSeconds - .01f), "not immediately after release");
            Check(ResearchObjectRecovery.Due(false, ResearchObjectRecovery.DwellSeconds), "due after dwell");

            ControllerFixture(room, Check);
            SceneAudit(Check);
            Debug.Log("RESEARCH_OBJECT_RECOVERY_PASS checks=" + checks.Count + "; Editor policy/fixture/scene geometry only, not hardware physics");
        }

        static void ControllerFixture(Bounds room, Action<bool, string> Check)
        {
            string directory = Path.GetFullPath("Logs/QuestFinalIntegration/object-recovery-fixture");
            Directory.CreateDirectory(directory);
            var root = new GameObject("ResearchObjectRecovery_ControllerFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false); // no controller Awake: no registry, no scene lookup
            var itemRoot = new GameObject("RecoveryFixtureObject") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var authored = new Vector3(9.97f, 1.02f, 4.3f); var authoredRotation = Quaternion.Euler(0, 30, 0);
                itemRoot.transform.SetPositionAndRotation(authored, authoredRotation);
                var item = itemRoot.AddComponent<HandsLabObject>(); item.stableId = "fixture.training_object";
                item.body = itemRoot.AddComponent<Rigidbody>(); item.grabbable = itemRoot.AddComponent<Grabbable>();
                item.Initialize(null);
                controller.trainingObjects = new[] { item };
                var tick = typeof(ResearchSessionFlowController).GetMethod("RecoverLostObjects", BindingFlags.Instance | BindingFlags.NonPublic);
                var pointer = typeof(HandsLabObject).GetMethod("Pointer", BindingFlags.Instance | BindingFlags.NonPublic);
                void Tick(float now) => tick.Invoke(controller, new object[] { now });
                void Move(float x, float y, float z) { itemRoot.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(80, 10, 5)); item.body.linearVelocity = new Vector3(0, -3, 1); item.body.angularVelocity = Vector3.one; }
                using (var logger = new HandsLabLogger(directory, "None", "Editor recovery fixture", technicalTest: true, testData: true))
                {
                    typeof(ResearchSessionFlowController).GetProperty("Logger").SetValue(controller, logger);
                    JObject[] Recoveries()
                    {
                        var rows = new List<JObject>();
                        using (var reader = new StreamReader(new FileStream(logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                            for (string line; (line = reader.ReadLine()) != null;) rows.Add(JObject.Parse(line));
                        return rows.Where(r => (string)r["event"] == "SYSTEM_OBJECT_RECOVERY").ToArray();
                    }

                    var badAuthored = new Bounds(new Vector3(100, 0, 100), Vector3.one);
                    controller.ConfigureRecovery(badAuthored, 0f, null);
                    Move(9.7f, -5f, 4f); Tick(0); Tick(10);
                    Check(Recoveries().Length == 0, "authored pose outside reference disables recovery (no loop)");

                    item.ResetToAuthored(); // Awake configures while objects are still at their authored pose
                    controller.ConfigureRecovery(room, 0f, null);
                    Move(9.97f, .94f, 4.3f); Tick(20); Tick(30);
                    Move(9f, .03f, 3f); Tick(31); Tick(40);
                    Check(Recoveries().Length == 0, "table and in-reach floor drops never reset");

                    pointer.Invoke(item, new object[] { new PointerEvent(7, PointerEventType.Select, Pose.identity) });
                    Check(item.IsHeld, "fixture holds object");
                    Move(9.7f, -5f, 4f); Tick(50); Tick(60);
                    Check(Recoveries().Length == 0, "held object never reset");
                    pointer.Invoke(item, new object[] { new PointerEvent(7, PointerEventType.Unselect, Pose.identity) });
                    Move(9.7f, -5f, 4f);
                    Tick(61); Tick(61 + ResearchObjectRecovery.DwellSeconds - .1f);
                    Check(Recoveries().Length == 0, "no reset immediately after release/throw");
                    Tick(61 + ResearchObjectRecovery.DwellSeconds);
                    var events = Recoveries();
                    Check(events.Length == 1, "below-world object reset once");
                    Check(itemRoot.transform.position == authored && itemRoot.transform.rotation == authoredRotation, "exact authored pose");
                    Check(item.body.linearVelocity == Vector3.zero && item.body.angularVelocity == Vector3.zero, "zero velocity");
                    var e = events[0];
                    Check((string)e["source"] == "System" && (string)e["target"] == item.stableId && (string)e["reason"] == "below_floor"
                        && (float)e["before"]["position"][1] < -4f && (float)e["after"]["position"][1] == authored.y, "neutral System log with before/after");
                    for (float t = 70; t < 100; t += 5) Tick(t);
                    Check(Recoveries().Length == 1, "no repeat after reset");

                    Move(9.7f, 1f, -3f); Tick(100); Tick(100 + ResearchObjectRecovery.DwellSeconds);
                    events = Recoveries();
                    Check(events.Length == 2 && (string)events[1]["reason"] == "out_of_bounds" && itemRoot.transform.position == authored, "far outside room reset");
                    Move(9f, .03f, 3f); Tick(110); Move(9.7f, -5f, 4f); Tick(111); Move(9f, .03f, 3f); Tick(112); Move(9.7f, -5f, 4f); Tick(113);
                    Check(Recoveries().Length == 2, "dwell restarts when object returns in bounds");
                    logger.Complete("isolated_object_recovery_fixture");
                    typeof(ResearchSessionFlowController).GetProperty("Logger").SetValue(controller, null);
                    File.WriteAllText(Path.Combine(directory, "result.json"), JsonConvert.SerializeObject(new { passed = true, events = logger.EventsPath,
                        hardware_verified = false, scope = "Controller recovery logic with explicit clock; not native physics" }, Formatting.Indented));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(itemRoot); }
        }

        // Real scene: reference geometry exists, authored poses are not lost, Meta RespawnOnDrop stays inactive.
        static void SceneAudit(Action<bool, string> Check)
        {
            var scene = SceneManager.GetActiveScene();
            Check(scene.path.EndsWith("VRResearch_ResearchFlow_V2.unity"), "ResearchFlow V2 scene loaded");
            var environment = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Environment_V2");
            var floor = environment ? environment.transform.Find("Training/Training_Floor")?.GetComponent<Collider>() : null;
            var shell = environment ? environment.transform.Find("Training/Training_WallShell")?.GetComponent<Collider>() : null;
            Check(floor && shell, "Training floor and wall shell colliders found");
            Check(Mathf.Abs(floor.bounds.max.y) < .001f, "floor top y=0");
            var controller = UnityEngine.Object.FindObjectsByType<ResearchSessionFlowController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            Check(controller.trainingObjects.Length == 2, "two practice objects");
            // P7: a 50 Hz step under the 72 Hz display froze released objects on ~1 in 3 frames. ISDK moves held
            // bodies through the transform, so match the step rate instead of enabling Rigidbody interpolation.
            Check(Mathf.Abs(Time.fixedDeltaTime - 1f / 72) < 1e-6f, "physics step matches 72 Hz display");
            foreach (var item in controller.trainingObjects)
            {
                Check(item.body.interpolation == RigidbodyInterpolation.None && !item.body.isKinematic && item.body.useGravity,
                    item.stableId + " dynamic without interpolation");
                var p = item.transform.position;
                Check(ResearchObjectRecovery.Reason(p, shell.bounds, floor.bounds.max.y, false) == null, item.stableId + " authored pose in reference");
                var hits = Physics.OverlapSphere(p, .002f, ~0, QueryTriggerInteraction.Ignore);
                Check(!hits.Any(h => !h.attachedRigidbody && h.transform.IsChildOf(environment.transform)), item.stableId + " authored pose not embedded");
            }
            var respawn = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(m => m.GetType().Name == "RespawnOnDrop").ToArray();
            Check(respawn.All(r => !r.enabled), "Meta sample RespawnOnDrop residue stays disabled (no second reset path)");
        }
    }
}
#endif
