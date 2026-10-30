#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Owner 30 Oct: TH/JP Study start failed with "Study requires tracked device, bilingual review and two approved task adapters".
    // In Play Mode, report each Study-start term the controller checks except the tracked headset, which only the Quest provides.
    public static class ResearchStudyStartReadiness
    {
        public static string Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first: task ports bind their mechanics at runtime.");
            var controller = UnityEngine.Object.FindObjectsByType<ResearchSessionFlowController>(FindObjectsSortMode.None).Single();
            bool translations = controller.translationsReviewed || ResearchUIPolicy.StudyOpenWithoutDeviceApprovals;
            var ports = controller.taskPorts;
            string report = "translations_ok=" + translations + " ports=" + ports.Length + " distinct=" + ports.Select(p => p.taskId).Distinct().Count();
            bool portsReady = true;
            foreach (var port in ports)
            {
                bool wasDemo = port.demoMode; port.demoMode = false;
                try { report += " " + port.taskId + ".ready=" + port.IsReadyForStudy + "(bound=" + port.HasBoundMechanics + ")"; portsReady &= port.IsReadyForStudy; }
                finally { port.demoMode = wasDemo; }
            }
            if (!translations || ports.Length != 2 || !portsReady) throw new InvalidOperationException("STUDY_START_NOT_READY " + report);
            return "STUDY_START_READY_EXCEPT_HEADSET " + report;
        }
    }
}
#endif
