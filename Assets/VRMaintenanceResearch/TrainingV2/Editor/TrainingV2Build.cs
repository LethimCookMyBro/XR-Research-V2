#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using VRMaintenanceResearch.TrainingV2;
using VRMaintenanceResearch.TrainingV2.Editor;

namespace VRTrainingV2.Editor
{
    public static partial class TrainingV2Editor
    {
        static void SaveOwnedAssets()
        {
            // Never flush unrelated dirty assets or upgraded sample prefabs.
            foreach(var guid in AssetDatabase.FindAssets("",new[]{"Assets/VRMaintenanceResearch/TrainingV2"}))
                AssetDatabase.SaveAssetIfDirty(new GUID(guid));
        }
        static partial void RunCommand(string command)
        {
            if(command=="build") Build();
            else if(command=="recover_build") {if(EditorApplication.isPlaying || !File.ReadAllBytes(ScenePath).SequenceEqual(File.ReadAllBytes(Out+"/BASELINE_FILE.unity")))throw new Exception("Saved scene changed; preserve");EditorSceneManager.OpenScene(ScenePath);Build();}
            else if(command=="checks") Checks();
            else if(command=="play") {Guard();var sim=All().Single(t=>t.name=="Training_Simulator_EDITOR_ONLY");sim.gameObject.SetActive(true);EditorApplication.isPlaying=true;}
            else if(command=="stop") EditorApplication.isPlaying=false;
            else if(command=="reopen") { Guard();EditorSceneManager.OpenScene(ScenePath);Checks();Probe(); }
            else if(command=="capture") Capture("edit_overview");
            else if(command=="repair_ui") {Guard();foreach(var g in All().Select(t=>t.GetComponent<TrainingRoundedGraphic>()).Where(g=>g)){if(!g.GetComponent<CanvasRenderer>())g.gameObject.AddComponent<CanvasRenderer>();g.SetAllDirty();}Checks();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());File.Copy(ScenePath,Out+"/MODIFIED_FILE.unity",true);Capture("edit_overview");}
            else if(command=="runtime_probe") RuntimeProbe();
            else if(command=="visual_probe")
            {
                Write("runtime_ray_visual",All().Select(t=>t.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.CurveVisualController>()).Where(v=>v).Select(v=>new {v.name,v.linePropertyAnimationSpeed,v.renderLineInWorldSpace,startWidth=v.lineRenderer.startWidth,endWidth=v.lineRenderer.endWidth,v.lineRenderer.widthMultiplier,v.lineRenderer.positionCount,curve=v.lineRenderer.widthCurve.keys.Select(k=>new{k.time,k.value}).ToArray(),material=v.lineRenderer.sharedMaterial?v.lineRenderer.sharedMaterial.name:null,shader=v.lineRenderer.sharedMaterial?v.lineRenderer.sharedMaterial.shader.name:null,positions=Enumerable.Range(0,v.lineRenderer.positionCount).Select(i=>v.lineRenderer.GetPosition(i).ToString("F3")).ToArray()}).ToArray());
                Capture("runtime_ray_visual");
            }
            else if(command=="input_run") TrainingSimulatorDriver.Begin();
            else if(command=="input_fixture") TrainingSimulatorDriver.BeginFixtureOnly();
            else if(command=="input_stop") TrainingSimulatorDriver.Stop();
            else if(command=="polish") Polish();
            else if(command=="play_write_error") {Guard();var p=All().Select(t=>t.GetComponent<TrainingPrototype>()).Single(x=>x);if(p.logRoot!="")throw new Exception("Non-default log root preserved");var file=Out+"/write_error_blocker_"+DateTime.UtcNow.Ticks;File.WriteAllText(file,"This file intentionally blocks directory creation in an isolated test.");p.logRoot=file;All().Single(t=>t.name=="Training_Simulator_EDITOR_ONLY").gameObject.SetActive(true);EditorApplication.isPlaying=true;}
            else if(command=="write_error_check") {var p=All().Select(t=>t.GetComponent<TrainingPrototype>()).Single(x=>x);var text=string.Join("\n",p.panel.GetComponentsInChildren<TMP_Text>().Select(t=>t.text));bool pass=Application.isPlaying&&p.Session.State==TrainingState.WriteError&&!p.Session.DataComplete&&text.Contains("การบันทึกขัดข้อง")&&p.panel.GetComponentsInChildren<UnityEngine.UI.Button>().All(b=>!b.interactable);Write("runtime_write_error",new{pass,state=p.Session.State.ToString(),dataComplete=p.Session.DataComplete,page=p.panel.CurrentPage,displayed=text,error=p.Session.LastError,fixture="Real file blocks directory creation; lifecycle test, not input-driven"});Capture("runtime_write_error");if(!pass)throw new Exception("Write error UI/lifecycle check failed");}
            else if(command=="restore_write_error") {if(EditorApplication.isPlaying)throw new Exception("Stop first");var p=All().Select(t=>t.GetComponent<TrainingPrototype>()).Single(x=>x);if(!p.logRoot.StartsWith(Out+"/write_error_blocker_",StringComparison.Ordinal))throw new Exception("Unexpected root");p.logRoot="";}
            else if(command=="layout_probe") Write("layout_probe",All().Where(t=>t.name=="Training_Practice_A"||t.name=="Training_NeutralCube"||t.name=="Training_TargetPad").Select(t=>new{t.name,position=t.position.ToString("F3"),bounds=t.GetComponent<Renderer>()?t.GetComponent<Renderer>().bounds.ToString("F3"):"none",collider=t.GetComponent<Collider>()?t.GetComponent<Collider>().bounds.ToString("F3"):"none"}).ToArray());
            else throw new Exception("Unknown command "+command);
        }
        static Material Material(string name,Color color)
        {
            string path="Assets/VRMaintenanceResearch/TrainingV2/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.28f);AssetDatabase.CreateAsset(m,path);}return m;
        }
        static void Polish()
        {
            if(EditorApplication.isPlaying || !File.ReadAllBytes(ScenePath).SequenceEqual(File.ReadAllBytes(Out+"/MODIFIED_FILE.unity")))throw new Exception("Scene changed externally");
            var p=All().Select(t=>t.GetComponent<TrainingPrototype>()).Single(x=>x);
            foreach(var text in p.panel.GetComponentsInChildren<TMP_Text>(true))text.text=text.text.Replace("ฝึกกับชิ้นงานกลางหนึ่งชิ้น ก่อนเริ่มงานจริง","ฝึกกับชิ้นงานสีฟ้าที่มีลูกศรหนึ่งชิ้น").Replace("วางชิ้นงานบนจุดเป้าหมาย","วางชิ้นงานสีฟ้าบนจุดเป้าหมาย");
            p.panel.transform.position=new Vector3(9.40f,1.65f,3.30f);
            p.origin.position=new Vector3(9.72f,0,5.25f);PrefabUtility.RecordPrefabInstancePropertyModifications(p.origin);
            var table=All().Single(t=>t.name=="Training_Practice_A").GetComponent<BoxCollider>();
            var pad=p.targetPad;pad.transform.position=new Vector3(9.20f,table.bounds.max.y+pad.bounds.extents.y,3.77f);
            p.block.transform.position=new Vector3(8.98f,table.bounds.max.y+p.blockCollider.bounds.extents.y+.005f,4.18f);
            var sim=All().Single(t=>t.name=="Training_Simulator_EDITOR_ONLY");sim.gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(sim.gameObject);
            Checks();EditorSceneManager.MarkSceneDirty(p.gameObject.scene);EditorSceneManager.SaveScene(p.gameObject.scene);SaveOwnedAssets();File.Copy(ScenePath,Out+"/MODIFIED_FILE.unity",true);Capture("edit_overview");
        }
        static GameObject Cube(string name,Transform parent,Vector3 position,Vector3 scale,Material material)
        {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent);g.transform.position=position;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=material;return g;}
        [MenuItem("Tools/Training V2/Build prototype (clean V2 only)")]
        public static void Build()
        {
            Guard(); if(All().Any(t=>t.name=="TrainingV2_Prototype"))throw new Exception("Prototype exists; preserve it, no overwrite");
            var root=new GameObject("TrainingV2_Prototype");
            var rig=All().Select(t=>t.GetComponent<Unity.XR.CoreUtils.XROrigin>()).Single(x=>x);
            var camera=rig.Camera;
            var left=All().Single(t=>t.name=="Left Controller");var right=All().Single(t=>t.name=="Right Controller");
            var actions=rig.GetComponent<InputActionManager>().actionAssets.Single();
            InputActionReference Ref(string name) => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(actions)).OfType<InputActionReference>().First(a=>a.action==actions.FindAction(name,true));
            var nearLeft=left.GetComponentInChildren<NearFarInteractor>(true);var nearRight=right.GetComponentInChildren<NearFarInteractor>(true);
            nearLeft.blockUIOnInteractableSelection=true;nearRight.blockUIOnInteractableSelection=true;
            var glove=Material("Training_Glove",new Color(.64f,.77f,.87f));
            foreach(var item in new[]{new{t=left,isLeft=true,near=nearLeft},new{t=right,isLeft=false,near=nearRight}})
            {
                var side=item.isLeft?"Left":"Right";
                var visual=item.t.Find(side+" Controller Visual");if(visual)visual.gameObject.SetActive(false);
                ControllerDrivenTrainingHand.Create(item.t,item.isLeft,glove,Ref("XRI "+side+" Interaction/Select Value"),Ref("XRI "+side+" Interaction/Activate Value"),item.near);
            }
            // Move only authored XR Origin start, never tracked controller/camera transforms or action assets.
            rig.transform.SetPositionAndRotation(new Vector3(9.72f,0,4.85f),Quaternion.Euler(0,180,0));
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig.transform);
            var blue=Material("Training_Block",new Color(.08f,.50f,.68f));var targetMat=Material("Training_Target",new Color(.14f,.22f,.29f));var arrowMat=Material("Training_Arrow",new Color(.94f,.77f,.23f));
            var obj=Cube("Training_OrientationBlock",root.transform,new Vector3(9.48f,1.01f,3.99f),new Vector3(.14f,.10f,.18f),blue);
            var rb=obj.AddComponent<Rigidbody>();rb.mass=.18f;rb.interpolation=RigidbodyInterpolation.Interpolate;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            var grab=obj.AddComponent<XRGrabInteractable>();grab.throwOnDetach=false;grab.useDynamicAttach=true;
            var pad=Cube("Training_TargetPad",root.transform,new Vector3(9.93f,.949f,3.99f),new Vector3(.30f,.018f,.34f),targetMat);
            // Visible directional markers, no socket, no automatic correction.
            Arrow(obj.transform,new Vector3(0,.505f,0),arrowMat,"Block_FrontArrow",new Vector3(1/.14f,1/.10f,1/.18f));
            Arrow(pad.transform,new Vector3(0,.505f,0),arrowMat,"Target_FrontArrow",new Vector3(1/.30f,1/.018f,1/.34f));
            var panel=TrainingPanel.Build(root.transform,TrainingPanelFont.EnsureFont(),camera);
            panel.transform.SetPositionAndRotation(new Vector3(8.96f,1.63f,3.43f),Quaternion.Euler(0,180,0));
            var systems=All().Select(t=>t.GetComponent<EventSystem>()).Where(x=>x).ToArray();
            if(systems.Length==0){var es=new GameObject("Training_EventSystem",typeof(EventSystem),typeof(XRUIInputModule));es.transform.SetParent(root.transform);}
            else if(systems.Length!=1 || !systems[0].GetComponent<XRUIInputModule>())throw new Exception("Unexpected existing EventSystem, preserve before adapting");
            var module=All().Select(t=>t.GetComponent<XRUIInputModule>()).Single(x=>x);module.enableMouseInput=false;module.enableGamepadInput=false;
            var simulator=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/3.4.0/XR Interaction Simulator/XR Interaction Simulator.prefab");
            var sim=(GameObject)PrefabUtility.InstantiatePrefab(simulator);sim.name="Training_Simulator_EDITOR_ONLY";sim.tag="EditorOnly";sim.transform.SetParent(root.transform);sim.SetActive(false);
            var p=root.AddComponent<TrainingPrototype>();p.block=grab;p.blockCollider=obj.GetComponent<BoxCollider>();p.targetPad=pad.GetComponent<BoxCollider>();p.panel=panel;p.head=camera.transform;p.left=left;p.right=right;p.origin=rig.transform;p.leftInteractor=nearLeft;p.rightInteractor=nearRight;p.leftGrip=Ref("XRI Left Interaction/Select Value");p.rightGrip=Ref("XRI Right Interaction/Select Value");
            p.logRoot="";
            Checks();EditorSceneManager.MarkSceneDirty(root.scene);if(!EditorSceneManager.SaveScene(root.scene))throw new Exception("Scene save failed");SaveOwnedAssets();
            File.Copy(ScenePath,Out+"/MODIFIED_FILE.unity",true);Probe();
        }
        static void Arrow(Transform parent,Vector3 local,Material mat,string name,Vector3 inverseScale)
        {
            var root=new GameObject(name);root.transform.SetParent(parent,false);root.transform.localPosition=local;root.transform.localScale=inverseScale;
            var shaft=Cube(name+"_Shaft",root.transform,root.transform.position,new Vector3(.018f,.003f,.07f),mat);shaft.transform.localPosition=new Vector3(0,0,-.012f);UnityEngine.Object.DestroyImmediate(shaft.GetComponent<Collider>());
            foreach(int side in new[]{-1,1}){var tip=Cube(name+"_Tip",root.transform,root.transform.position,new Vector3(.015f,.003f,.042f),mat);tip.transform.localPosition=new Vector3(side*.013f,0,.022f);tip.transform.localRotation=Quaternion.Euler(0,side*40,0);UnityEngine.Object.DestroyImmediate(tip.GetComponent<Collider>());}
        }
        public static void Checks()
        {
            var p=All().Select(t=>t.GetComponent<TrainingPrototype>()).Single(x=>x);
            Write("unity_logic_checks",new{hands=ControllerDrivenTrainingHand.RunSelfCheck(),geometry=TrainingGeometryChecks.Run(),ui=TrainingPanelChecks.Check(p.panel),eventSystems=All().Count(t=>t.GetComponent<EventSystem>()),roots=All().Count(t=>t.name=="TrainingV2_Prototype"),hardware="NOT VERIFIED",scene=ScenePath});
        }
        public static void Capture(string name)
        {
            var p=All().Select(t=>t.GetComponent<TrainingPrototype>()).Single(x=>x);var cam=p.head.GetComponent<Camera>();
            Canvas.ForceUpdateCanvases();
            var old=cam.targetTexture;var rt=new RenderTexture(1600,1200,24);var tex=new Texture2D(1600,1200,TextureFormat.RGB24,false);
            var oldActive=RenderTexture.active;try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,1200),0,0);tex.Apply();File.WriteAllBytes(Out+"/"+name+".png",tex.EncodeToPNG());}finally{cam.targetTexture=old;RenderTexture.active=oldActive;UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);}
        }
        static void RuntimeProbe()
        {
            var p=All().Select(t=>t.GetComponent<TrainingPrototype>()).Single(x=>x);
            Write("runtime_probe",new{state=p.Session?.State.ToString(),folder=p.Session?.DirectoryPath,snapshot=p.Snapshot(),page=p.panel.CurrentPage,head=p.head.position.ToString("F3"),left=p.left.position.ToString("F3"),right=p.right.position.ToString("F3"),surfaces=p.panel.GetComponentsInChildren<TrainingRoundedGraphic>().Select(g=>new{g.name,color=g.color.ToString(),rect=g.rectTransform.rect.ToString(),renderer=g.canvasRenderer?g.canvasRenderer.GetAlpha():-1,material=g.materialForRendering?g.materialForRendering.name:null}).ToArray(),devices=InputSystem.devices.Select(d=>new{d.name,d.layout,usages=d.usages.Select(u=>u.ToString()).ToArray()}).ToArray()});
            Capture("runtime_"+p.panel.CurrentPage);
        }
    }
}
#endif
