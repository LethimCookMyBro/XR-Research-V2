#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchObjectPointerChecks
    {
        // Isolated callback/logging regression. No claim of native controller input or physical grabbing.
        public static string Run()
        {
            string directory = Path.GetFullPath("Logs/QuestFinalIntegration/object-pointer-callbacks");
            Directory.CreateDirectory(directory);
            var root = new GameObject("ResearchObjectPointer_CallbackFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var checks = new List<string>();
            void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); }
            void Set(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
            try
            {
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var itemRoot = new GameObject("DummyObject"); itemRoot.transform.SetParent(root.transform);
                var item = itemRoot.AddComponent<HandsLabObject>(); item.stableId = "fixture.training_object";
                item.body = itemRoot.AddComponent<Rigidbody>(); item.grabbable = itemRoot.AddComponent<Grabbable>();
                var callback = typeof(ResearchSessionFlowController).GetMethod("ObjectPointer", BindingFlags.Instance | BindingFlags.NonPublic);
                void Emit(int pointer, PointerEventType type) => callback.Invoke(controller, new object[] { item, new PointerEvent(pointer, type, Pose.identity) });
                using (var logger = new HandsLabLogger(directory, "None", "Editor callback fixture", technicalTest: true, testData: true))
                {
                    Set(controller, "Logger", logger);
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialWelcome));
                    JObject[] Events()
                    {
                        var rows = new List<JObject>();
                        using (var reader = new StreamReader(new FileStream(logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                            for (string line; (line = reader.ReadLine()) != null;) rows.Add(JObject.Parse(line));
                        return rows.ToArray();
                    }
                    int baseline = Events().Length;
                    Emit(11, PointerEventType.Select); Emit(11, PointerEventType.Unselect);
                    Check(Events().Length == baseline, "Pre-tutorial rejected select/release emits no participant action");
                    Emit(11, PointerEventType.Cancel);
                    Check((string)Events().Last()["event"] == "SYSTEM_RELEASE" && (string)Events().Last()["source"] == "System", "Unpaired SDK cancel remains neutral system evidence");
                    var flow = new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions);
                    Set(controller, "Flow", flow);
                    Set(controller, "FloorPolicy", root.AddComponent<ResearchFloorPolicy>());
                    baseline = Events().Length;
                    Emit(11, PointerEventType.Select); Emit(11, PointerEventType.Unselect);
                    Check(Events().Length == baseline, "Unconfirmed native floor rejects participant grab even in active tutorial");
                    Set(controller, "FloorPolicy", null);
                    baseline = Events().Length;
                    Emit(11, PointerEventType.Move); Emit(11, PointerEventType.Hover); Emit(11, PointerEventType.Unhover);
                    Check(Events().Length == baseline, "Unused high-frequency pointer types have no logging side effects");
                    Emit(11, PointerEventType.Select);
                    var grab = Events().Last();
                    Check((string)grab["event"] == "ACTION" && (string)grab["result"] == "Grab", "Accepted select records participant grab");
                    baseline = Events().Length;
                    Emit(11, PointerEventType.Select); Emit(12, PointerEventType.Unselect);
                    Check(Events().Length == baseline, "Duplicate select and unmatched pointer release do not replace accepted history");
                    Emit(11, PointerEventType.Unselect);
                    var release = Events().Last();
                    Check((string)release["event"] == "ACTION" && (string)release["result"] == "Release" && (string)release["source"] == "User"
                        && (string)release["action_id"] == (string)grab["action_id"] && release["before"]?.Type == JTokenType.Object,
                        "Matching release retains accepted grab action and before snapshot");
                    baseline = Events().Length; Emit(11, PointerEventType.Unselect);
                    Check(Events().Length == baseline, "Duplicate release cannot create another participant action");
                    Emit(13, PointerEventType.Select); grab = Events().Last(); flow.Pause(); Emit(13, PointerEventType.Cancel);
                    var cancel = Events().Last();
                    Check((string)cancel["event"] == "SYSTEM_RELEASE" && (string)cancel["source"] == "System"
                        && (string)cancel["action_id"] == (string)grab["action_id"], "Accepted grab cancelled during pause retains neutral paired provenance");
                    baseline = Events().Length; Emit(13, PointerEventType.Unselect);
                    Check(Events().Length == baseline, "Late unselect after cancellation is not a user release");
                    Check(Events().All(e => (bool)e["technical_test"] && (bool)e["test_data"]), "All fixture records remain explicit technical data");
                    logger.Complete("isolated_object_pointer_callback_fixture");
                    File.WriteAllText(Path.Combine(directory, "result.json"), JsonConvert.SerializeObject(new { passed = true, checks,
                        events = logger.EventsPath, hardware_verified = false, scope = "Callback/logging only; native input and physics tested separately" }, Formatting.Indented));
                    Set(controller, "Logger", null);
                    return "RESEARCH_OBJECT_POINTER_PASS checks=" + checks.Count;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
#endif
