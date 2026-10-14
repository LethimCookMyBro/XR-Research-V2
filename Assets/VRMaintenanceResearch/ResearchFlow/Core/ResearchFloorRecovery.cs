namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Bounded request policy; only native plus XROrigin readback establishes readiness.</summary>
    public sealed class ResearchFloorRecovery
    {
        public const int MaximumAttempts = 3;
        public const double RetrySeconds = 1;
        public bool Ready { get; private set; }
        public string Status { get; private set; } = "AwaitingXR";
        public int Attempts { get; private set; }
        double nextRequest;
        bool wasNativeFloor;

        public void Restart()
        {
            Ready = false; Status = "AwaitingXR"; Attempts = 0;
            nextRequest = 0; wasNativeFloor = false;
        }

        public void Invalidate() { if (!Ready) return; Ready = false; Status = "AwaitingFloor"; }

        // Returns permission for one request, never proof that the request succeeded.
        public bool Observe(double now, bool running, bool supportKnown, bool supportsFloor, bool nativeFloor, bool managedFloor)
        {
            Ready = false;
            if (!running || !supportKnown) { Status = "AwaitingXR"; return false; }
            if (wasNativeFloor && !nativeFloor) { Attempts = 0; nextRequest = now; }
            wasNativeFloor = nativeFloor;
            if (!supportsFloor) { Status = "FloorUnsupported"; return false; }
            if (nativeFloor)
            {
                Ready = managedFloor;
                Status = Ready ? "FloorConfirmed" : "AwaitingXROrigin";
                return false;
            }
            Status = Attempts >= MaximumAttempts ? "FloorRecoveryExhausted" : "AwaitingFloor";
            if (Attempts >= MaximumAttempts || now < nextRequest) return false;
            Attempts++;
            nextRequest = now + RetrySeconds;
            return true;
        }
    }
}
