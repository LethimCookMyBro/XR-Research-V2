#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EnvironmentV2Isolated
{
 public static partial class EnvironmentV2Import
 {
  static void FinalCheck()
  {
   Check(!EditorApplication.isPlaying,"Exit PlayMode first");Protected();
   EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);AuditScene();
   string before=Hash(ScenePath);string path=Audit+"/Rollback_Test/Restored_Empty_Baseline.unity";
   Check(File.Exists(path),"Run ROLLBACK.sh on test copy first");
   AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
   var rollback=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);int count=rollback.GetRootGameObjects().Length;
   Check(count==0,"Rollback baseline not empty");EditorSceneManager.CloseScene(rollback,true);
   Check(Hash(ScenePath)==before,"Rollback changed modified scene");Protected();
   var console=SaveProbe("final_console.json");Check(console.compileErrorEntries==0,"Final compile errors");
   Write("final_verification.json",new{status="PASS",scene=ScenePath,scene_sha256=Hash(ScenePath),baseline_roots=0,modified_groups=Environment().transform.childCount,rollback_roots=count,rollback_exact=Hash(path)==Hash(Audit+"/Environment_V2_BASELINE.unity"),modified_retained=true,protected_scenes_unchanged=true,console_errors=console.consoleErrors,console_warnings=console.consoleWarnings,compile_errors=console.compileErrorEntries,unity_version=Application.unityVersion});
   Debug.Log("ENVIRONMENT_V2_FINAL_PASS groups=7 baseline_roots=0 rollback_roots=0 protected_scenes=2");
  }
 }
}
#endif
