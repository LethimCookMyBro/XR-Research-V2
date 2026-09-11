#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace EnvironmentV2Isolated
{
 public static partial class EnvironmentV2Import
 {
  const string VisualAudit=Audit+"/GlassOakFix";
  const string VisualMaterials=Folder+"/Materials";
  static IEnumerable<Renderer> RoomRenderers()=>Groups.Take(4).SelectMany(n=>Environment().transform.Find(n).GetComponentsInChildren<Renderer>(true));
  static string Hierarchy(Transform t)=>t.parent?Hierarchy(t.parent)+"/"+t.name:t.name;
  static string AssetId(Object obj){if(!obj)return "";AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj,out string guid,out long id);return guid+":"+id;}
  static object Structure()=>TargetScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).OrderBy(Hierarchy).Select(t=>new{
   path=Hierarchy(t),position=V(t.localPosition),rotation=new[]{t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w},scale=V(t.localScale),active=t.gameObject.activeSelf,
   components=t.GetComponents<Component>().Select(c=>c?c.GetType().FullName:"MISSING").ToArray(),mesh=t.TryGetComponent<MeshFilter>(out var mf)?AssetId(mf.sharedMesh):"",
   renderer=t.TryGetComponent<Renderer>(out var r)?new{enabled=r.enabled,shadows=r.shadowCastingMode,receive=r.receiveShadows}:null,
   box=t.TryGetComponent<BoxCollider>(out var b)?new{center=V(b.center),size=V(b.size),trigger=b.isTrigger,enabled=b.enabled}:null,
   meshCollider=t.TryGetComponent<MeshCollider>(out var mc)?new{mesh=AssetId(mc.sharedMesh),convex=mc.convex,trigger=mc.isTrigger,enabled=mc.enabled}:null,
   animator=t.TryGetComponent<Animator>(out var a)?new{controller=AssetId(a.runtimeAnimatorController),rootMotion=a.applyRootMotion,culling=a.cullingMode,enabled=a.enabled}:null,
   light=t.TryGetComponent<Light>(out var l)?new{type=l.type,intensity=l.intensity,color=new[]{l.color.r,l.color.g,l.color.b,l.color.a},shadows=l.shadows}:null
  }).ToArray();
  static void VisualCommand(string command)
  {
   Directory.CreateDirectory(VisualAudit);SessionState.SetString("EnvironmentV2Isolated.OutputAudit",VisualAudit);
   TargetScene();Protected();Check(Application.unityVersion=="6000.3.20f1","Unexpected Unity version");
   if(command=="visual_baseline")
   {
    Check(!EditorApplication.isPlaying,"Exit PlayMode before baseline capture");
    Check(!TargetScene().isDirty,"Unsaved scene changes; baseline must be preserved first");
    Check(SaveProbe("baseline_console.json").compileErrorEntries==0,"Baseline compile errors");
    Check(!File.Exists(VisualAudit+"/baseline_structure.json"),"Baseline already captured");
    if(!File.Exists(VisualAudit+"/BASELINE.unity"))File.Copy(ScenePath,VisualAudit+"/BASELINE.unity");
    Check(Hash(ScenePath)==Hash(VisualAudit+"/BASELINE.unity"),"Baseline file differs");Write("baseline_structure.json",Structure());
    Write("baseline_materials.json",RoomRenderers().SelectMany(r=>r.sharedMaterials.Select((m,i)=>new{r,m,i})).Where(x=>x.m&& (x.m.name=="Glass"||x.m.name=="Oak")).Select(x=>new{path=Hierarchy(x.r.transform),slot=x.i,material=x.m.name,asset=AssetDatabase.GetAssetPath(x.m),shader=x.m.shader.name,queue=x.m.renderQueue,keywords=x.m.shaderKeywords,properties=new[]{"_SrcBlend","_DstBlend","_ZWrite","_Surface","transmissionFactor"}.Where(x.m.HasProperty).ToDictionary(n=>n,n=>x.m.GetFloat(n)),colors=new[]{"baseColorFactor","_BaseColor"}.Where(x.m.HasProperty).ToDictionary(n=>n,n=>x.m.GetColor(n).ToString()),textures=x.m.GetTexturePropertyNames().Where(n=>x.m.GetTexture(n)).ToArray()}).ToArray());
    Debug.Log("VISUAL_BASELINE_PASS checkpoint_scene_preserved=1");
   }
   else if(command=="visual_fix") ApplyVisualFix();
   else if(command=="visual_check")
   {
    Check(!EditorApplication.isPlaying,"Exit PlayMode first");
    EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
    // Compare the same JSON numeric representation on both sides (float versus parsed double).
    var before=JToken.Parse(File.ReadAllText(VisualAudit+"/baseline_structure.json"));var after=JToken.Parse(JsonConvert.SerializeObject(Structure()));
    Write("modified_structure.json",after);Check(JToken.DeepEquals(before,after),"Non-material scene structure changed");
    AuditScene();Write("visual_verification.json",new{status="PASS",structure_unchanged=true,scene_sha256=Hash(ScenePath),groups=Environment().transform.childCount,console=SaveProbe("final_console.json")});
    CaptureVisualProof();Debug.Log("VISUAL_CHECK_PASS structure_unchanged=1 groups=7");
   }
   else if(command=="visual_play") BeginRuntime();
   else if(command=="visual_rollback")
   {
    string modified=Hash(ScenePath);var restored=EditorSceneManager.OpenScene(VisualAudit+"/Rollback_Test/Restored_Baseline.unity",OpenSceneMode.Additive);
    var root=restored.GetRootGameObjects().Single(g=>g.name=="Environment_V1");
    var mats=root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).ToArray();
    Check(!mats.Any(m=>m.name.StartsWith("V2_RoomGlass")||m.name.StartsWith("V2_Oak")),"Rollback retained overrides");
    Check(root.transform.childCount==7,"Rollback groups missing");EditorSceneManager.CloseScene(restored,true);
    Check(Hash(ScenePath)==modified,"Rollback modified current scene");
    Write("rollback_verification.json",new{status="PASS",restored_original_materials=true,groups=7,modified_retained=true,baseline_exact=Hash(VisualAudit+"/BASELINE.unity")==Hash(VisualAudit+"/Rollback_Test/Restored_Baseline.unity"),console=SaveProbe("rollback_console.json")});
    SessionState.EraseString("EnvironmentV2Isolated.OutputAudit");Debug.Log("VISUAL_ROLLBACK_PASS restored_original_materials=1 modified_retained=1");
   }
   else throw new InvalidOperationException(command);
  }
  static void ApplyVisualFix()
  {
   Check(File.Exists(VisualAudit+"/baseline_structure.json"),"Capture baseline first");
   Check(!Directory.Exists(VisualMaterials),"Material folder already exists; do not overwrite");Directory.CreateDirectory(VisualMaterials);AssetDatabase.Refresh();
   var shader=Shader.Find("Universal Render Pipeline/Lit");Check(shader&&shader.isSupported,"URP Lit missing");
   var glass=new Material(shader){name="V2_RoomGlass_Alpha",renderQueue=3000};
   glass.SetFloat("_Surface",1);glass.SetFloat("_Blend",0);glass.SetFloat("_BlendModePreserveSpecular",0);
   glass.SetFloat("_ZWrite",0);glass.SetFloat("_Cull",(float)CullMode.Back);glass.SetFloat("_ReceiveShadows",0);
   glass.SetFloat("_Metallic",0);glass.SetFloat("_Smoothness",.6f);glass.SetColor("_BaseColor",new Color(.72f,.87f,.92f,.12f));
   UnityEditor.BaseShaderGUI.SetupMaterialBlendMode(glass);glass.EnableKeyword("_RECEIVE_SHADOWS_OFF");glass.SetShaderPassEnabled("ShadowCaster",false);
   AssetDatabase.CreateAsset(glass,VisualMaterials+"/V2_RoomGlass_Alpha.mat");
   // Small reusable color map only; no custom shader, refraction or render feature.
   var tex=new Texture2D(256,256,TextureFormat.RGB24,false);var pixels=new Color[256*256];
   for(int y=0;y<256;y++)for(int x=0;x<256;x++)
   {
    float u=x/256f,v=y/256f;float warp=.025f*Mathf.Sin(v*Mathf.PI*2)+.012f*Mathf.Sin(v*Mathf.PI*6);
    float grain=Mathf.Pow(.5f+.5f*Mathf.Sin((u+warp)*Mathf.PI*2*24),12);
    float broad=.5f+.5f*Mathf.Sin((u+warp)*Mathf.PI*2*5);float fine=Mathf.Sin(u*Mathf.PI*2*91+Mathf.Sin(v*Mathf.PI*2*7));
    pixels[y*256+x]=Color.Lerp(new Color(.42f,.25f,.12f),new Color(.73f,.53f,.30f),.52f+.25f*broad-.24f*grain+.025f*fine);
   }
   tex.SetPixels(pixels);tex.Apply();string texturePath=VisualMaterials+"/Oak_Grain_256.png";File.WriteAllBytes(texturePath,tex.EncodeToPNG());Object.DestroyImmediate(tex);
   AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
   importer.sRGBTexture=true;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=2;importer.maxTextureSize=256;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
   var oak=new Material(shader){name="V2_Oak_Simple"};oak.SetColor("_BaseColor",Color.white);oak.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));oak.SetFloat("_Metallic",0);oak.SetFloat("_Smoothness",.28f);
   AssetDatabase.CreateAsset(oak,VisualMaterials+"/V2_Oak_Simple.mat");
   var changes=new List<object>();
   foreach(var r in RoomRenderers())
   {
    var mats=r.sharedMaterials;bool changed=false;
    for(int i=0;i<mats.Length;i++)if(mats[i]&&(mats[i].name=="Glass"||mats[i].name=="Oak"))
    {var old=mats[i];mats[i]=old.name=="Glass"?glass:oak;changed=true;changes.Add(new{path=Hierarchy(r.transform),slot=i,before=old.name,after=mats[i].name});}
    if(changed){r.sharedMaterials=mats;PrefabUtility.RecordPrefabInstancePropertyModifications(r);}
   }
   Check(changes.Count>0,"No material assignments changed");AssetDatabase.SaveAssetIfDirty(glass);AssetDatabase.SaveAssetIfDirty(oak);EditorSceneManager.MarkSceneDirty(TargetScene());Check(EditorSceneManager.SaveScene(TargetScene()),"Save failed");
   Write("material_changes.json",changes);Debug.Log("VISUAL_FIX_PASS material_slots="+changes.Count);
  }
  static void CaptureVisualProof()
  {
   var camera=TargetScene().GetRootGameObjects().Single(g=>g.name=="Audit_Camera").GetComponent<Camera>();var pos=camera.transform.position;var rot=camera.transform.rotation;float fov=camera.fieldOfView;var rows=new List<object>();
   try
   {
    foreach(var view in Source["views"].Where(v=>new[]{"06_Computer_Wide","08_Fan_Wide","17_Computer_Activity"}.Contains((string)v["name"])))
    {
     camera.transform.position=Vec(view["position"]);var p=camera.transform.position;p.y=1.65f;camera.transform.position=p;camera.transform.rotation=Quaternion.LookRotation(Vec(view["forward"]),Vector3.up);camera.fieldOfView=60;
     var previous=RenderTexture.active;var rt=new RenderTexture(1080,720,24,RenderTextureFormat.ARGB32);rt.Create();var tex=new Texture2D(1080,720,TextureFormat.RGB24,false);
     try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1080,720),0,0);tex.Apply();string path=VisualAudit+"/"+(string)view["name"]+"_After.png";File.WriteAllBytes(path,tex.EncodeToPNG());rows.Add(new{path,position=V(camera.transform.position)});}
     finally{RenderTexture.active=null;Object.DestroyImmediate(tex);rt.Release();Object.DestroyImmediate(rt);RenderTexture.active=previous;}
    }
   }
   finally{camera.transform.SetPositionAndRotation(pos,rot);camera.fieldOfView=fov;}
   Write("screenshots.json",rows);
  }
 }
}
#endif
