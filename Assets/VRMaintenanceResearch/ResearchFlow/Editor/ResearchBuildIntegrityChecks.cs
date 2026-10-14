using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Analytics;
using UnityEditor.CrashReporting;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Preserve the existing research project's services opt-out after Unity's package/domain-load migration.
    // Only native project properties are set; no service Enabler, binding or remote flag API is called.
    public static class ResearchBuildIntegrityChecks
    {
        public static string BuildWithOriginalServicesDisabled()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Preserve active Unity work before the bounded build");

            string path = Path.Combine("Logs/QuestFinalQA/build/services", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff"));
            Directory.CreateDirectory(path);
            string beforePath = Path.Combine(path, "services-before-build.json");
            string afterPath = Path.Combine(path, "services-after-build.json");
            if (File.Exists(beforePath) || File.Exists(afterPath))
                throw new InvalidOperationException("Preserve prior services evidence");

            // Baseline UnityConnectSettings had both services and CrashReporting disabled.
            // The installed legacy package may reapply a Cloud service flag after project binding/refreshed events.
            ServicesEnabled = false;
            CrashReportingSettings.enabled = false;
            Write(beforePath);
            RequireLocalServicesDisabled();

            try { ResearchFlowAndroidBuild.BuildApk(); }
            finally { Write(afterPath); }
            RequireLocalServicesDisabled();
            return "Build complete with original disabled local service flags; serialized APK settings require independent verification";
        }

        public static void RequireLocalServicesDisabled()
        {
            if (ServicesEnabled || CrashReportingSettings.enabled || AnalyticsSettings.enabled)
                throw new InvalidOperationException("Research APK requires local services, Cloud Diagnostics and analytics disabled; inspect serialized APK before installation");
        }

        static void Write(string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(new
            {
                utc = DateTime.UtcNow.ToString("o"),
                unityServicesEnabled = ServicesEnabled,
                legacyCloudDiagnosticsEnabled = CrashReportingSettings.enabled,
                analyticsEnabled = AnalyticsSettings.enabled,
                localProjectSettingsOnly = true,
                hardwareVerified = false
            }, Formatting.Indented));
        }

        // Unity 6000.3.20f1 exposes this native project property on an internal engine type.
        // Resolve precisely and fail closed if the pinned API is absent; no runtime assembly uses this helper.
        static bool ServicesEnabled
        {
            get => (bool)ServicesProperty.GetValue(null);
            set => ServicesProperty.SetValue(null, value);
        }

        static PropertyInfo ServicesProperty => Type.GetType("UnityEngine.Connect.UnityConnectSettings, UnityEngine.UnityConnectModule", true)
            .GetProperty("enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException("Pinned native UnityConnectSettings.enabled property unavailable");
    }
}
