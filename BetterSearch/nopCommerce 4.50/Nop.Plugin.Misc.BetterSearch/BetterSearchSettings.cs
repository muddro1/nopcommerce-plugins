using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.BetterSearch
{
    public class BetterSearchSettings : ISettings
    {
        /// <summary>Master switch. When false the plugin delegates everything to stock search.</summary>
        public bool Enabled { get; set; }

        /// <summary>Maximum ids taken from the index before nopCommerce filters them</summary>
        public int MaxIndexResults { get; set; } = 2000;

        /// <summary>
        /// When the strict identifier pass finds nothing, allow a second, fuzzy pass that can
        /// return a DIFFERENT part number than the one typed. Defaults to false: nothing in the
        /// storefront today labels such a result as approximate, so showing one silently would
        /// let a customer order the wrong part. Leave this off until a UI exists that marks
        /// approximate results as such.
        /// </summary>
        public bool AllowApproximateFallback { get; set; }
    }
}
