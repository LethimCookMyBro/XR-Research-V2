#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Owner 30 Oct: on Quest the Reader opened in a task queued its placement and never applied it, so nothing appeared.
    public static class ResearchReaderStuckChecks
    {
        public static string Run()
        {
            const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            var root = new GameObject("ReaderStuckFixture");
            int checks = 0;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
            ResearchPanelMotion Slate(ResearchFlowView view, string id, Vector3 position)
            {
                var go = new GameObject(id, typeof(RectTransform), typeof(Canvas), typeof(ResearchPanelMotion));
                go.transform.SetParent(root.transform);
                ((RectTransform)go.transform).sizeDelta = new Vector2(840, 600); go.transform.localScale = Vector3.one * .001f;
                go.transform.position = position;
                var panel = go.GetComponent<ResearchPanelMotion>(); panel.view = view; panel.panelId = id;
                return panel;
            }
            void SetPrivate(object target, string name, object value)
            {
                var field = target.GetType().GetField(name, Hidden) ?? target.GetType().GetField("<" + name + ">k__BackingField", Hidden);
                field.SetValue(target, value);
            }
            try
            {
                var view = root.AddComponent<ResearchFlowView>();
                var head = new GameObject("Head").transform; head.SetParent(root.transform);
                head.position = new Vector3(60, 51.6f, 60); view.Head = head;
                view.mainPanel = Slate(view, "task_information", head.position + new Vector3(.6f, -.1f, 1.2f));
                view.referencePanel = Slate(view, "reference", head.position + Vector3.back * 3);
                var lateUpdate = typeof(ResearchPanelMotion).GetMethod("LateUpdate", Hidden);
                var reader = view.referencePanel;
                // Reader shown edge-on at a stale far pose, then opened while the Main slate still reports a pending recenter.
                SetPrivate(view.mainPanel, "RecenterPending", true);
                typeof(ResearchPanelMotion).GetMethod("QueueRecenter", Hidden).Invoke(reader, new object[] { false, false, "None" });
                SetPrivate(reader, "PlacementBlocked", true);
                lateUpdate.Invoke(reader, null);
                Check(reader.RecenterPending && !reader.HasAcceptedPose, "within the wait limit the Reader still waits for the Main slate");
                SetPrivate(reader, "pendingSince", Time.unscaledTime - 1f);
                lateUpdate.Invoke(reader, null);
                Check(!reader.RecenterPending, "a Reader stuck waiting is placed after the limit");
                Check(reader.HasAcceptedPose && !reader.PlacementBlocked, "the forced placement is accepted and shown");
                Check(Vector3.Distance(reader.transform.position, head.position) < 2.5f, "the Reader is placed near the wearer, not left at the stale pose");
                return "READER_STUCK_PASS checks=" + checks + "; Editor fixture, not Quest hardware";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
#endif
