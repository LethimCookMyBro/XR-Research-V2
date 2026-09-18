#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.SceneManagement;
namespace VRMaintenanceResearch.HandsLabQuest3.Editor { public static partial class HandsLabEditor {
const string RegressionActive="Temp/HandsLabRegressionActive.txt";
[InitializeOnLoadMethod] static void RegisterRegressionCapture(){Application.logMessageReceived-=RegressionMessage;Application.logMessageReceived+=RegressionMessage;}
static void RegressionMessage(string message,string stack,LogType type){if(type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)return;if(!File.Exists(RegressionActive))return;var name=File.ReadAllText(RegressionActive);if(name!="v2"&&name!="master")return;File.AppendAllText(Out+"/regression_"+name+"_errors.jsonl",Newtonsoft.Json.JsonConvert.SerializeObject(new{type=type.ToString(),message,stack,playing=EditorApplication.isPlaying})+"\n");}
static void RegressionCommand(string command){
 if(command.StartsWith("regression_open_")){Guard();var name=command.Substring("regression_open_".Length);string path=name=="v2"?"Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_Environment_V2.unity":name=="master"?"Assets/VRMaintenanceResearch/Scenes/VRResearch_Master.unity":throw new ArgumentException(name);File.WriteAllText(Out+"/regression_"+name+"_errors.jsonl","");File.WriteAllText(RegressionActive,name);EditorSceneManager.OpenScene(path);Write("regression_"+name+"_open",new{path,loaded=SceneManager.GetActiveScene().isLoaded,dirty=SceneManager.GetActiveScene().isDirty,missing=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))});}
 else if(command=="regression_sample"){var name=File.ReadAllText(RegressionActive);Write("regression_"+name+"_runtime",new{scene=SceneManager.GetActiveScene().path,playing=EditorApplication.isPlaying,frame=Time.frameCount,roots=SceneManager.GetActiveScene().rootCount,origins=UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length,cameras=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length,errors=File.ReadAllLines(Out+"/regression_"+name+"_errors.jsonl").Length});}
 else if(command=="regression_end"){Guard();if(File.Exists(RegressionActive))File.Move(RegressionActive,Out+"/regression_finished_"+DateTime.UtcNow.Ticks+".txt");}
 else throw new ArgumentException(command);
}
}}
#endif
