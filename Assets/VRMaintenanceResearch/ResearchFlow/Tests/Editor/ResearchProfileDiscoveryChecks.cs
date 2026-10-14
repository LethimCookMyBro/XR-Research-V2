#if UNITY_EDITOR
namespace VRMaintenanceResearch.ResearchFlow.Editor {
using System; using System.IO; using System.Linq; using UnityEngine; using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine.SceneManagement;
public static class ResearchProfileDiscoveryChecks {
static Type T(string name) {return AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("VRMaintenanceResearch.ResearchFlow."+name)).First(x=>x!=null);}
static object P(object value,string name) {return value?.GetType().GetProperty(name)?.GetValue(value);}
static object Request(string group) {var jt=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Newtonsoft.Json.Linq.JObject")).First(x=>x!=null);return jt.GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{"{\"group\":\""+group+"\"}"});}
static string Json(object value) {var jt=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Newtonsoft.Json.JsonConvert")).First(x=>x!=null);return (string)jt.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value});}
static string Hash(string path) {using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
public static string Run(string root) {
Directory.CreateDirectory(root);
var preview=EditorSceneManager.NewPreviewScene();
var go=new GameObject("TEST export discovery fixture") {hideFlags=HideFlags.HideAndDontSave}; go.SetActive(false); SceneManager.MoveGameObjectToScene(go,preview);
try {
var registry=Activator.CreateInstance(T("ResearchParticipantRegistry"),new object[]{Path.Combine(root,"registry")});
var group=Enum.Parse(T("ParticipantGroup"),"Dev");
var saved=((System.Collections.IEnumerable)T("ResearchParticipantRegistry").GetMethod("List").Invoke(registry,new[]{group})).Cast<object>().ToArray();
var created=saved.Length==0 ? T("ResearchParticipantRegistry").GetMethod("CreateNext").Invoke(registry,new object[]{group}) : saved.Single();
string id=(string)created.GetType().GetField("participant_id").GetValue(created);
var record=T("ResearchParticipantRegistry").GetMethod("StartOrResume").Invoke(registry,new object[]{id,Enum.Parse(T("ParticipantLanguage"),"English"),Enum.Parse(T("ExperimentInputMode"),"Controller"),null,false});
var controller=go.AddComponent(T("ResearchSessionFlowController")); T("ResearchSessionFlowController").GetProperty("Registry").SetValue(controller,registry);
var channel=go.AddComponent(T("ResearchAdbControlChannel")); var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
T("ResearchAdbControlChannel").GetField("controller",flags).SetValue(channel,controller);
var before=Directory.GetFiles(Path.Combine(root,"registry"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Hash(p));
var method=T("ResearchAdbControlChannel").GetMethod("List",flags);
var response=method.Invoke(channel,new[]{Request("Dev")});
var row=((System.Collections.IEnumerable)P(P(response,"data"),"participants")).Cast<object>().First();
string session=(string)record.GetType().GetField("session_id").GetValue(record), run=(string)record.GetType().GetField("run_id").GetValue(record);
bool tuple=(string)P(row,"participant_id")==id && (string)P(row,"session_id")==session && (string)P(row,"run_id")==run && !string.IsNullOrEmpty(session) && !string.IsNullOrEmpty(run);
var denied=method.Invoke(channel,new[]{Request("Thai")});
var after=Directory.GetFiles(Path.Combine(root,"registry"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Hash(p));
bool unchanged=before.Count==after.Count&&before.All(kv=>after.TryGetValue(kv.Key,out var v)&&v==kv.Value);
var evidence=new {fixture_only=true,hardware_verified=false,tuple_pass=tuple,study_group_refused=(bool)P(denied,"ok")==false,registry_unchanged=unchanged,response};
File.WriteAllText(Path.Combine(root,"discovery.json"),Json(evidence));
return "DISCOVERY "+(tuple && (bool)P(denied,"ok")==false && unchanged ? "PASS" : "FAIL")+" tuple="+tuple+" studyRefused="+((bool)P(denied,"ok")==false)+" registryUnchanged="+unchanged+"; "+root;
} finally {UnityEngine.Object.DestroyImmediate(go);EditorSceneManager.ClosePreviewScene(preview);}
}}

}
#endif
