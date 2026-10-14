using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Study identity (package not ending ".technical") must keep researcher-controlled setup.
    // UseStudyIdentity -> Play Mode -> Check -> exit -> Restore. The identity change is in-memory and restored.
    public static class ResearchStudyGateChecks
    {
        const string Key = "ResearchStudyGateChecks.OriginalId";

        public static string UseStudyIdentity()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (!id.EndsWith(".technical", StringComparison.Ordinal)) throw new InvalidOperationException("Expected the technical identity: " + id);
            SessionState.SetString(Key, id);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, id.Substring(0, id.Length - ".technical".Length) + ".studygatecheck");
            // HideAndDontSave channel hosts from earlier technical Play sessions survive Play exit in the Editor only.
            var leaked = Resources.FindObjectsOfTypeAll<ResearchAdbControlChannel>();
            foreach (var channel in leaked) UnityEngine.Object.DestroyImmediate(channel.gameObject);
            return PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) + "; removed leaked editor channel hosts=" + leaked.Length;
        }

        public static string Restore()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            string id = SessionState.GetString(Key, "");
            if (id.Length == 0) throw new InvalidOperationException("No saved identity.");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, id);
            SessionState.EraseString(Key);
            return PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        }

        public static string Check()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode required.");
            var passed = new List<string>();
            void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); passed.Add(message); }
            var c = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            Require(c && !ResearchSessionFlowController.TechnicalBuild && !c.TechnicalSelfSetup, "Study identity has no in-headset technical setup: " + Application.identifier);
            Require(c.guards.All(g => g.Suspended && g.SuspensionReason == "researcher_setup"), "Study setup keeps both input guards suspended for the researcher");
            Require(Resources.FindObjectsOfTypeAll<ResearchAdbControlChannel>().Length == 0, "No ADB technical channel instance in the study identity");
            var texts = c.view.mainPanel.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text).ToArray();
            Require(texts.Contains(ResearchStrings.Get("setup.wait", "Thai")) && !texts.Contains(ResearchStrings.Get("technical.start", "Thai")), "Study headset shows the researcher wait, never Start Full Test");
            Require(!c.view.GetComponentsInChildren<ResearchUIAction>(false).Any(a => a.command.StartsWith("technical_", StringComparison.Ordinal) && a.GetComponent<Button>().IsInteractable()), "No technical action is exposed");
            int dev = c.Registry.List(ParticipantGroup.Dev).Count;
            c.Command("technical_start", "Controller");
            Require(!c.StartTechnicalSelfTest("Controller"), "Direct technical self-start is refused");
            Require(c.Record == null && c.Flow == null && c.Registry.List(ParticipantGroup.Dev).Count == dev, "No record or DEV ID is created in the study identity");
            string path = Path.GetFullPath("Logs/ResearchFlowEvidence/2026-09-28-TechnicalSetup/study-gate-check.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = "Passed", identifier = Application.identifier, passed, hardware_verified = false, utc = DateTime.UtcNow.ToString("O") }, Formatting.Indented));
            return "STUDY_GATE_PASS checks=" + passed.Count + " " + path;
        }
    }
}
