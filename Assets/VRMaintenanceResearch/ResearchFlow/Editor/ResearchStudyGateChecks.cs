#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // S1_OWNER_OPEN_STUDY_20261029: a Thai/Japanese Study record starts with no authorization file on the device.
    public static class ResearchStudyGateChecks
    {
        public static string Run()
        {
            int checks = 0;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
            var controller = UnityEngine.Object.FindObjectsByType<ResearchSessionFlowController>(FindObjectsInactive.Include, FindObjectsSortMode.None).First();
            string dir = Path.Combine(Path.GetTempPath(), "cc_no_auth_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (var group in new[] { ParticipantGroup.Thai, ParticipantGroup.Japanese })
                {
                    var record = new ParticipantRecord { participant_id = "GATE_" + group, group = group, test_data = false, session_state = RegistryState.Available, revision = 3 };
                    Check(controller.StudyApprovalBlocker(record, dir) == null, group + " Study is not blocked by a missing authorization file");
                    Check(ResearchOperatorGuard.Demand("start", record, null) == "OwnerOpenStudyNoDeviceAuthorization", group + " start is allowed and labelled");
                }
                // Ports bind their state providers only in Play; in Edit Mode check the approval term they no longer require.
                Check(controller.taskPorts.Length == 2 && controller.taskPorts.All(p => p && (p.contentApproved || ResearchUIPolicy.StudyOpenWithoutDeviceApprovals)), "both scene task ports accept Study content");
            }
            finally { Directory.Delete(dir, true); }
            return "STUDY_GATE_OPEN_PASS checks=" + checks + " version=" + ResearchUIPolicy.StudyGateVersion;
        }
    }
}
#endif
