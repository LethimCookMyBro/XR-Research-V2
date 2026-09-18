#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace VRMaintenanceResearch.HandsLabQuest3.Editor {
[InitializeOnLoad] public static partial class HandsLabEditor {
 public const string Root="Assets/VRMaintenanceResearch/HandsLabQuest3";
 public const string ScenePath="Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_HandsLab_Quest3.unity";
 public const string Out="D:/for_model_codex_vr_tmu/outputs/handslab_quest3_20260914_212949";
 const string Request="D:/VR_TMU/XR-Research-V2/Temp/HandsLabCommand.json";
 static HandsLabEditor(){EditorApplication.update+=Poll;}
 public static void Write(string name,object value){File.WriteAllText(Out+"/"+name+".json",JsonConvert.SerializeObject(value,Formatting.Indented));}
 public static void Guard(){if(Application.dataPath!="D:/VR_TMU/XR-Research-V2/Assets" || EditorApplication.isPlaying)throw new Exception("Wrong project or Play Mode");for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new Exception("Unsaved scene preserved");}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Request))return;string raw;try{raw=File.ReadAllText(Request);File.Move(Request,Out+"/command_"+DateTime.UtcNow.Ticks+".json");}catch(IOException){return;}dynamic r=JsonConvert.DeserializeObject(raw);string id=r.id,cmd=r.command;try{if(cmd=="probe")Probe();else if(cmd=="refresh")AssetDatabase.Refresh();else Execute(cmd);Write("response_"+id,new{command=cmd,passed=true});}catch(Exception ex){Write("response_"+id,new{command=cmd,passed=false,error=ex.ToString()});Debug.LogException(ex);}}
 static partial void Execute(string command);
 public static void Probe(){Write("live_probe",new{version=Application.unityVersion,playing=EditorApplication.isPlaying,activeBuildTarget=EditorUserBuildSettings.activeBuildTarget.ToString(),scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty};}),dirtyAssets=Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Where(o=>EditorUtility.IsPersistent(o)&&EditorUtility.IsDirty(o)).Select(o=>new{o.name,path=AssetDatabase.GetAssetPath(o)}).ToArray(),packages=UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Select(p=>new{p.name,p.version,p.resolvedPath}).ToArray()});}
}}
#endif
