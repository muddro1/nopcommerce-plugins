using System.Collections.Generic;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Result of comparing the live index's document count and content checksum against a
    /// freshly rebuilt one.
    /// </summary>
    public record DriftReport(bool HasDrifted, string Message);

    /// <summary>
    /// The pure half of the drift check: no index, no I/O, just the two counts and the two
    /// checksums.
    ///
    /// A periodic full rebuild otherwise conceals a missed event - the index silently
    /// self-corrects, so a sync bug between the live index and the catalogue never produces a
    /// visible symptom. Comparing the live state against the rebuilt one, before the rebuild
    /// discards the evidence, turns that silent repair into something that gets noticed.
    ///
    /// Counts alone catch inserts and deletes but not a product whose SKU or name changed via a
    /// path that raised no event: the count stays identical, nothing gets logged, and the
    /// scheduled rebuild silently repairs exactly the drift this check exists to catch. The
    /// content checksum (see <see cref="SearchIndexManager.ContentChecksumAsync"/>) closes that
    /// gap, so drift is reported whenever EITHER the counts or the checksums differ.
    /// </summary>
    public static class DriftDetector
    {
        public static DriftReport Compare(int liveCount, int rebuiltCount, string liveChecksum, string rebuiltChecksum)
        {
            var countDiffers = liveCount != rebuiltCount;
            var checksumDiffers = liveChecksum != rebuiltChecksum;

            if (!countDiffers && !checksumDiffers)
                return new DriftReport(false,
                    $"Search index rebuild found no drift: live document count was {liveCount}, rebuilt count is {rebuiltCount}, and the content checksums match.");

            var reasons = new List<string>();
            if (countDiffers)
                reasons.Add($"document count differs (live {liveCount}, rebuilt {rebuiltCount})");
            if (checksumDiffers)
                reasons.Add("content checksum differs");

            return new DriftReport(true,
                $"Search index drift detected: {string.Join(" and ", reasons)}.");
        }
    }
}
