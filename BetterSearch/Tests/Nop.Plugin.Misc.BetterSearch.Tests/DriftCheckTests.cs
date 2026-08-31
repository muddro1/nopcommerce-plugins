using FluentAssertions;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    /// <summary>
    /// The drift check is what stops a periodic rebuild from silently concealing a sync bug:
    /// without it, a missed event just gets quietly repaired on the next rebuild and the bug
    /// never produces a visible symptom. <see cref="DriftDetector.Compare"/> is the pure part of
    /// that check - no index, no I/O, just the counts and the checksums.
    ///
    /// The checksum half exists because counts alone catch inserts and deletes but not a
    /// product whose SKU or name changed via a path that raised no event - the count is
    /// identical, so without a checksum no warning is ever logged and the rebuild silently
    /// repairs exactly the drift this check exists to catch.
    /// </summary>
    [TestFixture]
    public class DriftCheckTests
    {
        [Test]
        public void Reports_no_drift_when_counts_and_checksums_both_match()
        {
            var report = DriftDetector.Compare(42, 42, "checksum-a", "checksum-a");

            report.HasDrifted.Should().BeFalse();
        }

        [Test]
        public void Reports_drift_when_live_count_exceeds_rebuilt_count()
        {
            var report = DriftDetector.Compare(50, 47, "checksum-a", "checksum-a");

            report.HasDrifted.Should().BeTrue();
        }

        [Test]
        public void Reports_drift_when_rebuilt_count_exceeds_live_count()
        {
            var report = DriftDetector.Compare(47, 50, "checksum-a", "checksum-a");

            report.HasDrifted.Should().BeTrue();
        }

        [Test]
        public void Drift_message_names_both_counts_when_the_count_differs()
        {
            var report = DriftDetector.Compare(50, 47, "checksum-a", "checksum-a");

            report.Message.Should().Contain("50");
            report.Message.Should().Contain("47");
        }

        [Test]
        public void Reports_drift_when_checksums_differ_even_though_counts_match()
        {
            //this is exactly the case a count-only comparison would miss: a product renamed or
            //re-SKUed via a path that raised no event leaves the document count unchanged
            var report = DriftDetector.Compare(42, 42, "checksum-a", "checksum-b");

            report.HasDrifted.Should().BeTrue();
        }

        [Test]
        public void No_drift_only_when_both_counts_and_checksums_match()
        {
            var equalCounts = DriftDetector.Compare(42, 42, "checksum-a", "checksum-a");
            var differingChecksum = DriftDetector.Compare(42, 42, "checksum-a", "checksum-b");

            equalCounts.HasDrifted.Should().BeFalse();
            differingChecksum.HasDrifted.Should().BeTrue();
        }

        [Test]
        public void Drift_message_names_checksum_when_only_the_checksum_differs()
        {
            var report = DriftDetector.Compare(42, 42, "checksum-a", "checksum-b");

            report.Message.Should().Contain("checksum");
        }

        [Test]
        public void Drift_message_names_count_when_only_the_count_differs()
        {
            var report = DriftDetector.Compare(50, 47, "checksum-a", "checksum-a");

            report.Message.Should().Contain("count");
        }

        [Test]
        public void Drift_message_names_both_when_both_differ()
        {
            var report = DriftDetector.Compare(50, 47, "checksum-a", "checksum-b");

            report.Message.Should().Contain("count");
            report.Message.Should().Contain("checksum");
        }
    }
}
