#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.XR.CoreUtils;
using Object = UnityEngine.Object;

namespace EnvironmentV2Isolated
{
    public static partial class EnvironmentV2Import
    {
        const string Folder="Assets/VRMaintenanceResearch/Assets/V2/Environment";
        const string ScenePath="Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_Environment_V2.unity";
        static readonly string[] Groups={"Training","Computer","Fan","Corridor","Factory_Static","Factory_Activity","Vehicles_Background"};
        static JObject Source => JObject.Parse(File.ReadAllText(Audit+"/source_reference.json"));
        static string Hash(string path) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant(); }
        static void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException(why); }
        static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
        static Vector3 Vec(JToken v)=>new Vector3((float)v[0],(float)v[1],(float)v[2]);
        static void Write(string name,object value)=>File.WriteAllText(OutputAudit+"/"+name,JsonConvert.SerializeObject(value,Formatting.Indented));
        static void Protected()
        {
            foreach(var p in (JObject)Source["protected_hashes"])Check(Hash(p.Key)==(string)p.Value,"Protected scene hash changed: "+p.Key);
        }
        static Scene TargetScene()
        {
            var s=SceneManager.GetActiveScene();Check(s.path==ScenePath,"V2 scene must be active: "+s.path);return s;
        }
        static Dictionary<string,Transform> Named(GameObject root)=>root.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        static GameObject Environment()=>TargetScene().GetRootGameObjects().Single(g=>g.name=="Environment_V1");
        static Animator FactoryAnimator()=>Environment().transform.Find("Factory_Activity").GetComponentInChildren<Animator>(true);
        static AnimationClip FactoryClip()=>AssetDatabase.LoadAllAssetsAtPath(Folder+"/06_Factory_Activity.glb").OfType<AnimationClip>().Single();
        static void Build()
        {
            Check(SaveProbe("before_import_console.json").compileErrorEntries==0,"Compile gate failed");
            Check(Application.unityVersion=="6000.3.20f1","Version mismatch");Protected();
            Check(!File.Exists(ScenePath),"Existing V2 scene preserved; use audit instead of rebuilding");
            var source=Source;
            foreach(var file in source["source_files"])
            {
                string dest=Folder+"/"+(string)file["name"];
                if(File.Exists(dest))Check(Hash(dest)==(string)file["sha256"],"Different existing V2 asset: "+dest);
                else File.Copy((string)file["source"],dest);
                Check(Hash(dest)==(string)file["sha256"],"Copy hash mismatch");
                AssetDatabase.ImportAsset(dest,ImportAssetOptions.ForceSynchronousImport);
            }
            var previous=SceneManager.GetActiveScene();
            if(previous.path=="")Check(EditorSceneManager.SaveScene(previous,Audit+"/PreImport_EditorScene.unity"),"Untitled checkpoint save failed");
            else Check(!previous.isDirty,"Existing scene has unsaved edits; preserve it before V2 assembly");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);SceneManager.SetActiveScene(scene);
            Check(EditorSceneManager.SaveScene(scene,Audit+"/Environment_V2_BASELINE.unity"),"Baseline save failed");
            var root=new GameObject("Environment_V1");var imported=new List<object>();
            int i=0;
            foreach(var file in source["source_files"])
            {
                string path=Folder+"/"+(string)file["name"];
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);Check(asset!=null,"GLB main GameObject missing: "+path);
                var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);go.name=Groups[i++];go.transform.SetParent(root.transform,false);
                Check(go.transform.localPosition.sqrMagnitude<1e-8f&&(go.transform.localScale-Vector3.one).sqrMagnitude<1e-8f&&Quaternion.Angle(go.transform.localRotation,Quaternion.identity)<.001f,"Non-identity imported wrapper");
                imported.Add(new{path,group=go.name,source_sha256=Hash(path),bytes=new FileInfo(path).Length});
            }
            var activity=root.transform.Find("Factory_Activity");var animator=activity.GetComponentInChildren<Animator>(true);Check(animator!=null,"No imported Animator");
            var clip=FactoryClip();Check(Mathf.Abs(clip.length-48)<.02f,"Unexpected activity duration");
            var settings=AnimationUtility.GetAnimationClipSettings(clip);Check(settings.loopTime,"glTFast importer did not enable loopTime");
            string controllerPath=Folder+"/Factory_Loop.controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)??AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine=controller.layers[0].stateMachine;
            var state=machine.states.FirstOrDefault(x=>x.state.name=="Factory_Loop").state??machine.AddState("Factory_Loop");state.motion=clip;machine.defaultState=state;
            animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var named=Named(root);var collision=new List<string>();
            foreach(string name in source["collider_candidates"].Values<string>())
            {
                if(!named.TryGetValue(name,out var t))continue;var mf=t.GetComponent<MeshFilter>();if(mf==null||mf.sharedMesh==null)continue;
                if(name=="Factory_Slab") {var mc=t.gameObject.AddComponent<MeshCollider>();mc.sharedMesh=mf.sharedMesh;}
                else {var bc=t.gameObject.AddComponent<BoxCollider>();bc.center=mf.sharedMesh.bounds.center;bc.size=mf.sharedMesh.bounds.size;}
                collision.Add(name);
            }
            var rigAsset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/3.4.0/Starter Assets/Prefabs/XR Origin (XR Rig).prefab");Check(rigAsset!=null,"Existing XRI base rig missing");
            var rig=(GameObject)PrefabUtility.InstantiatePrefab(rigAsset,scene);rig.name="XR_Origin_Environment_Test";
            var xr=rig.GetComponent<XROrigin>();Check(xr!=null&&xr.Camera!=null,"XROrigin/camera missing");xr.CameraYOffset=1.65f;
            xr.transform.position=new Vector3(5.5f,.03f,3f);
            var view=source["views"].First(x=>((string)x["name"]).StartsWith("05"));xr.MatchOriginUpCameraForward(Vector3.up,Vec(view["forward"]));xr.MoveCameraToWorldLocation(Vec(view["position"]));
            var manager=new GameObject("XR_Interaction_Manager_Test");manager.AddComponent<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
            var sun=new GameObject("Baseline_Directional_Light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1f;sun.shadows=LightShadows.None;sun.transform.rotation=Quaternion.Euler(50,-30,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.6f,.6f,.6f);RenderSettings.skybox=null;
            var cam=new GameObject("Audit_Camera").AddComponent<Camera>();cam.enabled=false;cam.nearClipPlane=.05f;cam.farClipPlane=200;cam.fieldOfView=60;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.25f,.28f,.32f);cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            clip.SampleAnimation(animator.gameObject,0);
            Check(EditorSceneManager.SaveScene(scene,ScenePath),"V2 scene save failed");AssetDatabase.SaveAssetIfDirty(controller);
            Write("assembly.json",new{scene=ScenePath,imported,collision_objects=collision,basic_colliders=collision.Count,lighting="one nonshadow directional + flat ambient; no global pipeline edits",xr_prefab=AssetDatabase.GetAssetPath(rigAsset)});
            for(int si=SceneManager.sceneCount-1;si>=0;si--){var other=SceneManager.GetSceneAt(si);if(other!=scene&&other.path==""&&!other.isDirty&&other.GetRootGameObjects().All(g=>g.name=="Main Camera"||g.name=="Directional Light"))EditorSceneManager.CloseScene(other,true);}
            AuditScene();Protected();Debug.Log("ENVIRONMENT_V2_BUILD_PASS packages=7");
        }
        static void AuditScene()
        {
            TargetScene();var root=Environment();var named=Named(root);var source=Source;var errors=new List<string>();float maxPosition=0,maxBounds=0;
            foreach(var package in (JObject)source["references"])
            {
                foreach(var obj in (JObject)package.Value["objects"])
                {
                    if(!named.TryGetValue(obj.Key,out var t)){errors.Add("Missing "+obj.Key);continue;}
                    var m=package.Value["matrices"][obj.Key];var expected=new Vector3(-(float)m[0][3],(float)m[2][3],-(float)m[1][3]);
                    float err=Vector3.Distance(t.position,expected);maxPosition=Mathf.Max(maxPosition,err);if(err>.002f)errors.Add("Position "+obj.Key+" "+err);
                    if(((string)package.Key)!="06_Factory_Activity"&&obj.Value["bounds"]!=null&&t.TryGetComponent<Renderer>(out var ren))
                    {
                        var lo=Vec(obj.Value["bounds"][0]);var hi=Vec(obj.Value["bounds"][1]);var min=new Vector3(-hi.x,lo.z,-hi.y);var max=new Vector3(-lo.x,hi.z,-lo.y);
                        float be=Mathf.Max(Vector3.Distance(ren.bounds.min,min),Vector3.Distance(ren.bounds.max,max));maxBounds=Mathf.Max(maxBounds,be);if(be>.004f)errors.Add("Bounds "+obj.Key+" "+be);
                    }
                }
            }
            var dimensions=new List<object>();
            foreach(string room in new[]{"Training","Computer","Fan"})
            {
                var floor=named[room+"_Floor"].GetComponent<Renderer>().bounds;var door=named[room+"_Door"].GetComponent<Renderer>().bounds;
                var tables=named.Values.Where(t=>t.name.StartsWith(room+"_")&&t.name.EndsWith("_Top")).ToArray();Check(tables.Length==2,"Two tables required "+room);
                dimensions.Add(new{room,floor_center=V(floor.center),floor_size=V(floor.size),door_height=door.size.y,ceiling_height=named[room+"_Ceiling"].GetComponent<Renderer>().bounds.min.y-floor.max.y,tables=tables.Select(t=>new{name=t.name,height_above_floor=t.GetComponent<Renderer>().bounds.max.y-floor.max.y,size=V(t.GetComponent<Renderer>().bounds.size)}).ToArray()});
            }
            var toggles=new List<object>();
            foreach(string group in new[]{"Factory_Activity","Vehicles_Background"})
            {
                var go=root.transform.Find(group).gameObject;go.SetActive(false);
                bool rooms=Groups.Take(3).All(n=>root.transform.Find(n).GetComponentsInChildren<Renderer>().Length>0);toggles.Add(new{group,rooms_remain_active=rooms,disabled_renderers=go.GetComponentsInChildren<Renderer>(true).Count(r=>!r.gameObject.activeInHierarchy)});Check(rooms,"Toggle harmed rooms");go.SetActive(true);
            }
            var renderers=root.GetComponentsInChildren<Renderer>(true);var mats=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
            var materials=mats.Select(m=>new{name=m.name,path=AssetDatabase.GetAssetPath(m),shader=m.shader?m.shader.name:null,supported=m.shader&&m.shader.isSupported,render_queue=m.renderQueue,transparent=m.renderQueue>=3000,texture_inputs=m.GetTexturePropertyNames().Where(n=>m.GetTexture(n)!=null).Select(n=>new{slot=n,texture=AssetDatabase.GetAssetPath(m.GetTexture(n))}).ToArray(),visual_status="PENDING_RENDER_REVIEW"}).ToArray();
            var missing=renderers.Where(r=>r.sharedMaterials.Any(m=>m==null||m.shader==null||m.shader.name=="Hidden/InternalErrorShader")).Select(r=>r.name).ToArray();
            var meshFilters=root.GetComponentsInChildren<MeshFilter>(true);long triangles=meshFilters.Sum(m=>(long)(m.sharedMesh?m.sharedMesh.triangles.Length/3:0));
            var animator=FactoryAnimator();var clip=FactoryClip();
            var targets=AnimationUtility.GetCurveBindings(clip).Select(b=>b.path).Distinct().ToArray();
            Check(named.Keys.Count(n=>n=="R03_A_Robot_Root"||n=="R03_B_Robot_Root")==2,"Robot root count");Check(named.Keys.Count(n=>n.StartsWith("R03_A_Part_")||n.StartsWith("R03_B_Part_"))==8,"Part count");
            var tracked=animator.GetComponentsInChildren<Transform>(true);clip.SampleAnimation(animator.gameObject,0);var first=tracked.Select(t=>t.localToWorldMatrix).ToArray();clip.SampleAnimation(animator.gameObject,clip.length);float seam=0;
            for(int i=0;i<tracked.Length;i++)for(int r=0;r<4;r++)for(int c=0;c<4;c++)seam=Mathf.Max(seam,Mathf.Abs(tracked[i].localToWorldMatrix[r,c]-first[i][r,c]));clip.SampleAnimation(animator.gameObject,0);
            var report=new{status=errors.Count==0&&missing.Length==0&&seam<.001f?"ASSEMBLY_AUDIT_PASS":"ASSEMBLY_AUDIT_FAILED",project_root=Root,unity_version=Application.unityVersion,graphics_api=SystemInfo.graphicsDeviceType.ToString(),branch=(string)source["branch"],scene_path=ScenePath,source_files=source["source_files"],scale_orientation=new{mapping="Blender(x,y,z) -> Unity(-x,z,-y); glTFast handedness X inversion; identity wrappers",max_position_error_m=maxPosition,max_static_bounds_error_m=maxBounds,errors,rooms=dimensions},toggle_result=toggles,materials,missing_material_objects=missing,animation=new{clip=clip.name,duration_seconds=clip.length,loopTime=AnimationUtility.GetAnimationClipSettings(clip).loopTime,robot_roots=2,persistent_workpieces=8,animated_transform_paths=targets.Length,preview_endpoint_matrix_error=seam,playmode="PENDING"},statistics=new{renderer_count=renderers.Length,material_asset_count=mats.Length,unique_material_names=mats.Select(m=>m.name).Distinct().Count(),mesh_instances=meshFilters.Length,unique_mesh_assets=meshFilters.Select(m=>m.sharedMesh).Distinct().Count(),triangles,realtime_lights=TargetScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Light>(true)).Count(l=>l.lightmapBakeType!=LightmapBakeType.Baked),transparent_material_assets=mats.Count(m=>m.renderQueue>=3000)},xr_scale_sanity="Existing XRI rig placed; dimensions measured. Render and PlayMode review pending. No headset test.",remaining=new[]{"Actual visual render review","PlayMode animation/scale test","URP transmission approximation with existing Opaque Texture disabled","No hardware performance validation"}};
            Write("environment_v1_unity_import_report.json",report);Write("after_import_console.json",SaveProbe("after_import_console_raw.json"));
            Check(errors.Count==0,string.Join("; ",errors.Take(12)));Check(missing.Length==0,"Missing material assignments");Check(seam<.001f,"Clip endpoint mismatch");Protected();
            EditorSceneManager.SaveScene(TargetScene());Debug.Log("ENVIRONMENT_V2_AUDIT_PASS renderers="+renderers.Length+" triangles="+triangles);
        }
        static void CaptureViews()
        {
            TargetScene();var env=Environment();var cam=TargetScene().GetRootGameObjects().Single(g=>g.name=="Audit_Camera").GetComponent<Camera>();var source=Source;var rows=new List<object>();
            var anim=FactoryAnimator();FactoryClip().SampleAnimation(anim.gameObject,3.75f);
            foreach(var v in source["views"])
            {
                string name=(string)v["name"];cam.transform.position=Vec(v["position"]);
                string floorName=name.StartsWith("05")?"Training_Floor":name.StartsWith("06")?"Computer_Floor":name.StartsWith("08")?"Fan_Floor":name.StartsWith("03")?"Neutral_Corridor_Floor":null;
                if(floorName!=null){var cp=cam.transform.position;cp.y=Named(env)[floorName].GetComponent<Renderer>().bounds.max.y+1.65f;cam.transform.position=cp;}
                cam.transform.rotation=Quaternion.LookRotation(Vec(v["forward"]),Vector3.up);cam.fieldOfView=name.StartsWith("22")?40:60;
                var hidden=new List<Renderer>();
                if((bool)v["overallCutaway"])
                    foreach(var r in env.GetComponentsInChildren<Renderer>(true))
                        if(r.enabled&&(r.bounds.min.y>3.5f||r.name.Contains("Ceiling")||r.name.StartsWith("Factory_Roof")||r.name.StartsWith("Factory_SideWall")||r.name.StartsWith("Factory_EndWall"))){r.enabled=false;hidden.Add(r);}
                var rt=new RenderTexture(1080,720,24,RenderTextureFormat.ARGB32);rt.Create();var old=RenderTexture.active;var tex=new Texture2D(1080,720,TextureFormat.RGB24,false);
                try
                {
                    RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1080,720),0,0);tex.Apply();
                    string path=Audit+"/"+name+"_Unity.png";File.WriteAllBytes(path,tex.EncodeToPNG());rows.Add(new{path,position=V(cam.transform.position),temporary_cutaway=hidden.Count,graphics_api=SystemInfo.graphicsDeviceType.ToString()});
                }
                finally {RenderTexture.active=old;Object.DestroyImmediate(tex);rt.Release();Object.DestroyImmediate(rt);foreach(var r in hidden)r.enabled=true;}
            }
            FactoryClip().SampleAnimation(anim.gameObject,0);Write("screenshots.json",rows);EditorSceneManager.SaveScene(TargetScene());Protected();Debug.Log("ENVIRONMENT_V2_RENDER_PASS views="+rows.Count);
        }
    }
}
#endif
