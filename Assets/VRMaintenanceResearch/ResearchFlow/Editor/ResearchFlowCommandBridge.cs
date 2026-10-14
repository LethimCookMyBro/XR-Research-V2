using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Editor-only automation entry for when no MCP bridge is connected. Mirrors the HandsLab file-command pattern.
    // Temp/ResearchFlowCommand.json: {"id":"x","invoke":"Namespace.Type.Member","args":[...]} | {"id":"x","menu":"Path"} | {"id":"x","play":true}
    [InitializeOnLoad]
    static class ResearchFlowCommandBridge
    {
        const string Request = "Temp/ResearchFlowCommand.json";
        static ResearchFlowCommandBridge() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
            string raw;
            try { raw = File.ReadAllText(Request); File.Delete(Request); }
            catch (IOException) { return; }
            string id = DateTime.UtcNow.Ticks.ToString();
            object result; bool ok = true;
            // Never serialize Unity structs: Newtonsoft recurses forever through Vector3.normalized / Color.linear.
            try { var request = JObject.Parse(raw); id = (string)request["id"] ?? id; result = Run(request); if (result != null && !(result is string) && !result.GetType().IsPrimitive) result = result.ToString(); }
            catch (Exception e) { ok = false; result = (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e).ToString(); }
            string output = Path.GetFullPath("Logs/ResearchFlowEvidence/bridge");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "response_" + id + ".json"), JsonConvert.SerializeObject(
                new { id, ok, result, playing = EditorApplication.isPlaying, utc = DateTime.UtcNow.ToString("O") }, Formatting.Indented,
                new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MaxDepth = 8 }));
        }

        // Loaded scenes and dirty flags: check before Play/build (a dirty ResearchFlow scene blocks the build).
        static string SceneState() => string.Join("; ", Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
            .Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i)).Select(s => s.path + (s.isDirty ? " DIRTY" : " clean")));

        static object Run(JObject request)
        {
            if (request["menu"] != null) return EditorApplication.ExecuteMenuItem((string)request["menu"]);
            if (request["play"] != null) { EditorApplication.isPlaying = (bool)request["play"]; return "requested"; }
            if (request["refresh"] != null) { AssetDatabase.Refresh(); return "refreshed"; }
            string name = (string)request["invoke"];
            int dot = name.LastIndexOf('.');
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name.Substring(0, dot))).FirstOrDefault(t => t != null)
                ?? throw new ArgumentException("Type not found: " + name.Substring(0, dot));
            string member = name.Substring(dot + 1);
            var args = (request["args"] as JArray)?.ToArray() ?? Array.Empty<JToken>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var method = type.GetMethods(flags).FirstOrDefault(m => m.Name == member && m.GetParameters().Length >= args.Length
                && m.GetParameters().Skip(args.Length).All(p => p.HasDefaultValue));
            if (method == null) return (type.GetProperty(member, flags) ?? throw new MissingMemberException(name)).GetValue(null);
            var parameters = method.GetParameters();
            return method.Invoke(null, parameters.Select((p, i) => i < args.Length ? args[i].ToObject(p.ParameterType) : p.DefaultValue).ToArray());
        }
    }
}
