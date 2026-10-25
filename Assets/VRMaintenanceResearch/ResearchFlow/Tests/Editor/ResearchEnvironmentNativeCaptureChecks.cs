#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchEnvironmentNativeCaptureChecks
    {
        [MenuItem("VR Research/Research Flow/Check Native ENV Capture Guards")]
        static void RunFromMenu() => Run();

        public static void Run(string outputPath = null)
        {
            var checks=new List<object>();
            void Check(bool pass,string name) {checks.Add(new {name,pass});if(!pass)throw new InvalidOperationException(name);}
            string Refuse(string identity="th.ac.tmu.vr.researchflow.quest3.technical",string command="capture",string id="env-01234567-abcd",double age=1,bool idle=true)
                => ResearchEnvironmentNativeCapture.RequestRefusal(identity,command,id,age,idle);
            Check(Refuse()==null,"fresh technical idle capture accepted");
            Check(Refuse(age:-5)==null && Refuse(age:30)==null,"clock window boundary accepted");
            Check(Refuse(identity:null)!=null,"missing package refused");
            Check(Refuse(identity:"th.ac.tmu.vr.researchflow.quest3")!=null,"study identity refused");
            Check(Refuse(identity:"technical")!=null,"missing technical suffix boundary refused");
            Check(Refuse(identity:"app.technical.extra")!=null,"suffix spoof refused");
            Check(Refuse(command:"state")!=null,"other protocol commands refused");
            Check(Refuse(command:"technical_scenario")!=null,"scenario command refused");
            Check(Refuse(id:null)!=null && Refuse(id:"")!=null,"missing id refused");
            Check(Refuse(id:"../StudyExports")!=null,"directory traversal refused");
            Check(Refuse(id:"/sdcard/other")!=null,"absolute output path refused");
            Check(Refuse(id:"x\ny")!=null && Refuse(id:"x\n")!=null,"newline id refused including final newline");
            Check(Refuse(id:new string('a',81))!=null,"oversized id refused");
            Check(Refuse(age:30.001)!=null,"expired request refused");
            Check(Refuse(age:-5.001)!=null,"future request refused");
            Check(Refuse(age:double.NaN)!=null && Refuse(age:double.PositiveInfinity)!=null,"nonfinite clock refused");
            Check(Refuse(idle:false)!=null,"active participant/session/scenario refused");
            Check(ResearchEnvironmentNativeCapture.PageRefusal("baseline",0,8)==null,"baseline preserves original eight-view selection");
            Check(ResearchEnvironmentNativeCapture.PageRefusal("baseline",1,8)!=null,"baseline page overflow refused");
            Check(ResearchEnvironmentNativeCapture.PageRefusal("full",0,121)==null && ResearchEnvironmentNativeCapture.PageRefusal("full",15,121)==null,"full profile accepts final partial page");
            Check(ResearchEnvironmentNativeCapture.PageRefusal("full",16,121)!=null,"full profile page overflow refused");
            Check(ResearchEnvironmentNativeCapture.PageRefusal("full",-1,121)!=null,"negative page refused");
            Check(ResearchEnvironmentNativeCapture.PageRefusal("../other",0,121)!=null,"profile cannot name an arbitrary resource/path");
            Check(ResearchEnvironmentNativeCapture.PageRefusal("full",0,0)!=null && ResearchEnvironmentNativeCapture.PageRefusal("full",0,513)!=null,"empty or unbounded full plan refused");
            var full=Resources.Load<TextAsset>("ResearchEnvironmentFullCapturePlan");
            Check(full!=null,"reviewed full plan is available as a packaged resource");
            var selection=typeof(ResearchEnvironmentNativeCapture).GetMethod("SelectPoses",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            var selectedNames=new HashSet<string>(); string selectedHash=null; int totalSelected=0;
            for(int page=0;page<45;page++)
            {
                object[] args={"full",page,0,null};
                var pagePoses=(Array)selection.Invoke(null,args);
                Check((int)args[2]==353 && pagePoses.Length==Math.Min(8,353-page*8),"full resource page "+page+" returns its actual bounded poses");
                if(selectedHash==null)selectedHash=(string)args[3];
                Check((string)args[3]==selectedHash && selectedHash.Length==64,"full resource hash stays stable across pages "+page);
                foreach(var pose in pagePoses) {string name=(string)pose.GetType().GetField("name").GetValue(pose);Check(selectedNames.Add(name),"full resource pose appears once: "+name);totalSelected++;}
            }
            Check(totalSelected==353 && selectedNames.Count==353,"all finite full-plan views selected once across 45 pages");
            bool RejectSlices(int total,int start,int original,int materials,bool batched) {try {ResearchEnvironmentNativeCapture.SubmeshIndices(total,start,original,materials,batched);return false;}catch(InvalidOperationException){return true;}}
            Check(string.Join(",",ResearchEnvironmentNativeCapture.SubmeshIndices(2,0,2,2,false))=="0,1","unbatched original slices preserved");
            Check(string.Join(",",ResearchEnvironmentNativeCapture.SubmeshIndices(9,4,2,2,true))=="4,5","batched renderer draws only its reviewed original slices");
            Check(RejectSlices(9,4,2,3,true),"extra material slots require separate mapping");
            Check(RejectSlices(9,8,2,2,true),"batch slice overrun refused");
            Check(RejectSlices(9,-1,2,2,true),"negative batch slice refused");
            Check(RejectSlices(9,4,0,0,true),"empty original geometry refused");
            Check(RejectSlices(9,0,2,2,false),"unexpected unbatched combined mesh refused");
            Check(RejectSlices(int.MaxValue,int.MaxValue,2,2,true),"integer overflow cannot admit a slice");
            Check(!ResearchEnvironmentNativeCapture.ValidScenePixels(new[]{new Color32(0,0,0,0),new Color32(0,0,0,0)}),"uninitialized alpha-zero image cannot pass");
            Check(!ResearchEnvironmentNativeCapture.ValidScenePixels(new[]{new Color32(255,0,255,255),new Color32(255,0,255,255)}),"unchanged control marker cannot pass as ENV");
            Check(!ResearchEnvironmentNativeCapture.ValidScenePixels(new[]{new Color32(9,9,9,255),new Color32(9,9,9,255)}),"background-only image cannot pass as ENV");
            Check(ResearchEnvironmentNativeCapture.ValidScenePixels(new[]{new Color32(9,9,9,255),new Color32(120,80,50,255)}),"opaque scene variation passes pixel availability only");
            var narrowDetail = new Texture2D(25,25,TextureFormat.RGBA32,false,false) {hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var uniform=Enumerable.Repeat(new Color32(66,71,73,255),625).ToArray();
                narrowDetail.SetPixels32(uniform); narrowDetail.Apply(false,false);
                Check(!ResearchEnvironmentNativeCapture.ValidScenePixels(narrowDetail),"full texture background-only control remains refused");
                Check(ResearchEnvironmentNativeCapture.ValidScenePixels(narrowDetail,new Color32(9,9,9,255)),"uniform rendered ceiling differs from independently verified empty camera background");
                Check(!ResearchEnvironmentNativeCapture.ValidBackgroundPixels(narrowDetail,new Color32(9,9,9,255)),"ceiling cannot masquerade as the empty background control");
                // The actual d01-ceiling failure contained a narrow strip between every 7x7 sample row.
                for(int x=0;x<25;x++)uniform[14*25+x]=new Color32(242,253,255,255);
                narrowDetail.SetPixels32(uniform); narrowDetail.Apply(false,false);
                Check(ResearchEnvironmentNativeCapture.ValidScenePixels(narrowDetail),"thin rendered detail between sparse sample rows remains available");
                narrowDetail.SetPixels32(Enumerable.Repeat(new Color32(255,0,255,255),625).ToArray()); narrowDetail.Apply(false,false);
                Check(!ResearchEnvironmentNativeCapture.ValidScenePixels(narrowDetail),"full texture unchanged GPU marker still refused");
                narrowDetail.SetPixels32(Enumerable.Repeat(new Color32(0,0,0,0),625).ToArray()); narrowDetail.Apply(false,false);
                Check(!ResearchEnvironmentNativeCapture.ValidScenePixels(narrowDetail),"full texture uninitialized readback still refused");
                Check(!ResearchEnvironmentNativeCapture.ValidScenePixels((Texture2D)null),"missing full texture refused");
                var background=new Color32(9,9,9,255);
                narrowDetail.SetPixels32(Enumerable.Repeat(background,625).ToArray()); narrowDetail.Apply(false,false);
                Check(ResearchEnvironmentNativeCapture.ValidBackgroundPixels(narrowDetail,background),"complete opaque expected empty camera control accepted");
                Check(!ResearchEnvironmentNativeCapture.ValidScenePixels(narrowDetail,background),"unchanged calibrated background is not accepted as ENV");
                Check(!ResearchEnvironmentNativeCapture.ValidBackgroundPixels(narrowDetail,new Color32(8,9,9,255)),"one-channel background calibration mismatch refused");
                var mixed=Enumerable.Repeat(new Color32(66,71,73,255),625).ToArray(); mixed[624]=new Color32(255,0,255,255);
                narrowDetail.SetPixels32(mixed); narrowDetail.Apply(false,false);
                Check(!ResearchEnvironmentNativeCapture.ValidScenePixels(narrowDetail,background),"residual GPU marker at final pixel refused despite earlier rendered geometry");
                narrowDetail.SetPixels32(Enumerable.Repeat(new Color32(255,0,255,255),625).ToArray()); narrowDetail.Apply(false,false);
                Check(!ResearchEnvironmentNativeCapture.ValidScenePixels(narrowDetail,background),"uniform unchanged GPU marker refused with calibration");
                Check(!ResearchEnvironmentNativeCapture.ValidBackgroundPixels(narrowDetail,new Color32(255,0,255,255)),"GPU marker cannot be a background calibration authority");
                narrowDetail.SetPixels32(Enumerable.Repeat(new Color32(0,0,0,0),625).ToArray()); narrowDetail.Apply(false,false);
                Check(!ResearchEnvironmentNativeCapture.ValidScenePixels(narrowDetail,background),"alpha-zero readback refused with calibration");
                Check(!ResearchEnvironmentNativeCapture.ValidBackgroundPixels(narrowDetail,new Color32(0,0,0,0)),"alpha-zero background calibration refused");
            }
            finally {UnityEngine.Object.DestroyImmediate(narrowDetail);}
            var layout=JObject.Parse(Resources.Load<TextAsset>("ResearchEnvironmentCaptureLayout").text);
            var items=(JArray)layout["renderers"];
            Check(items.Count==338 && items.Select(x=>(string)x["path"]).Distinct().Count()==338,"complete unique original ENV layout is packaged");
            Check(items.All(x=>(int)x["submesh_count"]>0 && (int)x["submesh_count"]==((JArray)x["materials"]).Count),"reviewed original slices match material cardinality");
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var fixture=new GameObject("OwnedSnapshotFixture") {hideFlags=HideFlags.HideAndDontSave};
                SceneManager.MoveGameObjectToScene(fixture,scene);
                var light=fixture.AddComponent<Light>(); light.color=new Color(.1f,.2f,.3f,1);
                var renderer=fixture.AddComponent<MeshRenderer>(); renderer.lightmapScaleOffset=new Vector4(.1f,.2f,.3f,.4f);
                string before=ResearchEnvironmentNativeCapture.OriginalState(new[]{fixture});
                Check(before.Length>0 && before.Contains("lightmapScaleOffset") && before.Contains("color"),"real Color and Vector4 serialize without recursive Unity getters");
                fixture.transform.localPosition=Vector3.one;
                Check(ResearchEnvironmentNativeCapture.OriginalState(new[]{fixture})!=before,"guard detects original transform changes");
                fixture.transform.localPosition=Vector3.zero; light.color=Color.red;
                Check(ResearchEnvironmentNativeCapture.OriginalState(new[]{fixture})!=before,"guard detects original light changes");
                light.color=new Color(.1f,.2f,.3f,1); renderer.enabled=false;
                Check(ResearchEnvironmentNativeCapture.OriginalState(new[]{fixture})!=before,"guard detects renderer visibility changes");
                renderer.enabled=true;
                var casting=renderer.shadowCastingMode; var receiving=renderer.receiveShadows;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                Check(ResearchEnvironmentNativeCapture.OriginalState(new[]{fixture})!=before,"guard detects renderer casting changes");
                renderer.shadowCastingMode=casting; renderer.receiveShadows=!receiving;
                Check(ResearchEnvironmentNativeCapture.OriginalState(new[]{fixture})!=before,"guard detects renderer shadow receiving changes");
                renderer.receiveShadows=receiving;
                Check(ResearchEnvironmentNativeCapture.OriginalState(new[]{fixture})==before,"guard recognizes exact fixture restore");
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
            if(outputPath!=null) {Directory.CreateDirectory(Path.GetDirectoryName(outputPath));File.WriteAllText(outputPath,JsonConvert.SerializeObject(new {scope="Editor protocol failure guards, not native image or wearer verification",passed=true,count=checks.Count,checks},Formatting.Indented));}
            UnityEngine.Debug.Log("ENV_NATIVE_CAPTURE_GUARDS_PASS "+checks.Count);
        }
    }
}
#endif
