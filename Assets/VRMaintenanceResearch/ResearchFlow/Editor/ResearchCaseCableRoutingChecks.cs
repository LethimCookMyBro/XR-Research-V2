#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchCaseCableRoutingChecks
    {
        public static string RunRepeat(string evidencePath)
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || File.Exists(evidencePath))
                throw new InvalidOperationException("Clean stopped scene and fresh evidence path required.");
            try
            {
                var wiring=UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAWiring>(FindObjectsInactive.Include);
                if(!wiring)throw new InvalidOperationException("Authored Task A required.");
                ResearchS1TaskABuilder.ConfigureCaseCableRoutes(wiring);
                var spans=wiring.cables.SelectMany(c=>c.spans).ToArray();
                var first=spans.Select(s=>s.cachedPath==null?null:(Vector3[])s.cachedPath.Clone()).ToArray();
                ResearchS1TaskABuilder.ConfigureCaseCableRoutes(wiring);
                var mismatches=spans.Select((span,index)=>new{span,index}).Where(x=>
                    (first[x.index]==null)!=(x.span.cachedPath==null) ||
                    (first[x.index]!=null && (first[x.index].Length!=x.span.cachedPath.Length ||
                    first[x.index].Where((p,j)=>Vector3.Distance(p,x.span.cachedPath[j])>1e-6f).Any())))
                    .Select(x=>x.span.from.name).ToArray();
                string result="CASE_ROUTING_REPEAT_"+(mismatches.Length==0?"PASS":"FAIL")+" spans="+spans.Length+" mismatches="+string.Join(",",mismatches);
                File.WriteAllText(evidencePath,result);
                return result;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene.path);
            }
        }

        public static string Run(string directory)
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || Directory.Exists(directory))
                throw new InvalidOperationException("Clean stopped scene and fresh evidence directory required.");
            var task=UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            if(!task)throw new InvalidOperationException("Authored Task A required.");
            Directory.CreateDirectory(directory);
            var checks=new List<object>();
            var failures=new List<string>();
            void Check(bool pass,string name,object observed)
            {
                checks.Add(new{name,pass,observed});
                if(!pass)failures.Add(name);
            }
            var protectedEnds=task.wiring.contacts.Select(c=>c.mate).Concat(task.wiring.ends.Select(e=>e.plug.transform)).Distinct().ToArray();
            var poses=protectedEnds.ToDictionary(t=>t,t=>t.localToWorldMatrix);
            var spans=new List<object>();
            int guided=0,hits=0;
            foreach(var cable in task.wiring.cables)
            {
                cable.RefreshShape();
                foreach(var span in cable.spans)
                {
                    if(span.cachedPath==null)continue;
                    var points=span.cachedPath.Select(cable.transform.InverseTransformPoint).ToArray();
                    int slotHits=points.Count(p=>p.z<.075f &&
                        ((p.x>.165f&&p.x<.215f&&p.y>.255f&&p.y<.405f) ||
                         (p.x>.03f&&p.x<.20f&&p.y>.19f&&p.y<.275f)));
                    if(cable.assetId!="CABLE_AC_C13_JP")hits+=slotHits;
                    double length=ResearchS1CablePath.Length(span.cachedPath);
                    double radius=double.PositiveInfinity;
                    for(int i=1;i<span.cachedPath.Length-1;i++)
                    {
                        var u=span.cachedPath[i]-span.cachedPath[i-1];
                        var v=span.cachedPath[i+1]-span.cachedPath[i];
                        double cross=Vector3.Cross(u,v).magnitude;
                        if(cross>1e-12)radius=Math.Min(radius,u.magnitude*v.magnitude*(u+v).magnitude/(2*cross));
                    }
                    if(span.cachedGuided)
                    {
                        guided++;
                        string id=cable.assetId+"/"+span.from.name;
                        Check(Math.Abs(length-span.length)<.0002,id+"/authored length",length);
                        Check(radius>=.0079,id+"/8 mm bend radius",radius);
                        Check(Vector3.Distance(span.cachedPath[0],span.from.TransformPoint(span.fromExit))<.0001f &&
                            Vector3.Distance(span.cachedPath[span.cachedPath.Length-1],span.to.TransformPoint(span.toExit))<.0001f,id+"/exact endpoints",true);
                        Check(points.All(p=>p.x>=-.013f&&p.x<=.432f&&p.y>=-.002f&&p.y<=.47f&&p.z>=-.105f&&p.z<=.105f),id+"/within case envelope",points.Length);
                    }
                    spans.Add(new{cable=cable.assetId,from=span.from.name,span.cachedGuided,length,expected=span.length,radius,slotHits});
                }
            }
            Check(guided==4,"four fixed supply/fan spans use guides",guided);
            Check(hits==0,"cable centerlines clear RAM and GPU work rectangles at rest",hits);
            Check(task.wiring.AuthoredBindingsValid(),"authored wiring bindings remain valid",true);
            Check(task.wiring.contacts.Count(c=>c.Connected)==16,"sixteen fixed contacts remain connected",task.wiring.contacts.Count(c=>c.Connected));
            Check(task.wiring.ends.Length==3&&task.wiring.cables.Length==8,"three interactive ends and eight cables remain",task.wiring.ends.Select(e=>e.plug.stableId).ToArray());
            Check(poses.All(p=>Vector3.Distance(p.Key.position,p.Value.GetColumn(3))<.000001f&&Quaternion.Angle(p.Key.rotation,p.Value.rotation)<.001f),"routing does not move plugs or fixed contacts",protectedEnds.Length);
            var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToList();
            var helper=typeof(ResearchS1ReaderMediaBuilder);
            var flags=BindingFlags.Static|BindingFlags.NonPublic;
            var bounds=(Bounds)helper.GetMethod("Frame",flags).Invoke(null,new object[]{all,new[]{"MB_B760_DDR4"}});
            var cameraGo=new GameObject("CaseCableCheckCamera"){hideFlags=HideFlags.HideAndDontSave};
            var matrixFlags=task.wiring.cables.ToDictionary(c=>c.skin,c=>c.skin.forceMatrixRecalculationPerRender);
            try
            {
                foreach(var skin in matrixFlags.Keys)skin.forceMatrixRecalculationPerRender=true;
                var camera=cameraGo.AddComponent<Camera>();camera.fieldOfView=40;camera.aspect=1280f/720;
                camera.nearClipPlane=.01f;camera.farClipPlane=50;camera.cullingMask=~(1<<LayerMask.NameToLayer("UI"));camera.clearFlags=CameraClearFlags.Skybox;
                var light=cameraGo.AddComponent<Light>();light.type=LightType.Spot;light.spotAngle=70;light.range=4;light.intensity=1;light.shadows=LightShadows.None;
                float distance=Mathf.Clamp(Mathf.Max(bounds.extents.magnitude,.04f)/Mathf.Sin(20*Mathf.Deg2Rad)*1.05f,.35f,2.4f);
                camera.transform.position=bounds.center+new Vector3(0,.15f,1).normalized*distance;camera.transform.LookAt(bounds.center);
                File.WriteAllBytes(Path.Combine(directory,"case.png"),(byte[])helper.GetMethod("Render",flags).Invoke(null,new object[]{camera,null}));
            }
            finally
            {
                foreach(var pair in matrixFlags)pair.Key.forceMatrixRecalculationPerRender=pair.Value;
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
            Check(!scene.isDirty,"scene stays clean",scene.isDirty);
            string status=failures.Count==0?"PASS":"FAIL";
            File.WriteAllText(Path.Combine(directory,"case-routing.json"),JsonConvert.SerializeObject(new{status,scope="Stopped Editor actual cable shape and same-camera capture; not Quest input or wearer proof",guided,hits,checks,failures,spans},Formatting.Indented));
            return "CASE_ROUTING_"+status+" checks="+checks.Count+" guided="+guided+" slot_hits="+hits;
        }
    }
}
#endif
