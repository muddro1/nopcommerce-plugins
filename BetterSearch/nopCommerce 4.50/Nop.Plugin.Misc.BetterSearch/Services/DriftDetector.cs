namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Result of comparing the live index's document count against a freshly rebuilt one.
    /// </summary>
    public record DriftReport(bool HasDrifted, string Message);

    /// <summary>
    /// The pure half of the drift check: no index, no I/O, just the two counts.
    ///
    /// A periodic full rebuild otherwise conceals a missed event - the index silently
    /// self-corrects, so a sync bug between the live index and the catalogue never produces a
    /// visible symptom. Comparing the live count against the rebuilt one, before the rebuild
    /// discards the evidence, turns that silent repair into something that gets noticed.
    /// </summary>
    public static class DriftDetector
    {
        public static DriftReport Compare(int liveCount, int rebuiltCount)
        {
            if (liveCount == rebuiltCount)
                return new DriftReport(false,
                    $"Search index rebuild found no drift: live document count was {liveCount}, rebuilt count is {rebuiltCount}.");

            return new DriftReport(true,
                $"Search index drift detected: live document count was {liveCount}, rebuilt count is {rebuiltCount}.");
        }
    }
}
