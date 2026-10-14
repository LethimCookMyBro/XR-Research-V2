#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Normal research mode vs explicit technical diagnostic mode. Offline policy/source checks only.
    public static class ResearchDiagnosticModeChecks
    {
        [MenuItem("VR Research/Research Flow/Check Diagnostic Mode Gate")]
        public static void Run()
        {
            int checks = 0;
            void Check(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException("Diagnostic mode check failed: " + name);
                checks++;
            }
            const string technical = "th.ac.tmu.vr.researchflow.quest3.technical";
            Check(!ResearchFlowPhysicalProbe.ShouldInstall(false, technical, false), "normal mode: no flag, no capture");
            Check(ResearchFlowPhysicalProbe.ShouldInstall(false, technical, true), "explicit flag enables capture");
            Check(!ResearchFlowPhysicalProbe.ShouldInstall(true, technical, true), "editor never installs");
            Check(!ResearchFlowPhysicalProbe.ShouldInstall(false, "th.ac.tmu.vr.researchflow.quest3", true), "study identity never installs");
            Check(!ResearchFlowPhysicalProbe.ShouldInstall(false, technical + ".x", true), "exact technical identity only");
            Check(ResearchFlowPhysicalProbe.DiagnosticFlag != "physical-baseline.enabled", "old always-on flag name is not read");
            Check(ResearchFlowPhysicalProbe.AllowedFor(null), "researcher setup may be captured");
            Check(ResearchFlowPhysicalProbe.AllowedFor(ParticipantGroup.Dev), "DEV allowed");
            Check(ResearchFlowPhysicalProbe.AllowedFor(ParticipantGroup.Test), "TEST allowed");
            foreach (var group in new[] { ParticipantGroup.Pilot, ParticipantGroup.Thai, ParticipantGroup.Japanese })
                Check(!ResearchFlowPhysicalProbe.AllowedFor(group), group + " stops capture");

            // The flow and the research logger never create or read the heavy capture; only the probe
            // itself (install), the render gate (refusal) and the ADB readout (status) may name it.
            var sources = Directory.GetFiles("Assets/VRMaintenanceResearch", "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/Tests/") && !p.Replace('\\', '/').Contains("/Editor/")).ToArray();
            string[] Mentions(string token) => sources.Where(p => File.ReadAllText(p).Contains(token)).Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Check(Mentions("AddComponent<ResearchFlowPhysicalProbe>").SequenceEqual(new[] { "ResearchFlowPhysicalProbe.cs" }), "probe has one installer");
            Check(Mentions("ResearchFlowPhysicalProbe").SequenceEqual(new[] { "ResearchAdbControlChannel.cs", "ResearchFlowPhysicalProbe.cs", "ResearchRenderExperiment.cs" }),
                "only read-only consumers name the probe");
            Check(Mentions("HandsLabGrabProbe").SequenceEqual(new[] { "HandsLabGrabProbe.cs", "HandsLabSession.cs", "ResearchFlowPhysicalProbe.cs" }),
                "grab probe added only by the probe or the separate HandsLab lab session");
            foreach (string owner in new[] { "ResearchSessionFlowController.cs", "HandsLabLogger.cs" })
                Check(!sources.Where(p => Path.GetFileName(p) == owner).Any(p => File.ReadAllText(p).Contains("Probe")), owner + " has no probe dependency");
            Debug.Log("RESEARCH_DIAGNOSTIC_MODE_PASS checks=" + checks + "; offline policy/source only, not hardware");
        }
    }
}
#endif
