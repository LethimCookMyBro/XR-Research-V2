#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Owner 29 Oct: the Training "Examine" chip, grown to show information, sank into the bench. Uses the real scene targets.
    public static class ResearchExamineBenchChecks
    {
        public static string Run()
        {
            const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            var targets = UnityEngine.Object.FindObjectsByType<ResearchExamineTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.stableTarget == "training.module" || t.stableTarget == "A.module" || t.stableTarget == "B.module").ToArray();
            if (targets.Length != 3) throw new InvalidOperationException("Expected the three real Examine targets, found " + targets.Length);
            var rig = new GameObject("ExamineBenchCheckCamera");
            int checks = 0; string report = "";
            try
            {
                var camera = rig.AddComponent<Camera>(); camera.fieldOfView = 90; camera.aspect = 1;
                foreach (var target in targets)
                {
                    var tryBounds = typeof(ResearchExamineTarget).GetMethod("TryFocusBounds", Hidden);
                    var args = new object[] { null };
                    if (!(bool)tryBounds.Invoke(target, args)) throw new InvalidOperationException(target.stableTarget + " has no focus bounds");
                    var bounds = (Bounds)args[0];
                    var rect = (RectTransform)target.stationCanvas.transform;
                    var savedSize = rect.sizeDelta; var savedPose = new Pose(rect.position, rect.rotation);
                    try
                    {
                        // Information-sized chip (RefreshLabel), standing eye 1.6 m, 0.7 m from the part, looking at it.
                        rect.sizeDelta = new Vector2(340, 220);
                        var look = bounds.center; var eye = new Vector3(look.x, 1.6f, look.z) - Vector3.ProjectOnPlane(target.transform.forward, Vector3.up).normalized * .7f;
                        if (Vector3.Distance(eye, look) < .3f) eye = look + new Vector3(0, 1.6f - look.y, -.7f);
                        rig.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                        bool placed = (bool)typeof(ResearchExamineTarget).GetMethod("PlaceBesidePanels", Hidden).Invoke(target, new object[] { camera, bounds });
                        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                        float bottom = corners.Min(c => c.y);
                        bool clear = !placed || bottom >= bounds.min.y - .001f;
                        report += $" {target.stableTarget}: placed={placed} chipBottom={bottom:0.000} partBottom={bounds.min.y:0.000};";
                        if (!clear) throw new InvalidOperationException(target.stableTarget + " Examine chip bottom " + bottom + " is below the part bottom " + bounds.min.y);
                        checks++;
                    }
                    finally { rect.sizeDelta = savedSize; rect.SetPositionAndRotation(savedPose.position, savedPose.rotation); }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
            return "EXAMINE_BENCH_PASS checks=" + checks + ";" + report;
        }
    }
}
#endif
