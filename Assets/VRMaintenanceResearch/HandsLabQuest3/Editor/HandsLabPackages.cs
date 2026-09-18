#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
namespace VRMaintenanceResearch.HandsLabQuest3.Editor {
public static partial class HandsLabEditor {
 static AddAndRemoveRequest packages;
 static double packageDeadline;
 static partial void Execute(string command){
 if(command=="packages"){Guard();packages=Client.AddAndRemove(new[]{"com.meta.xr.sdk.interaction@205.0.0","com.unity.xr.hands@1.9.0"});packageDeadline=EditorApplication.timeSinceStartup+600;EditorApplication.update+=PollPackages;}
 else RunLabCommand(command);
 }
 static partial void RunLabCommand(string command);
 static void PollPackages(){if(packages==null)return;if(!packages.IsCompleted){if(EditorApplication.timeSinceStartup>packageDeadline){Write("packages_result",new{passed=false,error="UPM timeout"});EditorApplication.update-=PollPackages;}return;}EditorApplication.update-=PollPackages;Write("packages_result",new{passed=packages.Status==StatusCode.Success,error=packages.Error?.message,packages=packages.Status==StatusCode.Success?packages.Result.Select(p=>p.name+"@"+p.version).ToArray():null});packages=null;}
}}
#endif
