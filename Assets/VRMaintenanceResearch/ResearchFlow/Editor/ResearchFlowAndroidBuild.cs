#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Android;
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
        internal const string AppIcon = "Assets/VRMaintenanceResearch/ResearchFlow/UI/AppIcon/ResearchFlow_AppIcon.png";
        internal static bool BuildingResearchApk { get; private set; }

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

        static void TryRestore(Action action, List<Exception> errors)
        {
            try { action(); }
            catch (Exception error) { errors.Add(error); }
        }

        [MenuItem("VR Research/Research Flow/Build Quest APK")]
        public static void BuildApk()
        {
            if (EditorApplication.isPlaying) throw new Exception("Exit Play Mode before building");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new Exception("Editor is busy compiling/updating");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new Exception("ResearchFlow build requires the active build target to already be Android");

            ResearchBuildIntegrityChecks.RequireLocalServicesDisabled();

            // Preserve unsaved work rather than silently saving it.
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new Exception("Unsaved scene changes present; save or revert before building");

            if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            SceneChecks();

            var preservedFiles = new[] {
                "ProjectSettings/ProjectSettings.asset",
                ResearchBuildIntegrityChecks.LocalServicesSettingsPath,
                "Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_English.asset",
                "Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_Thai.asset",
                "Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_Japanese.asset",
                "Assets/VRMaintenanceResearch/TrainingV2/UI/Fonts/NotoSansThai SDF.asset"
            };
            foreach (string path in preservedFiles.Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)))
                if (AssetDatabase.LoadAllAssetsAtPath(path).Any(EditorUtility.IsDirty))
                    throw new Exception("Unsaved font changes present: " + path);
            ResearchBuildIntegrityChecks.RequireLocalServicesDisabled();
            var preservedBytes = preservedFiles.ToDictionary(path => path, File.ReadAllBytes);

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
            // The shared project default icon is the XRI Examples sample; the research app gets its own launcher icon.
            var oldIcons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            var appIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIcon) ?? throw new Exception("Missing launcher icon " + AppIcon);

            var builtUtc = DateTime.UtcNow;
            string apk = Path.Combine(Out, "ResearchFlowQuest3_" +
                builtUtc.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".apk");
            // Each candidate gets its own Android version: the code only increases, so "pm install -r" over an older build stays an update.
            string oldVersionName = PlayerSettings.bundleVersion;
            int oldVersionCode = PlayerSettings.Android.bundleVersionCode;
            int versionCode = (int)(builtUtc - new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes;
            string versionName = "3.4.1." + builtUtc.ToString("yyyyMMdd.HHmm", System.Globalization.CultureInfo.InvariantCulture);

            try
            {
                PlayerSettings.productName = "VR Maintenance Lab";
                PlayerSettings.bundleVersion = versionName;
                PlayerSettings.Android.bundleVersionCode = versionCode;
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { appIcon }, IconKind.Any);
                PlayerSettings.SetApplicationIdentifier(target, TechnicalApplicationId);
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                // The provider is not declared in normal/study builds. Runtime also enforces exact technical identity and shell/app UID.
                File.WriteAllText(ProfileManifest, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"th.ac.tmu.vr.researchflow.transport\">" +
                    "<application android:label=\"@string/app_name\" android:icon=\"@drawable/research_launcher_icon\">" +
                    "<activity android:name=\"com.unity3d.player.UnityPlayerGameActivity\" android:label=\"@string/app_name\" android:icon=\"@drawable/research_launcher_icon\" />" +
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
                    versionName,
                    versionCode,
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
                BuildingResearchApk = true;
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
                        .Select(m => m.content).Take(40).ToArray(),
                    warnings = report.steps.SelectMany(s => s.messages)
                        .Where(m => m.type == LogType.Warning).Select(m => m.content).ToArray()
                });

                if (!ok) throw new Exception("ResearchFlow Android build failed; see Logs/QuestFinalQA/build/build_result.json");
                ResearchBuildIntegrityChecks.RequireLocalServicesDisabled();
                Debug.Log("RESEARCHFLOW_APK_BUILT " + apk);
            }
            finally
            {
                BuildingResearchApk = false;
                var restoreErrors = new List<Exception>();
                TryRestore(() => File.WriteAllBytes(ProfileManifest, oldProfileManifest), restoreErrors);
                TryRestore(() => AssetDatabase.ImportAsset(ProfileManifest, ImportAssetOptions.ForceSynchronousImport), restoreErrors);
                TryRestore(() => PlayerSettings.productName = oldProduct, restoreErrors);
                TryRestore(() => PlayerSettings.bundleVersion = oldVersionName, restoreErrors);
                TryRestore(() => PlayerSettings.Android.bundleVersionCode = oldVersionCode, restoreErrors);
                TryRestore(() => PlayerSettings.SetIcons(NamedBuildTarget.Unknown, oldIcons, IconKind.Any), restoreErrors);
                TryRestore(() => PlayerSettings.SetApplicationIdentifier(target, oldId), restoreErrors);
                TryRestore(() => PlayerSettings.Android.targetSdkVersion = oldSdk, restoreErrors);
                TryRestore(() => PlayerSettings.Android.targetArchitectures = oldArch, restoreErrors);
                TryRestore(() => PlayerSettings.Android.useCustomKeystore = oldKeystore, restoreErrors);
                TryRestore(() => PlayerSettings.SetScriptingBackend(target, oldBackend), restoreErrors);
                TryRestore(() => EditorUserBuildSettings.buildAppBundle = oldBundle, restoreErrors);
                TryRestore(() => EditorUserBuildSettings.exportAsGoogleAndroidProject = oldExport, restoreErrors);
                TryRestore(() => PlayerSettings.SetPreloadedAssets(oldPreloadedAssets), restoreErrors);
                foreach (var pair in savedFeatures) TryRestore(() => pair.Key.enabled = pair.Value, restoreErrors);
                TryRestore(() => AssetDatabase.SaveAssetIfDirty(settings), restoreErrors);
                // TMP clears dynamic font data and Unity persists temporary settings during builds.
                foreach (var pair in preservedBytes) TryRestore(() => File.WriteAllBytes(pair.Key, pair.Value), restoreErrors);
                foreach (string path in preservedFiles.Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)))
                    TryRestore(() => AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate), restoreErrors);
                bool localServicesRestored = false;
                TryRestore(() =>
                {
                    ResearchBuildIntegrityChecks.RestoreLocalServicesDisabled(preservedBytes[ResearchBuildIntegrityChecks.LocalServicesSettingsPath]);
                    localServicesRestored = true;
                }, restoreErrors);
                bool sourceFilesRestored = false;
                TryRestore(() => sourceFilesRestored = preservedBytes.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), restoreErrors);
                TryRestore(() =>
                {
                    Write("build_settings_restored", new
                    {
                        sourceFilesRestored,
                        localServicesRestored,
                        restorationErrors = restoreErrors.Select(e => e.ToString()).ToArray(),
                        applicationIdentifier = PlayerSettings.GetApplicationIdentifier(target),
                        originalApplicationIdentifier = oldId,
                        applicationIdentifierRestored = PlayerSettings.GetApplicationIdentifier(target) == oldId,
                        productName = PlayerSettings.productName,
                        originalProductName = oldProduct,
                        productNameRestored = PlayerSettings.productName == oldProduct,
                        iconsRestored = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any).SequenceEqual(oldIcons),
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
                }, restoreErrors);
                if (!sourceFilesRestored || !localServicesRestored || restoreErrors.Count > 0)
                    throw new AggregateException("Build restoration failed; see build_settings_restored.json.", restoreErrors);
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

    // Give sideload launchers a single bitmap resource instead of relying on their
    // AdaptiveIconDrawable support. Only generated Gradle files are changed.
    public sealed class ResearchFlowLauncherIconPostprocessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (ResearchFlowAndroidBuild.BuildingResearchApk)
                Apply(path, ResearchFlowAndroidBuild.AppIcon);
        }

        public static void Apply(string unityLibraryPath, string iconPath)
        {
            const string resource = "@drawable/research_launcher_icon";
            XNamespace android = "http://schemas.android.com/apk/res/android";
            string launcherPath = Path.Combine(Path.GetDirectoryName(unityLibraryPath), "launcher", "src", "main", "AndroidManifest.xml");
            string libraryPath = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
            var launcher = XDocument.Load(launcherPath);
            var library = XDocument.Load(libraryPath);
            var application = launcher.Root.Element("application") ?? throw new InvalidDataException("Launcher application missing");
            var activity = library.Root.Element("application")?.Elements("activity")
                .SingleOrDefault(e => (string)e.Attribute(android + "name") == "com.unity3d.player.UnityPlayerGameActivity")
                ?? throw new InvalidDataException("Unity launcher activity missing");
            // Read all required inputs before writing any generated output.
            byte[] png = File.ReadAllBytes(iconPath);
            if (png.Length < 24 || !png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException("Launcher icon must be a PNG");
            string destination = Path.Combine(unityLibraryPath, "src", "main", "res", "drawable-nodpi", "research_launcher_icon.png");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllBytes(destination, png);
            application.SetAttributeValue(android + "icon", resource);
            application.SetAttributeValue(android + "roundIcon", resource);
            activity.SetAttributeValue(android + "icon", resource);
            launcher.Save(launcherPath);
            library.Save(libraryPath);
        }
    }
}
#endif
