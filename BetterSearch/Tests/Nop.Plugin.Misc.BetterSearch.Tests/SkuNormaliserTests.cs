using FluentAssertions;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    [TestFixture]
    public class SkuNormaliserTests
    {
        [Test]
        public void Normalise_lowercases_and_strips_punctuation()
        {
            SkuNormaliser.Normalise("FMSA-AB-1234").Should().Be("fmsaab1234");
        }

        [Test]
        public void Normalise_is_stable_for_input_that_is_already_normal()
        {
            SkuNormaliser.Normalise("fmsaab1234").Should().Be("fmsaab1234");
        }

        [Test]
        public void Normalise_handles_null_and_empty()
        {
            SkuNormaliser.Normalise(null).Should().BeEmpty();
            SkuNormaliser.Normalise("   ").Should().BeEmpty();
        }

        [Test]
        public void Segments_splits_on_punctuation_and_lowercases()
        {
            SkuNormaliser.Segments("FMSA-AB-1234").Should().Equal("fmsa", "ab", "1234");
        }

        [Test]
        public void Segments_ignores_repeated_and_trailing_separators()
        {
            SkuNormaliser.Segments("fmsa--ab-1234-").Should().Equal("fmsa", "ab", "1234");
        }

        [Test]
        public void Segments_of_a_value_with_no_separators_is_the_value_itself()
        {
            SkuNormaliser.Segments("fmsaab1234").Should().Equal("fmsaab1234");
        }

        [Test]
        public void Segments_handles_null_and_empty()
        {
            SkuNormaliser.Segments(null).Should().BeEmpty();
            SkuNormaliser.Segments("--").Should().BeEmpty();
        }

        [Test]
        public void NGrams_cover_every_substring_within_the_length_bounds()
        {
            var grams = SkuNormaliser.NGrams("fmsa-ab-1234", 3, 4);

            //generated over the NORMALISED form, so punctuation never appears in a gram
            grams.Should().Contain("123");
            grams.Should().Contain("234");
            grams.Should().Contain("1234");
            grams.Should().Contain("ab12");
            grams.Should().NotContain(g => g.Contains("-"));
        }

        [Test]
        public void NGrams_respects_its_bounds()
        {
            var grams = SkuNormaliser.NGrams("fmsa-ab-1234", 3, 4);

            grams.Should().OnlyContain(g => g.Length >= 3 && g.Length <= 4);
        }

        [Test]
        public void NGrams_are_distinct()
        {
            var grams = SkuNormaliser.NGrams("aaaa", 2, 2);

            grams.Should().Equal("aa");
        }

        [Test]
        public void NGrams_of_a_value_shorter_than_the_minimum_is_empty()
        {
            SkuNormaliser.NGrams("ab", 3, 4).Should().BeEmpty();
        }

        [Test]
        public void NGrams_handles_null()
        {
            SkuNormaliser.NGrams(null, 2, 5).Should().BeEmpty();
        }
    }
}
