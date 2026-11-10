#if RESEARCH_BUILD_FLAVOR_CHECKS
using System;
using VRMaintenanceResearch.ResearchFlow;

internal static class ResearchBuildFlavorChecks
{
    public static int Main()
    {
#if RESEARCH_DEV_READER_PROTOTYPE
        const bool dev = true;
#else
        const bool dev = false;
#endif
        int checks = 0;
        foreach (bool editorOverride in new[] { false, true })
        {
            ResearchBuildFlavor.EditorDevOverride = editorOverride;
            if (ResearchBuildFlavor.DevReaderPrototype != dev) throw new Exception("Player flavor must ignore Editor override.");
            checks++;
            foreach (ParticipantGroup group in Enum.GetValues(typeof(ParticipantGroup)))
            {
                bool expected = !dev || group == ParticipantGroup.Dev || group == ParticipantGroup.Test;
                if (ResearchBuildFlavor.Allows(group) != expected) throw new Exception("Wrong group gate: " + group);
                checks++;
            }
        }
        Console.WriteLine("BUILD_FLAVOR_PASS flavor=" + ResearchBuildFlavor.Name + "; checks=" + checks + "; scope=compiled_player_branch; unity_editor=false; apk=false");
        return 0;
    }
}
#endif
