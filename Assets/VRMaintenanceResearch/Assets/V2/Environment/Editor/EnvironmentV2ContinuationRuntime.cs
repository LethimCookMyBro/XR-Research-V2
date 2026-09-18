#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace EnvironmentV2Isolated {
[InitializeOnLoad]
public static class EnvironmentV2ContinuationRuntime {
    const string Out = "D:/for_model_codex_vr_tmu/outputs/unity_environment_v2_20260914_153441/runtime.json";
    const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_Environment_V2.unity";
    const string Key = "EnvironmentV2Isolated.ContinuationRuntime";
    static readonly List<object> samples = new List<object>();
    static readonly List<object> stages = new List<object>();
    static readonly List<string> failures = new List<string>();
    static Transform activity, vehicles;
    static Transform[] pallets, joints, signals;
    static Animator animator;
    static int[] palletIds;
    static Vector3[] initialPositions;
    static Quaternion[] initialRotations;
    static float[] palletMotion, jointMotion;
    static double start, nextSample;
    static float initialNormalized, lastNormalized;
    static bool stable, monotonic, oldBackground, gaugeOn, gaugeOff, passOn, passOff, opticalOn, opticalOff;
    static bool initialized;
    static readonly HashSet<string> captured = new HashSet<string>();
    static readonly Dictionary<string,ProfilerRecorder> timings = new Dictionary<string,ProfilerRecorder>();
    static object toggles;
    static object view;
    static JObject result;
    static EnvironmentV2ContinuationRuntime() {
        EditorApplication.playModeStateChanged += State;
        EditorApplication.update += Tick;
    }
    static Transform[] All() { return SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray(); }
    static Transform Find(string name) { return All().Single(t=>t.name==name); }
    static float[] V(Vector3 v) { return new[]{v.x,v.y,v.z}; }
    static float[] Q(Quaternion q) { return new[]{q.x,q.y,q.z,q.w}; }
    static object Pose(Transform t) { return new {t.name,id=t.GetInstanceID(),position=V(t.position),localPosition=V(t.localPosition),rotation=Q(t.localRotation),scale=V(t.localScale),active=t.gameObject.activeInHierarchy}; }
    static void Check(bool condition,string message) { if(!condition) failures.Add(message); }
    public static void Begin() {
        if(Application.dataPath!="D:/VR_TMU/XR-Research-V2/Assets" || SceneManager.GetActiveScene().path!=ScenePath)
            throw new InvalidOperationException("Runtime audit requires current target project and V2 scene");
        if(EditorApplication.isPlayingOrWillChangePlaymode || Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Runtime audit requires stopped Editor and saved scenes; no save or reload performed");
        SessionState.SetString(Key,"entering");
        EditorApplication.isPlaying=true;
    }
    static void State(PlayModeStateChange state) {
        if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetString(Key,"")=="entering") {
            try { Initialize(); } catch(Exception e) { Abort(e); }
        }
        if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetString(Key,"")!="") {
            if(result==null && File.Exists(Out)) result=JObject.Parse(File.ReadAllText(Out));
            if(result==null) result=new JObject { ["status"]="FAIL",["reason"]="Play Mode ended before runtime evidence completed" };
            result["restored_edit_mode"]=!EditorApplication.isPlaying;
            result["restored_scene_path"]=SceneManager.GetActiveScene().path;
            result["restored_scene_dirty"]=SceneManager.GetActiveScene().isDirty;
            result["restored_package_active_states"]=JArray.FromObject(All().Where(t=>t.name=="Factory_Activity"||t.name=="Vehicles_Background").Select(t=>new{t.name,t.gameObject.activeSelf}));
            if((bool)result["restored_scene_dirty"] || (string)result["restored_scene_path"]!=ScenePath) result["status"]="FAIL";
            File.WriteAllText(Out,result.ToString(Formatting.Indented));
            SessionState.EraseString(Key);
            Debug.Log("ENVIRONMENT_V2_CONTINUATION_RUNTIME_COMPLETE: "+(string)result["status"]);
        }
    }
    static void Initialize() {
        samples.Clear(); stages.Clear(); failures.Clear(); captured.Clear(); result=null; initialized=false;
        gaugeOn=gaugeOff=passOn=passOff=opticalOn=opticalOff=false;
        activity=Find("Factory_Activity"); vehicles=Find("Vehicles_Background");
        animator=activity.GetComponentsInChildren<Animator>(true).Single();
        pallets=Enumerable.Range(0,8).Select(i=>Find("Loop_Pallet_"+i.ToString("00"))).ToArray();
        joints=new[]{"A","B"}.SelectMany(r=>Enumerable.Range(1,6).Select(i=>Find("Robot_"+r+"_Joint_"+i))).ToArray();
        signals=new[]{"Production_Gauge_Scan","Production_PASS_Lamp","Production_PASS_Label","Production_Optical_Scan"}.Select(Find).ToArray();
        Find("Robot_A_RobotRoot"); Find("Robot_B_RobotRoot");
        var rooms=new[]{"Training","Computer","Fan","Corridor","Factory_Static"}.Select(Find).ToArray();
        bool a=activity.gameObject.activeSelf, v=vehicles.gameObject.activeSelf;
        Check(a&&v,"Both independent packages must initially be active for animation/visibility audit");
        var toggleRows=new List<object>();
        try {
            activity.gameObject.SetActive(false);
            bool onlyActivityOff=!activity.gameObject.activeInHierarchy && vehicles.gameObject.activeInHierarchy && rooms.All(t=>t.gameObject.activeInHierarchy);
            Check(onlyActivityOff,"Factory Activity independent off failed"); toggleRows.Add(new{disabled="Factory_Activity",pass=onlyActivityOff});
            activity.gameObject.SetActive(a); vehicles.gameObject.SetActive(false);
            bool onlyVehiclesOff=activity.gameObject.activeInHierarchy && !vehicles.gameObject.activeInHierarchy && rooms.All(t=>t.gameObject.activeInHierarchy);
            Check(onlyVehiclesOff,"Vehicles independent off failed"); toggleRows.Add(new{disabled="Vehicles_Background",pass=onlyVehiclesOff});
        } finally { activity.gameObject.SetActive(a); vehicles.gameObject.SetActive(v); }
        toggles=new{observations=toggleRows,restored_activity=activity.gameObject.activeSelf,restored_vehicles=vehicles.gameObject.activeSelf};
        oldBackground=Application.runInBackground; Application.runInBackground=true;
        var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);
        var available=handles.Select(ProfilerRecorderHandle.GetDescription).Where(d=>d.Name=="Main Thread" || d.Name=="Render Thread" || d.Name=="GPU Frame Time").ToArray();
        foreach(var description in available) if(!timings.ContainsKey(description.Name)) timings.Add(description.Name,ProfilerRecorder.StartNew(description.Category,description.Name,1));
        File.WriteAllText(Path.GetDirectoryName(Out)+"/timing_availability.json",JsonConvert.SerializeObject(available.Select(d=>new{d.Name,category=d.Category.Name,unit=d.UnitType.ToString()}),Formatting.Indented));
        palletIds=pallets.Select(t=>t.GetInstanceID()).ToArray(); initialPositions=pallets.Select(t=>t.position).ToArray(); initialRotations=joints.Select(t=>t.localRotation).ToArray();
        palletMotion=new float[8]; jointMotion=new float[12]; stable=monotonic=true;
        start=EditorApplication.timeSinceStartup; nextSample=0;
        initialNormalized=lastNormalized=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        view=new {scope="Unity Editor PlayMode; not Player or Quest",unity=Application.unityVersion,quality=QualitySettings.names[QualitySettings.GetQualityLevel()],vSyncCount=QualitySettings.vSyncCount,targetFrameRate=Application.targetFrameRate,screen=new[]{Screen.width,Screen.height},cameras=All().Select(t=>t.GetComponent<Camera>()).Where(c=>c&&c.enabled).Select(c=>new{c.name,position=V(c.transform.position),rotation=V(c.transform.eulerAngles),c.fieldOfView,c.pixelWidth,c.pixelHeight}).ToArray()};
        initialized=true; SessionState.SetString(Key,"running");
    }
    static Dictionary<string,object> Stats() {
        var values=new Dictionary<string,object>();
        foreach(var pair in timings) values[pair.Key]=new{valid=pair.Value.Valid,count=pair.Value.Valid?pair.Value.Count:0,unit=pair.Value.Valid?pair.Value.UnitType.ToString():null,value=pair.Value.Valid && pair.Value.Count>0?(long?)pair.Value.LastValue:null};
        foreach(string name in new[]{"batches","setPassCalls","triangles","frameTime","renderTime"}) {
            try {
                var p=typeof(UnityStats).GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
                var f=typeof(UnityStats).GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
                values[name]=p!=null?p.GetValue(null):f!=null?f.GetValue(null):null;
            } catch { values[name]=null; }
        }
        return values;
    }
    static bool Visible(Transform t) { return t.gameObject.activeInHierarchy && t.localScale.sqrMagnitude>.03f && t.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled); }
    static void Tick() {
        if(!EditorApplication.isPlaying || SessionState.GetString(Key,"")!="running") return;
        try {
            if(!initialized) throw new InvalidOperationException("Domain reload interrupted active runtime audit");
            double wall=EditorApplication.timeSinceStartup-start;
            var info=animator.GetCurrentAnimatorStateInfo(0);
            float seconds=info.normalizedTime*info.length;
            float phase=Mathf.Repeat(seconds,6);
            monotonic &= info.normalizedTime+0.0001f>=lastNormalized; lastNormalized=info.normalizedTime;
            var current=All().Where(t=>t.name.StartsWith("Loop_Pallet_",StringComparison.Ordinal)).ToArray();
            stable &= current.Length==8 && current.Select(t=>t.GetInstanceID()).OrderBy(i=>i).SequenceEqual(palletIds.OrderBy(i=>i));
            for(int i=0;i<8;i++) palletMotion[i]=Mathf.Max(palletMotion[i],Vector3.Distance(initialPositions[i],pallets[i].position));
            for(int i=0;i<12;i++) jointMotion[i]=Mathf.Max(jointMotion[i],Quaternion.Angle(initialRotations[i],joints[i].localRotation));
            // Observe every Editor update: the gauge's authored visible window is only 0.12 seconds.
            bool nearGauge=Mathf.Abs(phase-4.5f)<.06f, nearPass=Mathf.Abs(phase-2.5f)<.1f, nearOptical=Mathf.Abs(phase-1.5f)<.1f, idle=Mathf.Abs(phase-.5f)<.1f;
            if(nearGauge||nearPass||nearOptical||idle) {
                bool g=Visible(signals[0]), l=Visible(signals[1]), p=Visible(signals[2]), o=Visible(signals[3]);
                if(nearGauge) gaugeOn|=g; if(nearPass) passOn|=l&&p; if(nearOptical) opticalOn|=o;
                if(idle) {gaugeOff|=!g;passOff|=!l&&!p;opticalOff|=!o;}
                stages.Add(new{wall_seconds=wall,animation_seconds=seconds,phase_seconds=phase,gauge_visible=g,pass_lamp_visible=l,pass_label_visible=p,optical_visible=o,signals=signals.Select(Pose).ToArray()});
                string shot=nearGauge&&g?"gauge":nearPass&&l&&p?"pass":nearOptical&&o?"fastening_inspection":null;
                if(shot!=null && captured.Add(shot)) EnvironmentV2Continuation.Capture("runtime_"+shot);
            }
            if(wall>=nextSample) {
                nextSample=wall+.1;
                samples.Add(new{wall_seconds=wall,animation_seconds=seconds,normalized_time=info.normalizedTime,phase_seconds=phase,time_scale=Time.timeScale,pallet_count=current.Length,pallets=pallets.Select(Pose).ToArray(),joints=joints.Select(Pose).ToArray(),signals=signals.Select(Pose).ToArray(),stats=Stats()});
            }
            if(info.normalizedTime-initialNormalized>=1.15f && seconds>=55.2f) Finish(wall,info);
            else if(wall>240) { failures.Add("Timed out before naturally observing at least 1.15 animation cycles"); Finish(wall,info); }
        } catch(Exception e) { Abort(e); }
    }
    static void Finish(double wall,AnimatorStateInfo info) {
        Check(Mathf.Abs(info.length-48)<.05f,"Actual Animator state duration is not 48 seconds");
        Check(info.loop,"Actual Animator state does not loop");
        Check(info.normalizedTime-initialNormalized>=1.15f && monotonic,"Natural Animator progression/loop failed");
        Check(stable,"Eight pallet object identities/count changed");
        Check(palletMotion.All(m=>m>.01f),"One or more pallets did not move naturally");
        Check(jointMotion.All(m=>m>.01f),"One or more robot joints did not move naturally");
        Check(gaugeOn&&gaugeOff,"Gauge on/off phase observation incomplete");
        Check(passOn&&passOff,"PASS lamp/label on/off phase observation incomplete");
        Check(opticalOn&&opticalOff,"Robot B optical inspection phase observation incomplete");
        var tracked=activity.GetComponentsInChildren<Transform>(true);
        animator.Play(info.fullPathHash,0,0); animator.Update(0);
        var endpoint=tracked.Select(t=>t.localToWorldMatrix).ToArray();
        animator.Play(info.fullPathHash,0,1); animator.Update(0);
        float seam=0;
        for(int i=0;i<tracked.Length;i++) for(int r=0;r<4;r++) for(int c=0;c<4;c++) seam=Mathf.Max(seam,Mathf.Abs(endpoint[i][r,c]-tracked[i].localToWorldMatrix[r,c]));
        animator.Play(info.fullPathHash,0,(48-.01f)/48); animator.Update(0); var before=pallets.Select(t=>t.position).ToArray();
        animator.Play(info.fullPathHash,0,(48+.01f)/48); animator.Update(0);
        float near=pallets.Select((t,i)=>Vector3.Distance(t.position,before[i])).Max();
        Check(seam<.003f,"Forced endpoint matrix seam exceeds 0.003"); Check(near<.05f,"Pallet displacement across 20 ms restart exceeds 0.05 m");
        animator.Play(info.fullPathHash,0,info.normalizedTime); animator.Update(0);
        result=JObject.FromObject(new{status=failures.Count==0?"PASS":"FAIL",failures,scope="Editor PlayMode animation/toggle observation only; XR/controller/hardware not tested",view,wall_observation_seconds=wall,initial_normalized_time=initialNormalized,final_normalized_time=info.normalizedTime,actual_state_duration_seconds=info.length,natural_loop_observed=info.normalizedTime-initialNormalized>=1.15f,monotonic,pallet_count_stable=stable,pallet_ids=palletIds,pallet_motion_max_m=palletMotion,joint_names=joints.Select(t=>t.name).ToArray(),joint_motion_max_degrees=jointMotion,gaugeOn,gaugeOff,passOn,passOff,opticalOn,opticalOff,independent_toggles=toggles,forced_endpoint_check=new{matrix_error=seam,pallet_displacement_20ms_m=near,note="Explicit Animator sampling after natural loop observation; not substituted for natural playback"},stats_note="Raw UnityStats reflection values; null means unsupported. frameTime/renderTime are Editor timing, not GPU or Quest measurements.",samples,stages});
        End();
    }
    static void Abort(Exception e) {
        result=JObject.FromObject(new{status="FAIL",error=e.ToString(),failures,samples,stages,scope="Editor audit incomplete; no XR/hardware result"});
        End();
    }
    static void End() {
        foreach(var recorder in timings.Values) recorder.Dispose();timings.Clear();
        if(initialized) Application.runInBackground=oldBackground;
        File.WriteAllText(Out,result.ToString(Formatting.Indented));
        SessionState.SetString(Key,"finished"); EditorApplication.isPlaying=false;
    }
}
}
#endif
