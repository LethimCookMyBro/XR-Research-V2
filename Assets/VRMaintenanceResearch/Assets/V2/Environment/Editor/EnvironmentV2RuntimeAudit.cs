#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;
namespace EnvironmentV2Isolated
{
 public static partial class EnvironmentV2Import
 {
  const string RuntimeKey="EnvironmentV2Isolated.Runtime";
  static double runtimeStart;
  static int initialCount;
  static Vector3[] initialParts;
  static bool countStable=true;
  static Quaternion[] initialJoints; static float maxPartMotion, maxRobotAngle;
  static Transform[] Joints()=>Environment().GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("_Robot_J")).OrderBy(t=>t.name).ToArray();
  static void BeginRuntime(){TargetScene();Protected();EditorSceneManager.SaveScene(TargetScene());SessionState.SetString(RuntimeKey,"entering");EditorApplication.isPlaying=true;}
  static void OnPlayState(PlayModeStateChange state)
  {
   if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetString(RuntimeKey,"")=="entering")
   {
    Application.runInBackground=true;runtimeStart=EditorApplication.timeSinceStartup;initialCount=Environment().GetComponentsInChildren<Transform>(true).Length;
    initialParts=Parts().Select(t=>t.position).ToArray();initialJoints=Joints().Select(t=>t.localRotation).ToArray();maxPartMotion=0;maxRobotAngle=0;countStable=true;SessionState.SetString(RuntimeKey,"running");
   }
   if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetString(RuntimeKey,"")=="finished")
   {SessionState.EraseString(RuntimeKey);SaveProbe("after_playmode_console.json");Protected();Debug.Log("ENVIRONMENT_V2_PLAYMODE_COMPLETE");}
  }
  static Transform[] Parts()=>Environment().GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("R03_A_Part_")||t.name.StartsWith("R03_B_Part_")).OrderBy(t=>t.name).ToArray();
  static void RuntimeTick()
  {
   if(!EditorApplication.isPlaying||SessionState.GetString(RuntimeKey,"")!="running")return;
   try
   {
    var animator=FactoryAnimator();countStable&=Environment().GetComponentsInChildren<Transform>(true).Length==initialCount&&Parts().Length==8;
    maxPartMotion=Mathf.Max(maxPartMotion,Parts().Select((t,i)=>Vector3.Distance(t.position,initialParts[i])).Max());
    maxRobotAngle=Mathf.Max(maxRobotAngle,Joints().Select((t,i)=>Quaternion.Angle(t.localRotation,initialJoints[i])).Max());
    double elapsed=EditorApplication.timeSinceStartup-runtimeStart;var info=animator.GetCurrentAnimatorStateInfo(0);
    if(info.normalizedTime<1.04f&&elapsed<150)return;
    float moved=Parts().Select((t,i)=>Vector3.Distance(t.position,initialParts[i])).Max();
    bool naturalLoop=info.normalizedTime>=1.04f;float naturalNormalized=info.normalizedTime;
    int hash=info.fullPathHash;var tracked=animator.GetComponentsInChildren<Transform>(true);
    animator.Play(hash,0,0);animator.Update(0);var first=tracked.Select(t=>t.localToWorldMatrix).ToArray();
    animator.Play(hash,0,1);animator.Update(0);float seam=0;for(int i=0;i<tracked.Length;i++)for(int r=0;r<4;r++)for(int c=0;c<4;c++)seam=Mathf.Max(seam,Mathf.Abs(tracked[i].localToWorldMatrix[r,c]-first[i][r,c]));
    animator.Play(hash,0,2);animator.Update(0);countStable&=Environment().GetComponentsInChildren<Transform>(true).Length==initialCount;
    animator.Play(hash,0,(48-.01f)/48);animator.Update(0);var before=Parts().Select(t=>t.position).ToArray();
    animator.Play(hash,0,(48+.01f)/48);animator.Update(0);float near=Parts().Select((t,i)=>Vector3.Distance(t.position,before[i])).Max();
    var xr=UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<XROrigin>(true)).Single();
    var tests=new List<object>();var xrPasses=new List<bool>();var names=new[]{"05_Training_Participant","03_Neutral_Corridor","06_Computer_Wide","08_Fan_Wide"};
    foreach(var v in Source["views"].Where(v=>names.Contains((string)v["name"])))
    {
     xr.MatchOriginUpCameraForward(Vector3.up,Vec(v["forward"]));xr.MoveCameraToWorldLocation(Vec(v["position"]));Physics.SyncTransforms();
     var eye=xr.Camera.transform.position;var floorColliders=Environment().GetComponentsInChildren<Collider>().Where(c=>c.name.Contains("Floor")||c.name=="Factory_Slab");float distance=float.PositiveInfinity;
     foreach(var floor in floorColliders)if(floor.Raycast(new Ray(eye,Vector3.down),out var hit,5))distance=Mathf.Min(distance,hit.distance);
     if(float.IsNaN(distance)||float.IsInfinity(distance)){xrPasses.Add(false);tests.Add(new{view=(string)v["name"],pass=false,reason="No floor ray hit"});continue;}
     float floorY=eye.y-distance;xr.MoveCameraToWorldLocation(new Vector3(eye.x,floorY+1.65f,eye.z));Physics.SyncTransforms();eye=xr.Camera.transform.position;distance=eye.y-floorY;
     var foot=eye-Vector3.up*distance;var blockers=Physics.OverlapCapsule(foot+Vector3.up*.23f,foot+Vector3.up*1.5f,.2f,~0,QueryTriggerInteraction.Ignore).Where(c=>!c.transform.IsChildOf(xr.transform)&&!c.name.Contains("Floor")&&c.name!="Factory_Slab").Select(c=>c.name).ToArray();
     xrPasses.Add(Mathf.Abs(distance-1.65f)<.03f&&blockers.Length==0);
     tests.Add(new{view=(string)v["name"],camera_world=V(eye),eye_height_above_floor=distance,major_boundary_capsule_blockers=blockers,pass=Mathf.Abs(distance-1.65f)<.03f&&blockers.Length==0});
    }
    bool pass=naturalLoop&&maxPartMotion>.001f&&maxRobotAngle>1f&&countStable&&seam<.003f&&near<.05f&&xrPasses.Count==4&&xrPasses.All(p=>p);
    Write("playmode_verification.json",new{status=pass?"PASS":"INCOMPLETE_OR_FAIL",duration_seconds=FactoryClip().length,wall_observation_seconds=elapsed,natural_normalized_time=naturalNormalized,natural_loop_observed=naturalLoop,workpiece_motion_max_m=maxPartMotion,robot_joint_motion_max_deg=maxRobotAngle,workpiece_start_to_final_m=moved,xr_all_positions_pass=xrPasses.All(p=>p),robot_roots=2,persistent_parts=Parts().Select(t=>t.name).ToArray(),environment_object_count=initialCount,object_count_stable=countStable,loop_endpoint_matrix_error=seam,near_seam_part_displacement_over_20ms_m=near,xr_positions=tests,hardware_used=false,scope="Editor PlayMode natural playback plus normalized-time endpoint/second-loop samples; no headset ergonomics or Quest profiling"});
    SessionState.SetString(RuntimeKey,"finished");EditorApplication.isPlaying=false;
   }
   catch(Exception e){Write("playmode_failure.json",new{error=e.ToString()});SessionState.SetString(RuntimeKey,"finished");EditorApplication.isPlaying=false;Debug.LogException(e);}
  }
 }
}
#endif
