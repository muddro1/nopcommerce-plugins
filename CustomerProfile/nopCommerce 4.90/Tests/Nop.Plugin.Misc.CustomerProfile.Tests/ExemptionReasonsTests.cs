using FluentAssertions;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using NUnit.Framework;

namespace Nop.Plugin.Misc.CustomerProfile.Tests;

public class ExemptionReasonsTests
{
    [TestCase(10)]
    [TestCase(20)]
    [TestCase(30)]
    public void Offered_choices_are_accepted(int id) => ExemptionReasons.IsChoice(id).Should().BeTrue();

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(15)]
    [TestCase(40)]
    public void Not_set_and_unknown_ids_are_rejected(int id) => ExemptionReasons.IsChoice(id).Should().BeFalse();
}
