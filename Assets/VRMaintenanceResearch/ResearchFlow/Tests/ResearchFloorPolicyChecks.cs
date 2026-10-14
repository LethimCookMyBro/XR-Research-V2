#if RESEARCH_FLOOR_POLICY_SELF_TEST
using System;
using VRMaintenanceResearch.ResearchFlow;

static class ResearchFloorPolicyChecks
{
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    static void Main()
    {
        var policy = new ResearchFloorRecovery();
        Check(!policy.Observe(0, false, false, false, false, false) && !policy.Ready && policy.Attempts == 0, "No native subsystem remains blocked without requests");
        Check(!policy.Observe(1, true, false, false, false, false) && policy.Attempts == 0, "Initialization does not consume retries");
        Check(!policy.Observe(2, true, true, false, false, false) && policy.Status == "FloorUnsupported" && !policy.Ready, "Unsupported Floor never falls back to Device");
        Check(!policy.Observe(3, true, true, true, true, true) && policy.Ready, "Native Floor plus managed offset confirmation is ready");
        policy.Invalidate();
        Check(!policy.Ready && policy.Attempts == 0, "Origin change callback blocks immediately without replenishing retry budget");
        Check(policy.Observe(4, true, true, true, false, false) && !policy.Ready, "Unexpected Floor to Device transition blocks and requests Floor");
        Check(!policy.Observe(4.25, true, true, true, false, false) && policy.Attempts == 1, "Accepted request alone is not a readiness result");
        Check(!policy.Observe(4.5, true, true, true, true, false) && !policy.Ready, "Wait for XROrigin to remove Device offset");
        Check(!policy.Observe(4.75, true, true, true, true, true) && policy.Ready, "Device to Floor readback restores readiness");
        Check(policy.Observe(5, true, true, true, false, false) && policy.Attempts == 1, "A new Floor loss has a fresh bounded budget");
        for (int i = 1; i < 100; i++) Check(!policy.Observe(5 + i / 100.0, true, true, true, false, false), "No per-frame retry");
        Check(policy.Observe(6, true, true, true, false, false) && policy.Attempts == 2, "Second retry at one second");
        Check(policy.Observe(7, true, true, true, false, false) && policy.Attempts == 3, "Third bounded retry");
        Check(!policy.Observe(100, true, true, true, false, false) && !policy.Ready && policy.Status == "FloorRecoveryExhausted", "Exhaustion stays blocked indefinitely");
        for (int i = 0; i < 8; i++)
        {
            policy.Invalidate();
            Check(!policy.Ready && policy.Status == "FloorRecoveryExhausted" && policy.Attempts == 3,
                "Already-blocked exhaustion was lost: expected FloorRecoveryExhausted, got " + policy.Status + " (ready=" + policy.Ready + ", attempts=" + policy.Attempts + ")");
            Check(!policy.Observe(100 + i / 100.0, true, true, true, false, false) && !policy.Ready && policy.Status == "FloorRecoveryExhausted" && policy.Attempts == 3,
                "Repeated non-Floor callbacks must not change exhaustion or replenish requests");
        }
        Check(!policy.Observe(101, true, true, true, true, true) && policy.Ready, "Native recovery can confirm after request exhaustion");
        policy.Invalidate();
        Check(!policy.Ready && policy.Status == "AwaitingFloor", "A real confirmed Floor loss blocks immediately after exhausted recovery");
        Check(policy.Observe(101.25, true, true, true, false, false) && policy.Attempts == 1, "A genuine recovered Floor loss still grants a fresh bounded request");
        policy.Restart();
        Check(!policy.Ready && policy.Attempts == 0, "Lifecycle reset immediately invalidates readiness");
        Check(policy.Observe(102, true, true, true, false, false), "Lifecycle restart grants one fresh request");
        policy.Restart();
        policy.Invalidate();
        Check(!policy.Ready && policy.Status == "AwaitingXR" && policy.Attempts == 0, "Already-blocked initialization remains AwaitingXR without retries");
        Check(!policy.Observe(103, true, true, false, false, false) && policy.Status == "FloorUnsupported", "Unsupported readback stays blocked");
        policy.Invalidate();
        Check(!policy.Ready && policy.Status == "FloorUnsupported" && policy.Attempts == 0, "Already-blocked unsupported Floor reason remains stable");
        policy.Restart();
        Check(!policy.Observe(104, true, true, true, true, false) && policy.Status == "AwaitingXROrigin", "Unconfirmed managed offset stays blocked");
        policy.Invalidate();
        Check(!policy.Ready && policy.Status == "AwaitingXROrigin" && policy.Attempts == 0, "Already-blocked managed readback reason remains stable");
        Console.WriteLine("RESEARCH_FLOOR_POLICY_PASS checks=" + checks);
    }
}
#endif
