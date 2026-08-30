using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.BetterSearch
{
    public class BetterSearchSettings : ISettings
    {
        /// <summary>Master switch. When false the plugin delegates everything to stock search.</summary>
        public bool Enabled { get; set; }

        /// <summary>Maximum ids taken from the index before nopCommerce filters them</summary>
        public int MaxIndexResults { get; set; } = 2000;
    }
}
