#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EnvironmentV2Isolated
{
    [InitializeOnLoad]
    public static partial class EnvironmentV2Import
    {
        const string Root = "D:/VR_TMU/XR-Research-V2";
        const string Audit = "Assets/VRMaintenanceResearch/AuditOutputs/V2";
        static string OutputAudit => SessionState.GetString("EnvironmentV2Isolated.OutputAudit", Audit);
        const string Request = "Temp/EnvironmentV2Preflight/request.txt";
        [Serializable] public class Entry { public string message; public int mode; }
        [Serializable] public class Probe
        {
            public string projectRoot, unityVersion, activeScene, utc, status;
            public bool isCompiling, isUpdating, isPlaying;
            public int consoleErrors, consoleWarnings, consoleLogs, compileErrorEntries;
            public List<Entry> entries = new List<Entry>();
        }
        static EnvironmentV2Import() { EditorApplication.update += Poll; EditorApplication.update += RuntimeTick; EditorApplication.playModeStateChanged += OnPlayState; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
            string request = File.ReadAllText(Request).Trim();
            File.Move(Request, Request + ".consumed." + DateTime.UtcNow.Ticks);
            try
            {
                if (Path.GetFullPath(Application.dataPath + "/..").Replace('\\','/').TrimEnd('/') != Root)
                    throw new InvalidOperationException("Wrong project root; no scene action performed.");
                if (request.StartsWith("visual_")) VisualCommand(request);
                else if (request == "preflight") SaveProbe("preflight_editor.json");
                else if (request == "build") Build();
                else if(request == "audit") AuditScene();
                else if(request == "render") CaptureViews();
                else if(request == "play") BeginRuntime();
                else if(request == "verify_final") FinalCheck();
                else throw new InvalidOperationException("Unknown request: " + request);
            }
            catch (Exception e) { File.WriteAllText(OutputAudit + "/helper_error.txt", e.ToString()); Debug.LogException(e); }
        }
        static Probe SaveProbe(string filename)
        {
            var p = new Probe { projectRoot=Path.GetFullPath(Application.dataPath + "/.."), unityVersion=Application.unityVersion,
                activeScene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, utc=DateTime.UtcNow.ToString("o"),
                isCompiling=EditorApplication.isCompiling,isUpdating=EditorApplication.isUpdating,isPlaying=EditorApplication.isPlaying };
            var asm=typeof(Editor).Assembly;
            var logs=asm.GetType("UnityEditor.LogEntries",true); var entryType=asm.GetType("UnityEditor.LogEntry",true);
            var flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
            object[] counts={0,0,0}; logs.GetMethod("GetCountsByType",flags).Invoke(null,counts);
            p.consoleErrors=(int)counts[0]; p.consoleWarnings=(int)counts[1]; p.consoleLogs=(int)counts[2];
            int count=(int)logs.GetMethod("GetCount",flags).Invoke(null,null);
            logs.GetMethod("StartGettingEntries",flags).Invoke(null,null);
            try
            {
                var fieldFlags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
                var textField=entryType.GetField("message",fieldFlags) ?? entryType.GetField("condition",fieldFlags);
                var modeField=entryType.GetField("mode",fieldFlags);
                if(textField==null||modeField==null)throw new InvalidOperationException("Console reflection fields unavailable");
                for(int i=0;i<count;i++)
                {
                    object entry=Activator.CreateInstance(entryType); logs.GetMethod("GetEntryInternal",flags).Invoke(null,new object[]{i,entry});
                    string message=(string)textField.GetValue(entry) ?? "";
                    p.entries.Add(new Entry{message=message,mode=Convert.ToInt32(modeField.GetValue(entry))});
                    if(Regex.IsMatch(message,@"error CS\d+|Scripts have compiler errors|Compilation failed"))p.compileErrorEntries++;
                }
            }
            finally { logs.GetMethod("EndGettingEntries",flags).Invoke(null,null); }
            p.status=p.compileErrorEntries==0&&!p.isCompiling?"COMPILE_GATE_PASS":"STOP_PREEXISTING_COMPILE_ERRORS";
            File.WriteAllText(OutputAudit+"/"+filename,JsonUtility.ToJson(p,true));
            return p;
        }
    }
}
#endif
