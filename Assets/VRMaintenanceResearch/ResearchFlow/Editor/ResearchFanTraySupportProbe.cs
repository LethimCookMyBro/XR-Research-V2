#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchFanTraySupportProbe
    {
        public static string Run(string freshDirectory)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.path.EndsWith("VRResearch_ResearchFlow_V2.unity") || !scene.isDirty)
                throw new InvalidOperationException("This bounded probe requires the current dirty canonical scene, stopped Editor; no scene save/reload.");
            string dir = Path.GetFullPath(freshDirectory);
            if (Directory.Exists(dir) || File.Exists(dir)) throw new IOException("Fresh external evidence directory required.");
            var session = scene.GetRootGameObjects().Single(g => g.name == "ResearchFlow_Session");
            var fan = session.transform.Find("ResearchFlow_TaskB_Port/ResearchS1TaskB");
            if (!fan) throw new InvalidOperationException("Current authored Task B missing.");
            var task = fan.GetComponent<ResearchS1TaskBMechanics>();
            var position = fan.localPosition; var rotation = fan.localRotation; var scale = fan.localScale;
            var children = fan.GetComponentsInChildren<Transform>(true).Where(t => t != fan).ToArray();
            var positions = children.Select(t => t.localPosition).ToArray();
            var rotations = children.Select(t => t.localRotation).ToArray();
            var scales = children.Select(t => t.localScale).ToArray();
            string Poses() => string.Join("|", children.Select(t => t.GetInstanceID() + ":" + t.localPosition.ToString("R") + ":" + t.localRotation.ToString("R") + ":" + t.localScale.ToString("R")));
            string Thin() { try { ResearchThinGeometryChecks.Run(); return "PASS"; } catch(Exception e) { return e.ToString(); } }
            var before = Poses(); string authoredBefore = ResearchS1TaskBBuilder.CheckAuthored(task);
            string baseline = Thin(), modified = null, authoredModified = null, modifiedPoses = null, lift = null, idempotent = null, error = null;
            var worldBefore = fan.position; var worldModified = worldBefore;
            try
            {
                var method = typeof(ResearchS1SceneIntegration).GetMethod("SeatFanOnInspectionTray", BindingFlags.Static | BindingFlags.NonPublic);
                if (method == null) throw new MissingMethodException("Candidate authoring helper not installed; do not invoke IntegrateIntoCanonical.");
                lift = (string)method.Invoke(null, new object[] {fan, scene});
                worldModified = fan.position;
                modified = Thin(); authoredModified = ResearchS1TaskBBuilder.CheckAuthored(task); modifiedPoses = Poses();
                idempotent = (string)method.Invoke(null, new object[] {fan, scene});
                if (fan.position != worldModified) throw new InvalidOperationException("Second support placement was not idempotent.");
            }
            catch(Exception e) { error = (e is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : e).ToString(); }
            finally
            {
                fan.localPosition = position; fan.localRotation = rotation; fan.localScale = scale;
                for (int i = 0; i < children.Length; i++)
                {
                    children[i].localPosition = positions[i]; children[i].localRotation = rotations[i]; children[i].localScale = scales[i];
                }
                Physics.SyncTransforms();
            }
            string rollback = Thin(), authoredRollback = ResearchS1TaskBBuilder.CheckAuthored(task);
            bool preserved = before == Poses() && fan.localPosition.Equals(position) && fan.localRotation.Equals(rotation) && fan.localScale.Equals(scale) && SceneManager.GetActiveScene() == scene && scene.isDirty;
            bool pass = error == null && baseline.Contains("VIS_FAN_PED_BASE_LOD0 x Fan_InspectionTray") && modified == "PASS" && rollback == baseline && authoredBefore == authoredModified && authoredBefore == authoredRollback && before == modifiedPoses && preserved;
            float[] V(Vector3 v) => new[] {v.x,v.y,v.z};
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir,"fan-tray-aba.json");
            File.WriteAllText(path,JsonConvert.SerializeObject(new {scope="Synchronous stopped Editor live A/B/A; finally restores dirty scene root/children; no save/reload, runtime input or wearer claim",baseline,modified,rollback,authoredBefore,authoredModified,authoredRollback,lift,idempotent,worldBefore=V(worldBefore),worldModified=V(worldModified),worldRollback=V(fan.position),childLocalPosesUnchanged=before==modifiedPoses,preserved,error,pass},Formatting.Indented));
            return path;
        }
    }
}
#endif
