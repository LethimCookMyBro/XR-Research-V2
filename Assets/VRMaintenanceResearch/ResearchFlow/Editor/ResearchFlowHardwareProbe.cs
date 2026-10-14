#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>
    /// Read-only hardware/editor state probe for the Quest Link validation pass.
    /// Diagnostics only: it never starts, resumes or mutates a session, and never
    /// touches PlayerSettings. Session control stays in the existing Researcher Console.
    /// </summary>
    public static class ResearchFlowHardwareProbe
    {
        const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";

        static string Out => Directory.CreateDirectory(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "QuestFinalQA", "device"))).FullName;

        [MenuItem("VR Research/Research Flow/Probe Hardware State")]
        public static void Probe()
        {
            var active = SceneManager.GetActiveScene();
            var dirty = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isDirty) dirty.Add(s.path);
            }

            var devices = new List<InputDevice>();
            InputDevices.GetDevices(devices);

            var loader = XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null
                ? XRGeneralSettings.Instance.Manager.activeLoader
                : null;

            var controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            var ports = UnityEngine.Object.FindObjectsByType<ResearchTaskPort>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            var report = new
            {
                capturedUtc = System.DateTime.UtcNow.ToString("O"),
                editor = new
                {
                    unityVersion = Application.unityVersion,
                    isPlaying = EditorApplication.isPlaying,
                    isCompiling = EditorApplication.isCompiling,
                    isUpdating = EditorApplication.isUpdating,
                    activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString()
                },
                scene = new
                {
                    activePath = active.path,
                    isExpectedScene = active.path == ScenePath,
                    loadedSceneCount = SceneManager.sceneCount,
                    dirtyScenes = dirty.ToArray(),
                    clean = dirty.Count == 0
                },
                xr = new
                {
                    activeLoader = loader != null ? loader.name : null,
                    xrDeviceActive = XRSettings.isDeviceActive,
                    loadedDeviceName = XRSettings.loadedDeviceName,
                    eyeTextureWidth = XRSettings.eyeTextureWidth,
                    eyeTextureHeight = XRSettings.eyeTextureHeight,
                    openXrRuntimeName = UnityEngine.XR.OpenXR.OpenXRRuntime.name,
                    openXrRuntimeVersion = UnityEngine.XR.OpenXR.OpenXRRuntime.version.ToString(),
                    displayCount = Display.displays.Length
                },
                inputDevices = devices.Select(d => new
                {
                    d.name,
                    d.manufacturer,
                    characteristics = d.characteristics.ToString(),
                    d.isValid
                }).ToArray(),
                session = new
                {
                    controllerPresent = controller != null,
                    flowState = controller != null && controller.Flow != null ? controller.Flow.State.ToString() : "(no flow)",
                    participant = controller != null && controller.Record != null ? controller.Record.participant_id : null,
                    testData = controller != null && controller.Record != null ? (bool?)controller.Record.test_data : null,
                    taskPorts = ports.Select(p => new { p.taskId, p.demoMode, p.contentApproved }).ToArray()
                }
            };

            string path = Path.Combine(Out, "hardware-probe.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));
            Debug.Log("RESEARCHFLOW_HARDWARE_PROBE " + path + "\n" + JsonConvert.SerializeObject(report, Formatting.Indented));
        }
    }
}
#endif
