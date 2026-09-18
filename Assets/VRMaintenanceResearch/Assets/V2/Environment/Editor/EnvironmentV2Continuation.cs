#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace EnvironmentV2Isolated {
[InitializeOnLoad]
public static class EnvironmentV2Continuation {
    const string Out = "D:/for_model_codex_vr_tmu/outputs/unity_environment_v2_20260914_153441";
    const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_Environment_V2.unity";
    const string Folder = "Assets/VRMaintenanceResearch/Assets/V2/Environment";
    const string Request = "Temp/EnvironmentV2Continuation.txt";
    static EnvironmentV2Continuation() { EditorApplication.update += Poll; }
    static void Write(string file, object data) { File.WriteAllText(Out+"/"+file+".json", JsonConvert.SerializeObject(data, Formatting.Indented)); }
    static float[] V(Vector3 v) { return new[]{v.x,v.y,v.z}; }
    static Transform[] All() { return SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray(); }
    static object[] BrokenReferences(Transform[] transforms) {
        var rows=new List<object>();
        foreach(var component in transforms.SelectMany(t=>t.GetComponents<Component>()).Where(c=>c)) {
            using(var serialized=new SerializedObject(component)) {
                var property=serialized.GetIterator();while(property.Next(true)) {
                    if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceInstanceIDValue!=0 && property.objectReferenceValue==null) rows.Add(new{component=component.GetType().FullName,objectName=component.name,path=property.propertyPath});
                }
            }
        }
        return rows.ToArray();
    }
    static void Guard() {
        if(Application.dataPath!="D:/VR_TMU/XR-Research-V2/Assets" || Application.unityVersion!="6000.3.20f1") throw new Exception("Wrong project/version");
        if(EditorApplication.isPlaying || Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty)) throw new Exception("Playing or unsaved scene; preserve state");
    }
    static void Poll() {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command=File.ReadAllText(Request).Trim(); File.Move(Request, Out+"/request_"+DateTime.UtcNow.Ticks+".txt");
        try {
            if(command=="probe") Probe("live_probe");
            else if(command=="baseline") { Guard(); EditorSceneManager.OpenScene(ScenePath); Probe("baseline"); }
            else if(command=="import") Import();
            else if(command=="capture") Capture("modified");
            else if(command=="baseline_capture") Capture("baseline");
            else if(command=="refresh") AssetDatabase.Refresh();
            else if(command=="colliders") Colliders();
            else if(command=="runtime") EnvironmentV2ContinuationRuntime.Begin();
            else if(command=="start_pose") StartPose();
            else if(command=="walk") EnvironmentV2ContinuationWalk.Begin();
            else if(command=="pivots") Pivots();
            else if(command=="shadows") {var l=All().Select(t=>t.GetComponent<Light>()).Single(x=>x && x.type==LightType.Directional);var before=l.shadows;l.shadows=LightShadows.Soft;Write("shadow_change",new{l.name,before=before.ToString(),after=l.shadows.ToString(),l.intensity,globalPipelineUnchanged=true});Capture("shadow_trial");}
            else if(command=="save_reopen") SaveReopen();
            else throw new Exception("Unknown command "+command);
            File.WriteAllText(Out+"/last_command.txt",command+": PASS");
        } catch(Exception e) { Write("error_"+command,new {error=e.ToString()}); Debug.LogException(e); File.WriteAllText(Out+"/last_command.txt",command+": FAIL"); }
    }
    static GameObject Env() { return SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="Environment_V2"); }
    static void Pivots() {
        Guard();var drawers=All().Where(t=>t.name.Contains("_Drawer_") && t.name.EndsWith("_INTERACTIVE")).ToArray();
        if(drawers.Length!=6)throw new Exception("Expected six authored drawer units");
        var positions=drawers.Select(t=>t.localPosition).ToArray();var rows=new List<object>();string hash=Hash(ScenePath);
        try {foreach(var t in drawers) {
            var contents=t.GetComponentsInChildren<Transform>(true).Where(c=>c!=t).ToArray();var local=contents.Select(c=>c.localPosition).ToArray();var world=contents.Select(c=>c.position).ToArray();var start=t.position;var parentPos=t.parent.position;
            t.localPosition+=new Vector3(0,0,.4f);var delta=t.position-start;
            bool pass=Mathf.Abs(delta.magnitude-.4f)<.001f && t.parent.position==parentPos && contents.Select((c,i)=>(c.position-world[i]-delta).magnitude<.001f && c.localPosition==local[i]).All(x=>x);
            rows.Add(new{t.name,parent=t.parent.name,axis="Blender -Y -> Unity local +Z",travel=delta.magnitude,worldDelta=V(delta),contents=contents.Select(c=>c.name).ToArray(),pass});
            if(!pass)throw new Exception("Drawer transform regression "+t.name);
        }Capture("pivot_drawers");}
        finally {for(int i=0;i<drawers.Length;i++)drawers[i].localPosition=positions[i];Physics.SyncTransforms();}
        Write("pivot_checks",new{status="PASS",drawers=rows,restored=drawers.Select((t,i)=>t.localPosition==positions[i]).All(x=>x),savedFileUnchanged=Hash(ScenePath)==hash,controllerGrabTested=false});
    }
    static void SaveReopen() {
        if(EditorApplication.isPlaying || SceneManager.GetActiveScene().path!=ScenePath) throw new Exception("Wrong state");
        string record=Out+"/saved_scene_hash.txt";
        string expected=File.Exists(record)?File.ReadAllText(record).Trim():(string)JObject.Parse(File.ReadAllText(Out+"/baseline_manifest.json"))["files"].Single(r=>((string)r["path"]).Replace('\\','/').EndsWith(ScenePath))["sha256"];
        if(Hash(ScenePath)!=expected) throw new Exception("Scene disk changed externally; preserve file");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());if(!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))throw new Exception("Save failed");
        File.WriteAllText(record,Hash(ScenePath));Guard();EditorSceneManager.OpenScene(ScenePath);Probe("reopened");
    }
    static void StartPose() {
        if(EditorApplication.isPlaying || SceneManager.GetActiveScene().path!=ScenePath) throw new Exception("Wrong state");
        var xr=All().Select(t=>t.GetComponent<Unity.XR.CoreUtils.XROrigin>()).Single(x=>x);
        var v=JArray.Parse(File.ReadAllText(Out+"/views.json")).Single(x=>(string)x["name"]=="Review_03_Training");
        var pos=JV(v["position"]);var forward=JV(v["forward"]);forward.y=0;
        // Keep the authored viewing direction; stand clear of the retained storage boundary.
        pos+=forward.normalized*.4f;
        xr.transform.SetPositionAndRotation(new Vector3(pos.x,0,pos.z),Quaternion.LookRotation(forward,Vector3.up));xr.CameraYOffset=1.65f;
        xr.CameraFloorOffsetObject.transform.localPosition=new Vector3(0,1.65f,0);
        PrefabUtility.RecordPrefabInstancePropertyModifications(xr);PrefabUtility.RecordPrefabInstancePropertyModifications(xr.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(xr.CameraFloorOffsetObject.transform);
        var cc=xr.GetComponent<CharacterController>();Write("start_pose",new{rig=V(xr.transform.position),camera=V(xr.Camera.transform.position),xr.CameraYOffset,controller=new{cc.height,cc.radius,center=V(cc.center),cc.stepOffset,cc.skinWidth},existingRig=true,hardwareVerified=false});
    }
    static bool Architecture(string n) {
        return n.EndsWith("_Floor") || n.EndsWith("_RearWall") || n.EndsWith("_Facade") || n.EndsWith("_WallShell") || n.EndsWith("_Door_Frame") || n.EndsWith("_Door_Leaf") || n.EndsWith("_Door_Threshold") || n.EndsWith("_Window_Glass") || n.EndsWith("_Door_Glass") || n=="Corridor_Glass" || n.EndsWith("_Door_Masonry_Returns") || n=="V22_Building_Integration" || n=="Factory_Portal_Frames" || n=="Corridor_Window_Base" || n=="Corridor_Window_Frames" || (n.StartsWith("Corridor_") && (n.Contains("Portal") || n.Contains("Landing") || n.Contains("Door_Leaf") || n.Contains("Door_Frame") || n.Contains("Door_Threshold")));
    }
    static void Colliders() {
        if(EditorApplication.isPlaying || SceneManager.GetActiveScene().path!=ScenePath) throw new Exception("Wrong state");
        var furniture=new[]{"Training_Practice_A","Training_Storage","Computer_Main","Computer_Support","Computer_Cabinet_Carcass_STATIC","Fan_Main","Fan_Cabinet_Carcass_STATIC"};
        var retired=JObject.Parse(File.ReadAllText(Out+"/baseline.json"))["colliders"].Select(c=>(string)c["name"]).ToArray();
        if(Env().GetComponentsInChildren<Collider>(true).Any(c=>!Architecture(c.name) && !furniture.Contains(c.name) && !retired.Contains(c.name))) throw new Exception("Unowned collision component found; preserve it before rebuilding owned proxies");
        var removed=new List<string>();
        foreach(var c in Env().GetComponentsInChildren<Collider>(true)) {
            removed.Add(c.name);var go=c.gameObject;Object.DestroyImmediate(c);
            if(!PrefabUtility.GetCorrespondingObjectFromSource(go) && go.transform.childCount==0 && go.GetComponents<Component>().Length==1) Object.DestroyImmediate(go);
        }
        var rows=new List<object>();
        foreach(var mf in Env().GetComponentsInChildren<MeshFilter>(true).Where(m=>Architecture(m.name)||furniture.Contains(m.name))) {
            var mesh=mf.sharedMesh; if(!mesh) throw new Exception("Missing architecture mesh "+mf.name);
            if(furniture.Contains(mf.name)) {
                var c=mf.gameObject.AddComponent<BoxCollider>();c.center=mesh.bounds.center;c.size=mesh.bounds.size;
                rows.Add(new{source=mf.name,type="BoxCollider",center=V(c.center),size=V(c.size),reason="Retain prior seven coarse furniture collision functions at current authored geometry"});continue;
            }
            // Connected facades/frames contain real openings: their AABB closes those openings.
            // Limit exact triangle collision to static architecture, never vehicles/robots/furniture.
            bool opening=mf.name.EndsWith("_Facade") || mf.name.EndsWith("_Door_Frame") || mf.name.EndsWith("_WallShell") || mf.name.EndsWith("_Door_Masonry_Returns") || mf.name.Contains("Portal") || mf.name=="V22_Building_Integration";
            if(opening) {
                var c=mf.gameObject.AddComponent<MeshCollider>();c.sharedMesh=mesh;c.convex=false;
                rows.Add(new{source=mf.name,type="MeshCollider",triangles=mesh.triangles.Length/3,reason="Preserve authored openings in connected static architecture"});
                continue;
            }
            var vertices=mesh.vertices;var indices=mesh.triangles;var parent=Enumerable.Range(0,vertices.Length).ToArray();
            Func<int,int> root=null;root=i=>{while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;};
            Action<int,int> join=(a,b)=>parent[root(a)]=root(b);
            var welded=new Dictionary<Vector3Int,int>();
            for(int i=0;i<vertices.Length;i++) { var v=vertices[i];var key=new Vector3Int(Mathf.RoundToInt(v.x*100000),Mathf.RoundToInt(v.y*100000),Mathf.RoundToInt(v.z*100000)); if(welded.TryGetValue(key,out var first)) join(i,first);else welded.Add(key,i); }
            for(int i=0;i<indices.Length;i+=3){join(indices[i],indices[i+1]);join(indices[i],indices[i+2]);}
            foreach(var component in Enumerable.Range(0,vertices.Length).GroupBy(root)) {
                var ids=component.ToArray();if(ids.Length<3) continue; var b=new Bounds(vertices[ids[0]],Vector3.zero);foreach(var i in ids)b.Encapsulate(vertices[i]);
                if(b.size.x<.001f || b.size.y<.001f || b.size.z<.001f) continue;
                var c=mf.gameObject.AddComponent<BoxCollider>();c.center=b.center;c.size=b.size;
                rows.Add(new{source=mf.name,type="BoxCollider",vertices=ids.Length,center=V(b.center),size=V(b.size),worldCenter=V(mf.transform.TransformPoint(b.center)),moving=mf.name.Contains("Door_Leaf")});
            }
        }
        Physics.SyncTransforms();Write("collision_changes",new{removed,boxes=rows}); Probe("after_colliders");
    }
    static string Hash(string path) { using(var h=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant(); }
    static void Import() {
        Guard(); if(SceneManager.GetActiveScene().path!=ScenePath) throw new Exception("Wrong scene");
        var plan=JArray.Parse(File.ReadAllText(Out+"/import_plan.json"));
        var deps=AssetDatabase.GetDependencies("Assets/VRMaintenanceResearch/Scenes/VRResearch_Master.unity",true);
        foreach(var row in plan) {
            string src=(string)row["source"], dst=(string)row["target"];
            if(deps.Contains(dst) || Hash(src)!=(string)row["after"] || Hash(dst)!=(string)row["before"]) throw new Exception("Import conflict "+dst);
        }
        foreach(var row in plan) {
            string src=(string)row["source"], dst=(string)row["target"];
            File.Copy(src,dst,true); AssetDatabase.ImportAsset(dst,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            if(!AssetDatabase.LoadAssetAtPath<GameObject>(dst)) throw new Exception("Import failed "+dst);
        }
        var changed=new List<object>();
        foreach(var r in Env().GetComponentsInChildren<Renderer>(true)) {
            var source=PrefabUtility.GetCorrespondingObjectFromSource(r);
            if(source) { var before=r.sharedMaterials.Select(m=>m?m.name:"MISSING").ToArray(); r.sharedMaterials=source.sharedMaterials; PrefabUtility.RecordPrefabInstancePropertyModifications(r); changed.Add(new{r.name,before,after=r.sharedMaterials.Select(m=>m?m.name:"MISSING").ToArray()}); }
            // Static package names do not make door/drawer/joint geometry static.
            GameObjectUtility.SetStaticEditorFlags(r.gameObject,0);
        }
        var activity=Env().transform.Find("Factory_Activity").GetComponent<Animator>();
        var controller=AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(Folder+"/Factory_Loop.controller");
        var clip=AssetDatabase.LoadAllAssetsAtPath(Folder+"/06_Factory_Activity.glb").OfType<AnimationClip>().Single();
        var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Factory_Loop").state;
        state.motion=clip; AssetDatabase.SaveAssetIfDirty(state); AssetDatabase.SaveAssetIfDirty(controller);
        activity.runtimeAnimatorController=controller; activity.applyRootMotion=false;activity.cullingMode=AnimatorCullingMode.AlwaysAnimate;PrefabUtility.RecordPrefabInstancePropertyModifications(activity);
        clip.SampleAnimation(activity.gameObject,0);
        Write("import_changes",changed); Probe("after_import");
    }
    static Vector3 JV(JToken t) { return new Vector3((float)t[0],(float)t[1],(float)t[2]); }
    public static void Capture(string phase) {
        var cam=All().Select(t=>t.GetComponent<Camera>()).Single(c=>c && c.name=="Audit_Camera");
        var position=cam.transform.position; var rotation=cam.transform.rotation;float fov=cam.fieldOfView;
        var views=JArray.Parse(File.ReadAllText(Out+"/views.json"));Directory.CreateDirectory(Out+"/"+phase);
        var rows=new List<object>();
        try { foreach(var v in views) {
            string name=(string)v["name"]; if(phase=="baseline" && name!="Review_03_Training") continue;
            if(phase.StartsWith("runtime_") && name!="Focus_Production" && name!="Focus_A" && name!="Focus_B") continue;
            if(phase=="pivot_drawers" && name!="Review_14_Drawers_Open" && name!="Review_05_Fan") continue;
            cam.transform.SetPositionAndRotation(JV(v["position"]),Quaternion.LookRotation(JV(v["forward"]),Vector3.up));cam.fieldOfView=(float)v["fov"];
            var hidden=new List<Renderer>();
            if((bool?)v["cutaway"]==true) foreach(var r in Env().GetComponentsInChildren<Renderer>(true)) if(r.enabled && (r.name.Contains("Ceiling")||r.name.Contains("Roof"))) {r.enabled=false;hidden.Add(r);}
            var prior=RenderTexture.active;var rt=RenderTexture.GetTemporary(1200,800,24,RenderTextureFormat.ARGB32);var tex=new Texture2D(1200,800,TextureFormat.RGB24,false);
            try { RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1200,800),0,0);tex.Apply();File.WriteAllBytes(Out+"/"+phase+"/"+name+".png",tex.EncodeToPNG()); }
            finally {RenderTexture.active=prior;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(tex);foreach(var r in hidden)r.enabled=true;}
            rows.Add(new{name,position=V(cam.transform.position),forward=V(cam.transform.forward),cam.fieldOfView,width=1200,height=800,cutaway=hidden.Select(r=>r.name).ToArray(),pipeline=AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline)});
        }} finally {cam.transform.SetPositionAndRotation(position,rotation);cam.fieldOfView=fov;}
        Write(phase+"_views",rows);
    }
    static void Console(string name) {
        const string key="EnvironmentV2Isolated.OutputAudit"; string old=SessionState.GetString(key,"");
        SessionState.SetString(key,Out);
        try { typeof(EnvironmentV2Import).GetMethod("SaveProbe",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{name+"_console.json"}); }
        finally { if(old=="")SessionState.EraseString(key);else SessionState.SetString(key,old); }
    }
    static void Probe(string name) {
        var ts=All(); var rs=ts.Select(t=>t.GetComponent<Renderer>()).Where(r=>r).ToArray();
        Write(name,new {
            project=Application.dataPath, unity=Application.unityVersion, playing=EditorApplication.isPlaying,
            scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty,s.isLoaded};}).ToArray(),
            pipeline=GraphicsSettings.currentRenderPipeline ? AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline):"Builtin",
            graphics=SystemInfo.graphicsDeviceType.ToString(), target=EditorUserBuildSettings.activeBuildTarget.ToString(),
            roots=SceneManager.GetActiveScene().GetRootGameObjects().Select(g=>new{g.name,position=V(g.transform.position),rotation=V(g.transform.eulerAngles),scale=V(g.transform.localScale)}).ToArray(),
            cameras=ts.Select(t=>t.GetComponent<Camera>()).Where(c=>c).Select(c=>new{c.name,c.enabled,position=V(c.transform.position),rotation=V(c.transform.eulerAngles),c.fieldOfView}).ToArray(),
            objects=ts.Select(t=>new{t.name,parent=t.parent?t.parent.name:null,local=V(t.localPosition),position=V(t.position),rotation=V(t.localEulerAngles),scale=V(t.localScale),components=t.GetComponents<Component>().Select(c=>c?c.GetType().FullName:"MISSING").ToArray(),prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)}).ToArray(),
            renderers=rs.Select(r=>new{r.name,center=V(r.bounds.center),size=V(r.bounds.size),shadow=r.shadowCastingMode.ToString(),mesh=r.GetComponent<MeshFilter>()?r.GetComponent<MeshFilter>().sharedMesh?.name:null,materials=r.sharedMaterials.Select(m=>m?new{name=m.name,shader=m.shader.name,path=AssetDatabase.GetAssetPath(m),queue=m.renderQueue,textures=m.GetTexturePropertyNames().Where(p=>m.GetTexture(p)).Select(p=>new{property=p,name=m.GetTexture(p).name}).ToArray()}:null).ToArray()}).ToArray(),
            colliders=ts.SelectMany(t=>t.GetComponents<Collider>()).Select(c=>new{c.name,type=c.GetType().Name,center=V(c.bounds.center),size=V(c.bounds.size),c.enabled,c.isTrigger}).ToArray(),
            animators=ts.Select(t=>t.GetComponent<Animator>()).Where(a=>a).Select(a=>new{a.name,controller=AssetDatabase.GetAssetPath(a.runtimeAnimatorController),clips=a.runtimeAnimatorController?a.runtimeAnimatorController.animationClips.Select(c=>new{c.name,c.length,c.isLooping,path=AssetDatabase.GetAssetPath(c)}).ToArray():null}).ToArray(),
            missingScripts=ts.Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)),missingMaterials=rs.Sum(r=>r.sharedMaterials.Count(m=>!m)),
            environmentMeshes=Env().GetComponentsInChildren<MeshFilter>(true).Length,
            environmentTriangles=Env().GetComponentsInChildren<MeshFilter>(true).Where(m=>m.sharedMesh).Sum(m=>(long)m.sharedMesh.triangles.Length/3),
            missingMeshes=Env().GetComponentsInChildren<MeshRenderer>(true).Count(r=>!r.GetComponent<MeshFilter>() || !r.GetComponent<MeshFilter>().sharedMesh),
            unsupportedShaders=rs.SelectMany(r=>r.sharedMaterials).Where(m=>m && (!m.shader || !m.shader.isSupported)).Select(m=>m.name).Distinct().ToArray(),
            brokenObjectReferences=BrokenReferences(ts),
            lights=ts.Select(t=>t.GetComponent<Light>()).Where(l=>l).Select(l=>new{l.name,l.intensity,shadows=l.shadows.ToString(),type=l.type.ToString()}).ToArray(),
            masterDependencies=AssetDatabase.GetDependencies("Assets/VRMaintenanceResearch/Scenes/VRResearch_Master.unity",true)
        }); Console(name);
    }
}
}
#endif
