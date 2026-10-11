using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Turns an identifier such as a SKU or manufacturer part number into the several forms
    /// the index needs.
    ///
    /// The store's SKUs look like fmsa-xx-xxxx, where the leading segment is the same on every
    /// product. Staff search by the varying parts, so matching must work on any fragment of the
    /// identifier rather than only its beginning.
    ///
    /// Pure by design: no nopCommerce services, no I/O, so the matching rules can be tested
    /// exhaustively without a store.
    /// </summary>
    public static class SkuNormaliser
    {
        /// <summary>
        /// Lowercase and strip everything that is not a letter or digit.
        /// "FMSA-AB-1234" becomes "fmsaab1234", which is what lets a search for "ab1234"
        /// match a SKU written with separators.
        /// </summary>
        public static string Normalise(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                    builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Split on any run of non-alphanumeric characters, lowercased.
        /// "FMSA-AB-1234" becomes ["fmsa", "ab", "1234"], so a search for a whole segment
        /// such as "1234" is an exact token match rather than a substring scan.
        /// </summary>
        public static IReadOnlyList<string> Segments(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            return value
                .Split(c => !char.IsLetterOrDigit(c))
                .Where(segment => segment.Length > 0)
                .Select(segment => segment.ToLowerInvariant())
                .ToList();
        }

        /// <summary>
        /// Every distinct substring of the normalised value between the given lengths.
        /// This is what makes a partial segment such as "234" match "fmsa-ab-1234".
        /// </summary>
        public static IReadOnlyList<string> NGrams(string value, int minLength, int maxLength)
        {
            var normalised = Normalise(value);
            if (normalised.Length < minLength)
                return Array.Empty<string>();

            var grams = new HashSet<string>();
            for (var length = minLength; length <= maxLength; length++)
            {
                for (var start = 0; start + length <= normalised.Length; start++)
                    grams.Add(normalised.Substring(start, length));
            }

            return grams.ToList();
        }

        private static IEnumerable<string> Split(this string value, Func<char, bool> isSeparator)
        {
            var builder = new StringBuilder();
            foreach (var character in value)
            {
                if (isSeparator(character))
                {
                    if (builder.Length > 0)
                    {
                        yield return builder.ToString();
                        builder.Clear();
                    }
                }
                else
                {
                    builder.Append(character);
                }
            }

            if (builder.Length > 0)
                yield return builder.ToString();
        }
    }
}
