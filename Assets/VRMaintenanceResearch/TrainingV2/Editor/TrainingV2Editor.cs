#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRTrainingV2.Editor
{
    [InitializeOnLoad]
    public static partial class TrainingV2Editor
    {
        public const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_Environment_V2.unity";
        public const string Out = "D:/for_model_codex_vr_tmu/outputs/training_v2_20260914_184407";
        const string Request = "D:/VR_TMU/XR-Research-V2/Temp/TrainingV2Command.txt";
        static TrainingV2Editor() { EditorApplication.update += Poll; }
        public static void Write(string name, object data) => File.WriteAllText(Out + "/" + name + ".json", JsonConvert.SerializeObject(data, Formatting.Indented));
        static Transform[] All() => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
            string command;
            try
            {
                command = File.ReadAllText(Request).Trim();
                File.Move(Request, Out + "/command_" + DateTime.UtcNow.Ticks + ".txt");
            }
            catch(IOException e) when ((e.HResult & 0xffff)==32 || (e.HResult & 0xffff)==33)
            {
                // File-system sharing can be transient. Preserve the request; client has a bounded timeout.
                return;
            }
            try
            {
                if (command == "probe") Probe();
                else if (command == "open") { Guard(false); EditorSceneManager.OpenScene(ScenePath); Probe(); }
                else if (command == "refresh") AssetDatabase.Refresh();
                else RunCommand(command);
                File.WriteAllText(Out + "/last_command.txt", command + ": PASS");
            }
            catch (Exception e) { Write("error_" + command, new { error = e.ToString() }); File.WriteAllText(Out + "/last_command.txt", command + ": FAIL"); Debug.LogException(e); }
        }
        [MenuItem("Tools/VR Training V2/Play with Editor Simulator")]
        static void PlayWithEditorSimulator() { RunCommand("play"); }
        static partial void RunCommand(string command);
        public static void Guard(bool requireV2 = true)
        {
            if (Application.dataPath != "D:/VR_TMU/XR-Research-V2/Assets" || EditorApplication.isPlaying) throw new Exception("Wrong project or Play Mode");
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new Exception("Unsaved scene preserved; stop before write");
            if (requireV2 && SceneManager.GetActiveScene().path != ScenePath) throw new Exception("V2 scene not active");
        }
        public static void Probe()
        {
            var ts = All();
            Write("live_probe", new { version = Application.unityVersion, path = Application.dataPath, playing = EditorApplication.isPlaying,
                scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => {var s = SceneManager.GetSceneAt(i); return new {s.path,s.isDirty};}).ToArray(),
                objects = ts.Select(t => new {t.name, parent=t.parent?t.parent.name:null, active=t.gameObject.activeSelf,
                    position=new[]{t.position.x,t.position.y,t.position.z},rotation=new[]{t.eulerAngles.x,t.eulerAngles.y,t.eulerAngles.z},
                    components=t.GetComponents<Component>().Select(c=>c?c.GetType().FullName:"MISSING").ToArray()}).ToArray(),
                detailed = ts.SelectMany(t=>t.GetComponents<Component>()).Where(c=>c && (c.GetType().Name.Contains("Interactor") || c.GetType().Name.Contains("Input") || c.GetType().Name.Contains("Session") || c.GetType().Name.Contains("TrackedPose") || c.GetType().Name.Contains("Modality"))).Select(c=>new {c.name,type=c.GetType().FullName,json=EditorJsonUtility.ToJson(c)}).ToArray()
            });
        }
    }
}
#endif
