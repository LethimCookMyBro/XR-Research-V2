#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentV2Isolated {
[InitializeOnLoad]
public static class EnvironmentV2ContinuationWalk {
    const string Out="D:/for_model_codex_vr_tmu/outputs/unity_environment_v2_20260914_153441/";
    const string ScenePath="Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_Environment_V2.unity";
    const string Key="EnvironmentV2Isolated.ContinuationWalk";
    static Transform rig;
    static CharacterController controller;
    static Vector3 originalPosition;
    static Quaternion originalRotation;
    static bool originalEnabled;
    static bool capturedRig;
    static JArray routes;
    static int routeIndex;
    static double started;
    static JObject result;
    static readonly List<object> observations=new List<object>();
    static readonly List<string> failures=new List<string>();
    static EnvironmentV2ContinuationWalk() { EditorApplication.playModeStateChanged+=State; EditorApplication.update+=Tick; }
    static Transform[] All() { return SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray(); }
    static float[] V(Vector3 v) { return new[]{v.x,v.y,v.z}; }
    static Vector3 Vec(JToken t) { if(!(t is JArray a)||a.Count!=3) throw new Exception("Vector must contain exactly three numbers"); var v=new Vector3((float)t[0],(float)t[1],(float)t[2]); if(float.IsNaN(v.sqrMagnitude)||float.IsInfinity(v.sqrMagnitude)) throw new Exception("Vector must be finite"); return v; }
    static Vector3 Foot() { return controller.transform.TransformPoint(controller.center-Vector3.up*controller.height*.5f); }
    static float Horizontal(Vector3 v) { return new Vector2(v.x,v.z).magnitude; }
    public static void Begin() {
        if(Application.dataPath!="D:/VR_TMU/XR-Research-V2/Assets"||SceneManager.GetActiveScene().path!=ScenePath) throw new Exception("Walk audit requires target project and V2 scene");
        if(EditorApplication.isPlayingOrWillChangePlaymode||Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty)) throw new Exception("Walk audit requires saved scenes and stopped Editor; no save/reload performed");
        var plan=JObject.Parse(File.ReadAllText(Out+"walk_plan.json"));
        if(!(plan["routes"] is JArray list)||list.Count==0) throw new Exception("walk_plan.json requires nonempty routes");
        foreach(var r in list) {
            Vec(r["start"]); Vec(r["end"]);
            if((string)r["expected"]!="open"&&(string)r["expected"]!="blocked") throw new Exception("Route expected must be open or blocked");
            if(r["door"]!=null) Vec(r["door"]["localEuler"]);
        }
        SessionState.SetString(Key,"entering"); EditorApplication.isPlaying=true;
    }
    static void State(PlayModeStateChange state) {
        if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetString(Key,"")=="entering") {
            try {
                observations.Clear(); failures.Clear(); result=null; routeIndex=0; capturedRig=false; started=EditorApplication.timeSinceStartup;
                rig=All().Single(t=>t.name=="XR_Origin_Environment_Test");
                controller=rig.GetComponentsInChildren<CharacterController>(true).Single();
                if(!controller.enabled||!controller.gameObject.activeInHierarchy) throw new Exception("Existing CharacterController must be enabled and active");
                if(Vector3.Dot(controller.transform.up,Vector3.up)<.999f) throw new Exception("Existing CharacterController must be upright for world floor-height route coordinates");
                originalPosition=rig.position; originalRotation=rig.rotation; originalEnabled=controller.enabled;
                capturedRig=true;
                routes=(JArray)JObject.Parse(File.ReadAllText(Out+"walk_plan.json"))["routes"];
                SessionState.SetString(Key,"running");
            } catch(Exception e) { Finish(e); }
        }
        if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetString(Key,"")!="") {
            if(result==null&&File.Exists(Out+"walk.json")) result=JObject.Parse(File.ReadAllText(Out+"walk.json"));
            if(result==null) result=new JObject{["status"]="FAIL",["error"]="Play Mode ended before walk audit completed"};
            result["restored_edit_mode"]=!EditorApplication.isPlaying;
            result["restored_scene_path"]=SceneManager.GetActiveScene().path;
            result["restored_scene_dirty"]=SceneManager.GetActiveScene().isDirty;
            if(SceneManager.GetActiveScene().path!=ScenePath||SceneManager.GetActiveScene().isDirty) result["status"]="FAIL";
            File.WriteAllText(Out+"walk.json",result.ToString(Formatting.Indented));
            SessionState.EraseString(Key); Debug.Log("ENVIRONMENT_V2_CONTINUATION_WALK_COMPLETE: "+(string)result["status"]);
        }
    }
    static void Tick() {
        if(!EditorApplication.isPlaying||SessionState.GetString(Key,"")!="running") return;
        try {
            if(routes==null||!rig||!controller) throw new Exception("Domain reload interrupted walk audit");
            if(routeIndex<routes.Count) RunRoute(routes[routeIndex++]);
            if(routeIndex==routes.Count) Finish(null);
        } catch(Exception e) { Finish(e); }
    }
    static bool Ground(out RaycastHit hit) {
        var hits=Physics.RaycastAll(Foot()+Vector3.up*.15f,Vector3.down,2,~0,QueryTriggerInteraction.Ignore)
            .Where(h=>!h.collider.transform.IsChildOf(rig)&&h.normal.y>.5f).OrderBy(h=>h.distance).ToArray();
        hit=hits.Length>0?hits[0]:default(RaycastHit); return hits.Length>0;
    }
    static void RunRoute(JToken route) {
        var start=Vec(route["start"]); var end=Vec(route["end"]);
        string name=(string)route["name"], expected=(string)route["expected"];
        float tolerance=(float?)route["targetToleranceM"]??.15f, shortfall=(float?)route["blockedShortfallM"]??.20f;
        if(tolerance<=0||shortfall<=0) throw new Exception("Route tolerances must be positive");
        float length=Horizontal(end-start);
        if(length<.25f||length>50) throw new Exception("Route horizontal length must be between 0.25 and 50 m");
        Transform door=null; Quaternion doorRotation=Quaternion.identity;
        object doorChange=null;
        if(route["door"]!=null) {
            door=All().Single(t=>t.name==(string)route["door"]["name"]); doorRotation=door.localRotation;
            door.localRotation=Quaternion.Euler(Vec(route["door"]["localEuler"]));
            doorChange=new{door.name,before=V(doorRotation.eulerAngles),during=V(door.localEulerAngles)};
        }
        try {
            controller.enabled=false;
            rig.position+=start+Vector3.up*.02f-Foot();
            controller.enabled=true; Physics.SyncTransforms();
            for(int n=0;n<10;n++) controller.Move(Vector3.down*.01f);
            Physics.SyncTransforms();
            var initial=Foot(); var initialRoot=rig.position;
            var steps=new List<object>(); bool supported=true, aboveFloor=true, sideContact=false;
            float travel=0; var previous=initial;
            int count=Mathf.CeilToInt(length/.05f)+40;
            // Deterministic 0.05 m CharacterController.Move steps in Play Mode; no Scene camera or controller-input claim.
            for(int n=0;n<count;n++) {
                var delta=end-Foot(); delta.y=0;
                var move=Vector3.ClampMagnitude(delta,.05f)+Vector3.down*.01f;
                var flags=controller.Move(move); Physics.SyncTransforms();
                var foot=Foot(); travel+=Horizontal(foot-previous);previous=foot;
                bool ground=Ground(out var hit);
                bool support=ground&&Mathf.Abs(foot.y-hit.point.y)<.35f;
                supported&=support; aboveFloor&=foot.y>=Mathf.Min(start.y,end.y)-.15f;
                sideContact|=(flags&CollisionFlags.Sides)!=0;
                steps.Add(new{step=n,foot=V(foot),root=V(rig.position),move=V(move),flags=flags.ToString(),controller.isGrounded,ground_hit=ground,ground_collider=ground?hit.collider.name:null,ground_point=ground?V(hit.point):null,supported=support,target_error_m=Horizontal(end-foot)});
                if(Horizontal(end-foot)<.025f) break;
            }
            float error=Horizontal(end-Foot());
            bool routePass=expected=="open"?error<=tolerance:error>=shortfall&&sideContact;
            bool pass=routePass&&supported&&aboveFloor;
            if(!pass) failures.Add(name+": expected="+expected+", error_m="+error+", side_contact="+sideContact+", supported="+supported+", above_floor="+aboveFloor);
            observations.Add(new{name,expected,status=pass?"PASS":"FAIL",start_floor=V(start),end_floor=V(end),initial_world_feet=V(initial),final_world_feet=V(Foot()),initial_root=V(initialRoot),final_root=V(rig.position),requested_distance_m=length,actual_horizontal_travel_m=travel,net_horizontal_displacement_m=Horizontal(Foot()-initial),target_error_m=error,tolerance_m=tolerance,blocked_shortfall_m=shortfall,side_contact=sideContact,floor_supported_all_steps=supported,feet_never_below_floor_minus_015=aboveFloor,controller=new{controller.height,controller.radius,center=V(controller.center),controller.skinWidth,controller.stepOffset,controller.slopeLimit,controller.minMoveDistance,lossyScale=V(controller.transform.lossyScale)},door=doorChange,steps});
        } finally { if(door) door.localRotation=doorRotation; Physics.SyncTransforms(); }
    }
    static void Finish(Exception error) {
        if(error!=null) failures.Add(error.ToString());
        if(capturedRig&&rig&&controller) {
            controller.enabled=false;rig.SetPositionAndRotation(originalPosition,originalRotation);controller.enabled=originalEnabled;Physics.SyncTransforms();
        }
        result=JObject.FromObject(new{status=failures.Count==0?"PASS":"FAIL",scope="Unity Editor PlayMode existing XR rig CharacterController.Move collision checks; not Scene camera movement, XR tracked locomotion, hand/controller interaction, or hardware validation",hardware_tested=false,controller_input_tested=false,method="Each route teleports only its start with CC disabled, retains CC dimensions/settings, then calls Move in <=0.05 m horizontal steps with 0.01 m downward displacement; no environment collider disabled. Door poses are temporary in Play Mode and restored per route.",wall_seconds=EditorApplication.timeSinceStartup-started,failures,routes=observations,play_rig_pose_restored=rig?(object)new{position=V(rig.position),rotation=V(rig.eulerAngles)}:null});
        File.WriteAllText(Out+"walk.json",result.ToString(Formatting.Indented));SessionState.SetString(Key,"finished");EditorApplication.isPlaying=false;
    }
}
}
#endif
