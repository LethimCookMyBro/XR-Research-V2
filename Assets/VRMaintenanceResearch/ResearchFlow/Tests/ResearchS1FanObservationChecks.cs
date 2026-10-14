#if RESEARCH_S1_FAN_OBSERVATION_CHECKS
using System;
using VRMaintenanceResearch.ResearchFlow;

internal static class ResearchS1FanObservationChecks
{
    private static int checks, failures;
    private static void Equal(string actual, string expected, string name)
    {
        checks++;
        if (actual == expected) return;
        failures++;
        Console.Error.WriteLine("FAIL " + name + " expected=" + expected + " actual=" + actual);
    }

    public static int Main()
    {
        var state = new S1TaskBState { systemIntegrityValid = true, powerIsolated = true,
            frontGuardPlacement = S1Placement.Seated, frontGuardAligned = true, frontGuardClipped = true,
            bladePlacement = S1Placement.Seated, bladeKeyAligned = true, spinner = S1ThreadState.Tight,
            rearGuardPlacement = S1Placement.Seated, rearLock = S1ThreadState.EngagedLoose };
        Equal(ResearchS1FanObservations.Get(state, "B.front_guard", ParticipantLanguage.Japanese.ToString()),
            "合わせ位置が一致し、クリップは閉じています。", "controller Japanese token");
        Equal(ResearchS1FanObservations.Get(state, "B.front_guard", ParticipantLanguage.Thai.ToString()),
            "ตำแหน่งประกบตรงกันและคลิปปิด", "controller Thai token");
        Equal(ResearchS1FanObservations.Get(state, "B.blade_mount", ParticipantLanguage.Japanese.ToString()),
            "前ガードが観察箇所を覆っています。", "Japanese inaccessible blade");
        Equal(ResearchS1FanObservations.Get(state, "B.blade_mount", ParticipantLanguage.Thai.ToString()),
            "ตะแกรงหน้าบังบริเวณสังเกต", "Thai inaccessible blade");
        Equal(ResearchS1FanObservations.Get(state, "B.front_guard", ParticipantLanguage.English.ToString()),
            ResearchStrings.Get("examine.info.unavailable", "English"), "unsupported language is not Thai");
        Equal(ResearchS1FanObservations.Get(state, "B.front_guard", "JP"),
            ResearchStrings.Get("examine.info.unavailable", "JP"), "no caller authorizes JP alias");
        Equal(ResearchS1FanObservations.Get(state, "B.front_guard", "TH"),
            ResearchStrings.Get("examine.info.unavailable", "TH"), "no caller authorizes TH alias");
        state.systemIntegrityValid = false;
        Equal(ResearchS1FanObservations.Get(state, "B.front_guard", "Japanese"),
            ResearchStrings.Get("examine.info.unavailable", "Japanese"), "malformed bindings are not a Japanese observation");
        Equal(ResearchS1FanObservations.Get(state, "B.front_guard", "Thai"),
            ResearchStrings.Get("examine.info.unavailable", "Thai"), "malformed bindings are not a Thai observation");
        Equal(ResearchS1FanObservations.Get(null, "B.front_guard", "Japanese"),
            ResearchStrings.Get("examine.info.unavailable", "Japanese"), "null snapshot is unavailable");
        Console.WriteLine("S1_FAN_OBSERVATIONS checks=" + checks + "; failures=" + failures + "; scope=source_pure_observations; unity_e2e=false");
        return failures == 0 ? 0 : 1;
    }
}
#endif
