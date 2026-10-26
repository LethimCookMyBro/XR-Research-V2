using System;
using System.IO;
using System.Linq;
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
        public const string LocalServicesSettingsPath = "ProjectSettings/UnityConnectSettings.asset";
        public static string BuildWithOriginalServicesDisabled()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Preserve active Unity work before the bounded build");

            RequireLocalServicesDisabled();
            byte[] originalLocalServices = File.ReadAllBytes(LocalServicesSettingsPath);

            string path = Path.Combine("Logs/QuestFinalQA/build/services", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff"));
            Directory.CreateDirectory(path);
            string beforePath = Path.Combine(path, "services-before-build.json");
            string afterPath = Path.Combine(path, "services-after-build.json");
            if (File.Exists(beforePath) || File.Exists(afterPath))
                throw new InvalidOperationException("Preserve prior services evidence");

            // Pre-existing enabled native/serialized settings are rejected, never silently normalized.
            // Root owns explicit opt-out normalization before this bounded transaction.
            Write(beforePath);
            RequireLocalServicesDisabled();

            try { ResearchFlowAndroidBuild.BuildApk(); }
            finally
            {
                try { RestoreLocalServicesDisabled(originalLocalServices); }
                finally { Write(afterPath); }
            }
            RequireLocalServicesDisabled();
            return "Build complete with original disabled local service flags; serialized APK settings require independent verification";
        }

        public static void RequireLocalServicesDisabled()
        {
            if (ServicesEnabled || CrashReportingSettings.enabled || AnalyticsSettings.enabled)
                throw new InvalidOperationException("Research APK requires local services, Cloud Diagnostics and analytics disabled; inspect serialized APK before installation");
            ValidateDisabledLocalServicesBytes(File.ReadAllBytes(LocalServicesSettingsPath));
        }

        static void ValidateDisabledLocalServicesBytes(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            string text = System.Text.Encoding.UTF8.GetString(bytes);
            var enabled = System.Text.RegularExpressions.Regex.Matches(text, @"(?m)^\s*m_Enabled:\s*([0-9]+)\s*$");
            var enabledKeys = System.Text.RegularExpressions.Regex.Matches(text, @"(?m)^\s*(?:""m_Enabled""|'m_Enabled'|m_Enabled)\s*:");
            var crash = System.Text.RegularExpressions.Regex.Matches(text, @"(?m)^\s*m_EnableCloudDiagnosticsReporting:\s*([0-9]+)\s*$");
            var crashKeys = System.Text.RegularExpressions.Regex.Matches(text, @"(?m)^\s*(?:""m_EnableCloudDiagnosticsReporting""|'m_EnableCloudDiagnosticsReporting'|m_EnableCloudDiagnosticsReporting)\s*:");
            if (!text.Contains("--- !u!310 &1") || !text.Contains("UnityConnectSettings:") ||
                enabled.Count != 6 || enabledKeys.Count != enabled.Count || enabled.Cast<System.Text.RegularExpressions.Match>().Any(m => m.Groups[1].Value != "0") ||
                crash.Count != 1 || crashKeys.Count != crash.Count || crash[0].Groups[1].Value != "0")
                throw new InvalidOperationException("Preserve pre-existing serialized local services settings; Root opt-out normalization required before build");
        }

        public static void RestoreLocalServicesDisabled(byte[] originalSettings)
        {
            ValidateDisabledLocalServicesBytes(originalSettings);
            // Entry guard established native false. Never restore a previously observed true value.
            ServicesEnabled = false;
            CrashReportingSettings.enabled = false;
            if (AnalyticsSettings.enabled)
            {
                var analytics = typeof(AnalyticsSettings).GetProperty("enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (analytics == null || !analytics.CanWrite) throw new MissingMemberException("Pinned local analytics setter unavailable");
                analytics.SetValue(null, false);
            }
            if (ServicesEnabled || CrashReportingSettings.enabled || AnalyticsSettings.enabled)
                throw new InvalidOperationException("Local native services restoration failed");
            File.WriteAllBytes(LocalServicesSettingsPath, originalSettings);
            RequireLocalServicesDisabled();
            if (!File.ReadAllBytes(LocalServicesSettingsPath).SequenceEqual(originalSettings))
                throw new IOException("Exact local services bytes restoration failed");
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
