using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Every Android build (File > Build, HandsLab, scripted BuildPlayer), not only the menu path, must ship with telemetry off.
    sealed class ResearchPrivacyBuildGate : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            try { ResearchBuildIntegrityChecks.RequireLocalServicesDisabled(); }
            catch (System.Exception ex) { throw new BuildFailedException(ex); }
        }
    }
}
