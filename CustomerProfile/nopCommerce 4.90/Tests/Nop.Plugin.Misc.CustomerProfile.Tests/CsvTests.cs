using FluentAssertions;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using NUnit.Framework;

namespace Nop.Plugin.Misc.CustomerProfile.Tests;

public class CsvTests
{
    [Test]
    public void Plain_values_are_left_alone()
    {
        Csv.Line("abc", 12, null).Should().Be("abc,12,");
    }

    [Test]
    public void Commas_quotes_and_newlines_are_quoted()
    {
        Csv.Line("a,b", "say \"hi\"", "two\nlines").Should().Be("\"a,b\",\"say \"\"hi\"\"\",\"two\nlines\"");
    }

    [TestCase("=1+1")]
    [TestCase("+44 20")]
    [TestCase("-5")]
    [TestCase("@SUM(A1)")]
    public void Formula_starts_are_neutralised_for_spreadsheets(string value)
    {
        // Customer-entered text opened in Excel must not run as a formula
        Csv.Line(value).Should().Be("'" + value);
    }

    [Test]
    public void File_names_are_made_safe_for_zip_paths()
    {
        Csv.SafeFileName("../..\\evil:name?.pdf").Should().Be("evil_name_.pdf");
        Csv.SafeFileName("  ").Should().Be("file");
    }
}
