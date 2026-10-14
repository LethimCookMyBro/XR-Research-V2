#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchExamineChecks
    {
        [MenuItem("VR Research/Research Flow/Check Deliberate Examine")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run checks in Edit Mode.");
            var checks = new List<string>();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Examine_Check_Fixture"); SceneManager.MoveGameObjectToScene(root, scene);
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var flow = new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5);
                Set(controller, "Flow", flow);
                Set(controller, "Record", new ParticipantRecord { test_data = true, language = ParticipantLanguage.Thai });
                var task = root.AddComponent<ResearchTaskPort>(); task.taskId = "A"; task.demoMode = true; task.SetInteractionAllowed(true);
                var targetObject = new GameObject("Target", typeof(RectTransform), typeof(Image), typeof(Button)); targetObject.transform.SetParent(root.transform);
                targetObject.GetComponent<Button>().targetGraphic = targetObject.GetComponent<Image>();
                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); labelObject.transform.SetParent(targetObject.transform);
                var target = targetObject.AddComponent<ResearchExamineTarget>(); target.controller = controller; target.task = task; target.stableTarget = "A.module"; target.targetKind = 0;
                // Make listener lifecycle explicit: these are Editor component checks, not claimed user input.
                target.SendMessage("OnDisable"); target.SendMessage("OnEnable");
                var button = targetObject.GetComponent<Button>(); int events = 0; string eventTarget = "", eventName = ""; object eventAfter = null;
                task.Behavior += (name, stable, before, after) => { events++; eventTarget = stable; eventName = name; eventAfter = after; };
                var pointer = new PointerEventData(null) { pointerId = -1 };
                Check(!ResearchUIFeedback.TryPulse(null, pointer.pointerId) && !ResearchUIFeedback.TryPulse(controller, pointer.pointerId),
                    "unattributed Editor input does not request controller haptics", checks);
                Check(target.Available, "active matching task permits examination", checks);
                Check(!target.ChipVisible && !button.targetGraphic.raycastTarget, "idle target has no label or invisible button hit area", checks);
                target.OnPointerDown(pointer); button.onClick.Invoke();
                Check(events == 0, "unfocused target cannot emit an examination", checks);
                Focus(target);
                target.OnPointerEnter(pointer);
                Check(target.ChipVisible && events == 0, "focus and hover reveal only the neutral chip without logging", checks);
                button.onClick.Invoke(); Check(events == 0, "click without deliberate pointer source is ignored", checks);
                target.OnPointerDown(pointer); button.onClick.Invoke();
                Check(events == 1 && eventName == "EXAMINE" && eventTarget == "A.module", "deliberate press emits stable assembly EXAMINE target once", checks);
                button.onClick.Invoke(); Check(events == 1, "consumed pointer cannot create duplicate behavior", checks);
                target.OnPointerExit(pointer);
                Check(events == 1, "pointer exit cannot record behavior", checks);
                flow.Inspect(false); target.OnPointerDown(pointer); button.onClick.Invoke();
                Check(events == 1 && !target.Available, "Inspect fail modal blocks examination", checks);
                flow.ContinueInspect(); Check(events == 1, "closing fail does not auto-examine", checks);
                target.OnPointerDown(pointer); button.onClick.Invoke(); Check(events == 2, "participant selects examination after fail", checks);
                flow.Pause(); target.OnPointerDown(pointer); button.onClick.Invoke(); Check(events == 2, "safety pause blocks examination", checks); flow.ResearcherResume();
                task.taskId = "B"; target.OnPointerDown(pointer); button.onClick.Invoke(); Check(events == 2, "other station task cannot emit", checks); task.taskId = "A";
                task.SetInteractionAllowed(false); target.OnPointerDown(pointer); button.onClick.Invoke(); Check(events == 2, "adapter interaction gate blocks examination", checks); task.SetInteractionAllowed(true);
                Set(controller, "Record", new ParticipantRecord { test_data = false, language = ParticipantLanguage.Thai });
                target.OnPointerDown(pointer); button.onClick.Invoke(); Check(events == 2, "untracked desktop input cannot masquerade as study input", checks);
                Set(controller, "Record", new ParticipantRecord { test_data = true, language = ParticipantLanguage.Japanese });
                target.SendMessage("Update"); Check(labelObject.GetComponent<TMP_Text>().text == ResearchStrings.Get("action.examine", "Japanese"), "Japanese chip contains only the neutral action", checks);
                var otherObject = new GameObject("OtherTarget", typeof(RectTransform), typeof(Image), typeof(Button));
                otherObject.transform.SetParent(root.transform);
                var other = otherObject.AddComponent<ResearchExamineTarget>(); other.controller = controller; other.task = task;
                other.SendMessage("OnDisable"); other.SendMessage("OnEnable");
                Focus(other);
                Check(other.ChipVisible && !target.ChipVisible && !button.targetGraphic.raycastTarget, "switching physical target leaves only one chip and hit area", checks);
                target.OnPointerDown(pointer); button.onClick.Invoke();
                Check(events == 2, "old hidden chip cannot record examination", checks);
                var physical = new GameObject("PhysicalComponent", typeof(BoxCollider));
                physical.transform.SetParent(root.transform); physical.transform.position = new Vector3(3, 1, 0);
                var collider = physical.GetComponent<BoxCollider>(); target.ConfigureFocus(new Collider[] { collider }, physical.transform);
                Physics.SyncTransforms();
                Check(target.TryFocusHit(new Ray(new Vector3(3, 1, -2), Vector3.forward), 3, out float distance) && distance > 1,
                    "focus ray intersects the configured physical collider", checks);
                Check(!target.TryFocusHit(new Ray(new Vector3(0, 1, -2), Vector3.forward), 3, out _),
                    "ray beside physical component cannot focus a large invisible UI region", checks);
                collider.enabled = false;
                Check(!target.TryFocusHit(new Ray(new Vector3(3, 1, -2), Vector3.forward), 3, out _), "disabled collider cannot acquire focus", checks);
                Focus(target); target.stableTarget = "A.connection"; target.enabled = false;
                target.OnPointerDown(pointer); button.onClick.Invoke();
                Check(!target.Available && !target.ChipVisible && events == 2, "disabled reserved target cannot emit a connection examination", checks);
                target.enabled = true;
                target.stableTarget = "A.module"; Focus(target);
                target.OnPointerEnter(pointer); target.OnPointerDown(pointer);
                target.OnPointerExit(pointer); target.OnPointerEnter(pointer);
                button.onClick.Invoke();
                Check(events == 3 && eventName == "EXAMINE" && eventTarget == "A.module"
                    && (string)((Dictionary<string, object>)eventAfter)["input_source"] == "None",
                    "press-exit-enter-click retains the deliberate technical fixture source", checks);
                button.onClick.Invoke(); Check(events == 3, "returning pointer press is consumed only once", checks);
                target.OnPointerDown(pointer); Focus(other); Focus(target); button.onClick.Invoke();
                Check(events == 3, "focus replacement cancels the old deliberate press", checks);
                var comparison = root.AddComponent<ResearchRenderExperiment>();
                Set(comparison, "Running", true);
                typeof(ResearchSessionFlowController).GetProperty("RenderComparison", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, comparison);
                target.OnPointerDown(pointer); button.onClick.Invoke();
                Check(events == 3, "controlled render comparison rejects deliberate Examine without success feedback", checks);
                Set(comparison, "Running", false);
                typeof(ResearchSessionFlowController).GetProperty("RenderComparison", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, null);
                Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.PostTask1Checkpoint, 5));
                Check(!target.Available, "questionnaire checkpoint disables target", checks);
                string path = "Logs/QuestFinalIntegration/interaction-checks/examine-tests.json";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(new { passed = checks.Count, failed = 0, checks, scope = "Editor component callbacks; not hardware or pixel evidence" }, Formatting.Indented));
                Debug.Log("RESEARCH_EXAMINE_CHECKS=" + checks.Count + "/" + checks.Count);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void Set(object target, string property, object value) => target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance).SetValue(target, value);
        static void Focus(ResearchExamineTarget target) => typeof(ResearchExamineTarget).GetMethod("SetFocus", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { target });
        static void Check(bool passed, string name, List<string> checks) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
    }
}
#endif
