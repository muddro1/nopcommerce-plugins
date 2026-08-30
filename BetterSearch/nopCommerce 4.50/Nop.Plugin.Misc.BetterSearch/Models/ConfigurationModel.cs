using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.BetterSearch.Models
{
    public record ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Misc.BetterSearch.Fields.Enabled")]
        public bool Enabled { get; set; }
        public bool Enabled_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.BetterSearch.Fields.MaxIndexResults")]
        public int MaxIndexResults { get; set; }
        public bool MaxIndexResults_OverrideForStore { get; set; }

        #region Index status (read-only, not posted back)

        [NopResourceDisplayName("Plugins.Misc.BetterSearch.IndexStatus.DocumentCount")]
        public int DocumentCount { get; set; }

        [NopResourceDisplayName("Plugins.Misc.BetterSearch.IndexStatus.Available")]
        public bool IndexAvailable { get; set; }

        #endregion

        #region Minimum search term length warning

        /// <summary>
        /// True when <c>CatalogSettings.ProductSearchTermMinimumLength</c> is greater than 2 -
        /// the point at which nopCommerce's own <c>CatalogModelFactory</c> rejects the store's
        /// two-character SKU segment searches before Better Search is ever consulted.
        /// </summary>
        public bool ShowMinimumSearchTermWarning { get; set; }

        public int ProductSearchTermMinimumLength { get; set; }

        #endregion
    }
}
