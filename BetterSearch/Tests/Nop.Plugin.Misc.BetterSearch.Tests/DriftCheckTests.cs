using FluentAssertions;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    /// <summary>
    /// The drift check is what stops a periodic rebuild from silently concealing a sync bug:
    /// without it, a missed event just gets quietly repaired on the next rebuild and the bug
    /// never produces a visible symptom. <see cref="DriftDetector.Compare"/> is the pure part of
    /// that check - no index, no I/O, just the counts.
    /// </summary>
    [TestFixture]
    public class DriftCheckTests
    {
        [Test]
        public void Reports_no_drift_when_counts_are_equal()
        {
            var report = DriftDetector.Compare(42, 42);

            report.HasDrifted.Should().BeFalse();
        }

        [Test]
        public void Reports_drift_when_live_count_exceeds_rebuilt_count()
        {
            var report = DriftDetector.Compare(50, 47);

            report.HasDrifted.Should().BeTrue();
        }

        [Test]
        public void Reports_drift_when_rebuilt_count_exceeds_live_count()
        {
            var report = DriftDetector.Compare(47, 50);

            report.HasDrifted.Should().BeTrue();
        }

        [Test]
        public void Drift_message_names_both_counts()
        {
            var report = DriftDetector.Compare(50, 47);

            report.Message.Should().Contain("50");
            report.Message.Should().Contain("47");
        }
    }
}
