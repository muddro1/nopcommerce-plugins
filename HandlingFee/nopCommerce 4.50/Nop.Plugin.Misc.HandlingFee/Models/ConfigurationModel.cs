using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.HandlingFee.Models
{
    public record ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.Enabled")]
        public bool Enabled { get; set; }
        public bool Enabled_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.ThresholdAmount")]
        public decimal ThresholdAmount { get; set; }
        public bool ThresholdAmount_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.FeeAmount")]
        public decimal FeeAmount { get; set; }
        public bool FeeAmount_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.SuppressWhenShippingCharged")]
        public bool SuppressWhenShippingCharged { get; set; }
        public bool SuppressWhenShippingCharged_OverrideForStore { get; set; }
    }
}
