namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>
    /// Decisions of the Reader video loading lifecycle (capture C02), kept pure so they can be checked without a VideoPlayer.
    /// This bounds the way out of "Loading"; it does NOT identify why Android prepare/first-frame stalled (root cause still open).
    /// </summary>
    public static class ResearchVideoLoadRules
    {
        public const float StallSeconds = 10f;

        /// <summary>A coroutine or native callback may act only for the generation it was started for and only while visible.
        /// Hide, format switch, registry change and Retry all advance the generation.</summary>
        public static bool AcceptAsync(int acquiredGeneration, int currentGeneration, bool visible) => acquiredGeneration == currentGeneration && visible;

        /// <summary>The stall notice fires once per load attempt, after StallSeconds of visible loading.</summary>
        public static bool StallDue(bool isVideo, bool visible, bool loading, bool stallAlreadyShown, float secondsLoading) =>
            isVideo && visible && loading && !stallAlreadyShown && secondsLoading > StallSeconds;

        /// <summary>Retry after an error, or during a load that has already shown the stall notice. A fresh attempt resets the notice,
        /// so repeated presses cannot restart the decoder faster than once per StallSeconds.</summary>
        public static bool RetryAllowed(bool error, bool loading, bool stallShown) => error || (loading && stallShown);
    }
}
