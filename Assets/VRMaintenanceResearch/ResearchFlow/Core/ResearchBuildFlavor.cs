#if UNITY_EDITOR
using UnityEngine;
#endif

namespace VRMaintenanceResearch.ResearchFlow
{
    // Owner 4 Nov 2026: the DEV APK tests the front-guard Reader prototype in the headset and must not collect real participant
    // data. ResearchFlowAndroidBuild.BuildDevApk compiles the player with DevReaderDefine; normal builds and the Editor stay as
    // they are. In the DEV flavour Task B in DEV/TEST sessions reads the prototype package, and study groups cannot start.
    public static class ResearchBuildFlavor
    {
        // Package identity remains stable across updates; diagnostics follow the compiled player type.
        public static bool DiagnosticsEnabled =>
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            true;
#else
            false;
#endif
        public const string DevReaderDefine = "RESEARCH_DEV_READER_PROTOTYPE";
        public const string DevStudyBlocker = "DEV build: only DEV and TEST sessions can start; study and pilot sessions are disabled.";
#if RESEARCH_DEV_READER_PROTOTYPE
        static readonly bool Compiled = true;
#else
        static readonly bool Compiled = false;
#endif
        // Editor checks opt in for one Play session; a domain reload clears it.
        public static bool EditorDevOverride;

        public static bool DevReaderPrototype =>
#if UNITY_EDITOR
            Compiled || (Application.isEditor && EditorDevOverride);
#else
            Compiled;
#endif
        public static string Name => DevReaderPrototype ? "dev_reader_prototype" : "standard";
        // Study and Pilot data never come from the DEV flavour.
        public static bool Allows(ParticipantGroup group) => !DevReaderPrototype || group == ParticipantGroup.Dev || group == ParticipantGroup.Test;
    }
}
