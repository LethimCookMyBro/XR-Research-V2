#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Owner video 29 Oct: walking so the fan guard sat between the eye and an already placed Reader hid it every ~0.3 s.
    public static class ResearchIdleOcclusionChecks
    {
        public static string Run()
        {
            var root = new GameObject("IdleOcclusionFixture");
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            int checks = 0;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
            try
            {
                var view = root.AddComponent<ResearchFlowView>();
                var head = new GameObject("Head").transform; head.SetParent(root.transform);
                head.position = new Vector3(40, 50, 40); view.Head = head;
                var slate = new GameObject("Slate", typeof(RectTransform), typeof(Canvas), typeof(ResearchPanelMotion));
                slate.transform.SetParent(root.transform);
                ((RectTransform)slate.transform).sizeDelta = new Vector2(840, 760);
                slate.transform.localScale = Vector3.one * .001f;
                var panel = slate.GetComponent<ResearchPanelMotion>(); panel.view = view; panel.panelId = "reference"; view.referencePanel = panel;
                var pose = new Pose(head.position + Vector3.forward * 1.2f, Quaternion.identity);
                slate.transform.SetPositionAndRotation(pose.position, pose.rotation);
                var lateUpdate = (Action)typeof(ResearchPanelMotion).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .CreateDelegate(typeof(Action), panel);
                void Frame(Vector3 nudge) { head.position += nudge; Physics.SyncTransforms(); lateUpdate(); }

                obstacle.transform.position = head.position + Vector3.forward * 5; obstacle.transform.localScale = Vector3.one * .2f;
                Frame(Vector3.zero);
                Check(!panel.PlacementBlocked, "unobstructed placed panel is shown");

                obstacle.transform.position = head.position + Vector3.forward * .6f; Physics.SyncTransforms();
                Check(!panel.PlacementClear(pose.position, pose.rotation), "a new placement behind equipment is still rejected");
                for (int i = 0; i < 6; i++) Frame(Vector3.right * .01f * (i % 2 == 0 ? 1 : -1));
                Check(!panel.PlacementBlocked && slate.GetComponent<Canvas>().enabled, "equipment in the sight line does not conceal a placed panel");
                Check(slate.transform.position == pose.position, "placed panel stays where it was");

                obstacle.transform.position = pose.position; obstacle.transform.localScale = Vector3.one * .3f;
                Frame(Vector3.right * .01f);
                Check(panel.PlacementBlocked || slate.transform.position != pose.position, "equipment intersecting the panel still conceals or re-places it");
                return "IDLE_OCCLUSION_PASS checks=" + checks + "; Editor fixture, not Quest hardware";
            }
            finally { UnityEngine.Object.DestroyImmediate(obstacle); UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
#endif
