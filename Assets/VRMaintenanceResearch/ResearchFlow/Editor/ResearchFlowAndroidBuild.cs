#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using Unity.XR.CoreUtils;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>
    /// Quest APK build for the ResearchFlow integration scene.
    /// Mirrors the proven HandsLabAndroidBuild pattern: snapshot every persistent
    /// PlayerSettings/OpenXR value, apply build-specific values, build, then restore
    /// in finally and record whether restoration actually succeeded.
    /// The scene is never mutated: executionEnvironment is derived at runtime by
    /// ResearchSessionFlowController (Application.isEditor ? "Editor" : "QuestStandalone").
    /// </summary>
    public static class ResearchFlowAndroidBuild
    {
        public const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";

        // Deliberately a technical identity. Never reuse a production/study package id.
        public const string TechnicalApplicationId = "th.ac.tmu.vr.researchflow.quest3.technical";
        const string ProfileManifest = "Assets/Plugins/Android/QuestProfile.androidlib/AndroidManifest.xml";

        static string Out => Directory.CreateDirectory(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "QuestFinalQA", "build"))).FullName;

        static void Write(string name, object value) =>
            File.WriteAllText(Path.Combine(Out, name + ".json"), JsonConvert.SerializeObject(value, Formatting.Indented));

        static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        [MenuItem("VR Research/Research Flow/Build Quest APK")]
        public static void BuildApk()
        {
            if (EditorApplication.isPlaying) throw new Exception("Exit Play Mode before building");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new Exception("Editor is busy compiling/updating");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new Exception("ResearchFlow build requires the active build target to already be Android");

            // Preserve unsaved work rather than silently saving it.
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new Exception("Unsaved scene changes present; save or revert before building");

            if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            SceneChecks();

            var target = NamedBuildTarget.Android;

            // ---- snapshot -------------------------------------------------
            string oldId = PlayerSettings.GetApplicationIdentifier(target);
            string oldProduct = PlayerSettings.productName;
            var oldSdk = PlayerSettings.Android.targetSdkVersion;
            var oldArch = PlayerSettings.Android.targetArchitectures;
            bool oldKeystore = PlayerSettings.Android.useCustomKeystore;
            bool oldBundle = EditorUserBuildSettings.buildAppBundle;
            bool oldExport = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            var oldPreloadedAssets = PlayerSettings.GetPreloadedAssets();
            var oldBackend = PlayerSettings.GetScriptingBackend(target);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var features = settings.GetFeatures<OpenXRFeature>();
            var savedFeatures = features.ToDictionary(f => f, f => f.enabled);
            byte[] oldProfileManifest = File.ReadAllBytes(ProfileManifest);

            string apk = Path.Combine(Out, "ResearchFlowQuest3_" +
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".apk");

            try
            {
                PlayerSettings.productName = "ResearchFlow Quest3 Technical";
                PlayerSettings.SetApplicationIdentifier(target, TechnicalApplicationId);
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                // The provider is not declared in normal/study builds. Runtime also enforces exact technical identity and shell/app UID.
                File.WriteAllText(ProfileManifest, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"th.ac.tmu.vr.researchflow.transport\"><application>" +
                    "<provider android:name=\"th.ac.tmu.vr.researchflow.transport.QuestProfileProvider\" android:authorities=\"" + TechnicalApplicationId +
                    ".quest-profile\" android:exported=\"true\" android:grantUriPermissions=\"false\" /></application></manifest>\n");
                AssetDatabase.ImportAsset(ProfileManifest, ImportAssetOptions.ForceSynchronousImport);

                // Same OpenXR feature set the HandsLab hardware pass validated on this device.
                string[] required = { "HandTracking", "MetaHandTrackingAim", "OculusTouchControllerProfile",
                                      "MetaQuestTouchPlusControllerProfile", "MetaQuestFeature" };
                foreach (string type in required)
                {
                    var feature = features.FirstOrDefault(f => f.GetType().Name == type);
                    if (!feature) throw new Exception("Required OpenXR feature not found: " + type);
                    feature.enabled = true;
                }
                // ISDK binds <XRController>; these profiles would shadow real XRHandSubsystem hands.
                foreach (var f in features.Where(f => f is UnityEngine.XR.OpenXR.Features.Interactions.HandInteractionProfile
                                                   || f is UnityEngine.XR.OpenXR.Features.Interactions.MicrosoftHandInteraction))
                    f.enabled = false;

                Write("build_configuration", new
                {
                    applicationIdentifier = TechnicalApplicationId,
                    productName = PlayerSettings.productName,
                    scene = ScenePath,
                    development = true,
                    backend = "IL2CPP",
                    architecture = PlayerSettings.Android.targetArchitectures.ToString(),
                    targetSdk = 35,
                    unityVersion = Application.unityVersion,
                    graphicsAPIs = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Select(v => v.ToString()).ToArray(),
                    openXrFeatures = features.Where(f => f.enabled).Select(f => f.GetType().FullName).ToArray(),
                    output = apk,
                    hardwareVerified = false,
                    realTaskMechanics = "demo/technical fixture only (contentApproved=false, demoMode=true)"
                });

                ResearchBuildIntegrityChecks.RequireLocalServicesDisabled();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = apk,
                    target = BuildTarget.Android,
                    options = BuildOptions.Development
                });

                bool ok = report.summary.result == BuildResult.Succeeded;
                Write("build_result", new
                {
                    passed = ok,
                    result = report.summary.result.ToString(),
                    report.summary.totalErrors,
                    report.summary.totalWarnings,
                    totalSeconds = report.summary.totalTime.TotalSeconds,
                    report.summary.totalSize,
                    output = apk,
                    sha256 = ok && File.Exists(apk) ? Sha256(apk) : null,
                    sizeBytes = ok && File.Exists(apk) ? new FileInfo(apk).Length : 0,
                    errors = report.steps.SelectMany(s => s.messages)
                        .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                        .Select(m => m.content).Take(40).ToArray()
                });

                if (!ok) throw new Exception("ResearchFlow Android build failed; see Logs/QuestFinalQA/build/build_result.json");
                ResearchBuildIntegrityChecks.RequireLocalServicesDisabled();
                Debug.Log("RESEARCHFLOW_APK_BUILT " + apk);
            }
            finally
            {
                File.WriteAllBytes(ProfileManifest, oldProfileManifest);
                AssetDatabase.ImportAsset(ProfileManifest, ImportAssetOptions.ForceSynchronousImport);
                // ---- restore ----------------------------------------------
                PlayerSettings.productName = oldProduct;
                PlayerSettings.SetApplicationIdentifier(target, oldId);
                PlayerSettings.Android.targetSdkVersion = oldSdk;
                PlayerSettings.Android.targetArchitectures = oldArch;
                PlayerSettings.Android.useCustomKeystore = oldKeystore;
                PlayerSettings.SetScriptingBackend(target, oldBackend);
                EditorUserBuildSettings.buildAppBundle = oldBundle;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = oldExport;
                PlayerSettings.SetPreloadedAssets(oldPreloadedAssets);
                foreach (var pair in savedFeatures) pair.Key.enabled = pair.Value;
                AssetDatabase.SaveAssetIfDirty(settings);

                Write("build_settings_restored", new
                {
                    applicationIdentifier = PlayerSettings.GetApplicationIdentifier(target),
                    originalApplicationIdentifier = oldId,
                    applicationIdentifierRestored = PlayerSettings.GetApplicationIdentifier(target) == oldId,
                    productName = PlayerSettings.productName,
                    originalProductName = oldProduct,
                    productNameRestored = PlayerSettings.productName == oldProduct,
                    targetSdk = PlayerSettings.Android.targetSdkVersion.ToString(),
                    targetSdkRestored = PlayerSettings.Android.targetSdkVersion == oldSdk,
                    architecture = PlayerSettings.Android.targetArchitectures.ToString(),
                    architectureRestored = PlayerSettings.Android.targetArchitectures == oldArch,
                    backend = PlayerSettings.GetScriptingBackend(target).ToString(),
                    backendRestored = PlayerSettings.GetScriptingBackend(target) == oldBackend,
                    customKeystoreRestored = PlayerSettings.Android.useCustomKeystore == oldKeystore,
                    profileManifestRestored = File.ReadAllBytes(ProfileManifest).SequenceEqual(oldProfileManifest),
                    preloadedAssetsRestored = PlayerSettings.GetPreloadedAssets().SequenceEqual(oldPreloadedAssets),
                    featuresRestored = savedFeatures.All(p => p.Key.enabled == p.Value),
                    sceneMutated = false
                });
            }
        }

        /// <summary>
        /// Rejects the duplicate-rig / duplicate-EventSystem classes of broken build.
        /// This scene is driven by the Meta Interaction SDK rig inside HandsLab_Quest3, not by a
        /// Unity XRI XROrigin, so a zero XROrigin count is expected and is recorded, not enforced.
        /// Only duplicates are fatal.
        /// </summary>
        static void SceneChecks()
        {
            var origins = UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var events = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var flows = UnityEngine.Object.FindObjectsByType<ResearchSessionFlowController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var ports = UnityEngine.Object.FindObjectsByType<ResearchTaskPort>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            Write("scene_checks", new
            {
                scene = ScenePath,
                xrOrigins = origins.Length,
                eventSystems = events.Length,
                flowControllers = flows.Length,
                cameras = cameras.Select(c => c.name).ToArray(),
                rig = "Meta Interaction SDK (HandsLab_Quest3), no Unity XRI XROrigin expected",
                taskPorts = ports.Select(p => new { p.taskId, p.demoMode, p.contentApproved }).ToArray(),
                realTaskMechanics = ports.All(p => p.contentApproved)
                    ? "approved"
                    : "DEMO/TECHNICAL FIXTURE ONLY - contentApproved=false"
            });

            if (origins.Length > 1) throw new Exception("Duplicate XR rig: found " + origins.Length + " XROrigin");
            if (events.Length != 1) throw new Exception("Expected exactly 1 EventSystem, found " + events.Length);
            if (flows.Length != 1) throw new Exception("Expected exactly 1 ResearchSessionFlowController, found " + flows.Length);
            if (ports.Length != 2) throw new Exception("Expected exactly 2 ResearchTaskPort, found " + ports.Length);
        }
    }
}
#endif
