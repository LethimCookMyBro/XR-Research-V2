#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.ResearchFlow;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchButtonDiagnostics
    {
        public static string TestSideClearances()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            var environment = scene.GetRootGameObjects().Single(g => g.name == "Environment_V2");
            var floors = environment.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy && c.name.EndsWith("_Floor")).ToArray();
            var obstacles = environment.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy && r.bounds.size.x < 4 && r.bounds.size.z < 4 && r.bounds.max.y > .1f).ToArray();

            var pts = new[] {
                ("Fan Left", new Vector3(-10.30f, 0, 5.15f)),
                ("Fan Right", new Vector3(-9.10f, 0, 5.15f)),
                ("Training Left", new Vector3(9.12f, 0, 4.95f)),
                ("Training Right", new Vector3(10.32f, 0, 4.95f))
            };

            foreach (var (name, pt) in pts)
            {
                bool floor = floors.Any(f => f.Raycast(new Ray(pt + Vector3.up, Vector3.down), out var hit, 1.1f) && Mathf.Abs(hit.point.y - pt.y) < .001f);
                var clearance = new Bounds(pt + Vector3.up * 1.1f, new Vector3(.8f, 2.1f, .8f));
                var furniture = obstacles.Where(r => clearance.Intersects(r.bounds)).Select(r => r.name).ToArray();
                var solid = Physics.OverlapCapsule(pt + Vector3.up * .4f, pt + Vector3.up * 1.75f, .4f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(c => c.transform.IsChildOf(environment.transform) && !floors.Contains(c)).Select(c => c.name).Distinct().ToArray();
                sb.AppendLine($"{name} ({pt.x:F2}, 0, {pt.z:F2}): floor={floor}, furniture=[{string.Join(",", furniture)}], solid=[{string.Join(",", solid)}]");
            }
            return sb.ToString();
        }

        public static string ActivateTaskAssemblies()
        {
            ResearchFlowSceneBuilder.ApplyContextExamineSecondPass();
            return "S1 assemblies bound; legacy props disabled.";
        }

        public static string InspectLiveState()
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            if (!controller) return "No controller";
            var sb = new StringBuilder();
            sb.AppendLine($"InspectPassed={controller.Flow?.InspectPassed}, Stage={typeof(ResearchSessionFlowController).GetField("taskTiming", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(controller)?.GetType().GetProperty("Stage")?.GetValue(typeof(ResearchSessionFlowController).GetField("taskTiming", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(controller))}");
            sb.AppendLine($"InspectionFeedbackVisible={controller.InspectionFeedbackVisible}");
            if (controller.view)
            {
                var actions = (UnityEngine.UI.Button[])typeof(ResearchFlowView).GetField("actions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(controller.view);
                if (actions != null)
                {
                    for (int i = 0; i < actions.Length; i++)
                    {
                        var a = actions[i];
                        var act = a ? a.GetComponent<ResearchUIAction>() : null;
                        sb.AppendLine($"Action_{i}: active={(a ? a.gameObject.activeSelf.ToString() : "null")}, interactable={(a ? a.interactable.ToString() : "null")}, isInteractable={(a ? a.IsInteractable().ToString() : "null")}, cmd={(act ? act.command : "null")}");
                    }
                }
                var allUIActions = controller.view.GetComponentsInChildren<ResearchUIAction>(true);
                foreach (var uia in allUIActions)
                {
                    var btn = uia.GetComponent<UnityEngine.UI.Button>();
                    sb.AppendLine($"UIAction: name={uia.name}, parent={uia.transform.parent?.name}, activeInHierarchy={uia.gameObject.activeInHierarchy}, btn={(btn != null)}, btnInteractable={(btn ? btn.IsInteractable().ToString() : "null")}, cmd={uia.command}");
                }
            }
            return sb.ToString();
        }
    }
}
#endif
