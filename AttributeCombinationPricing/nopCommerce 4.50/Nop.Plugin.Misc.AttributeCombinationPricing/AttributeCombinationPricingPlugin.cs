using Nop.Services.Common;
using Nop.Services.Plugins;

namespace Nop.Plugin.Misc.AttributeCombinationPricing
{
    /// <summary>
    /// Corrects the storefront price-adjustment label for "Associated to product" attribute values
    /// that have a combination-level price override. No settings and no configuration page: the fix
    /// applies automatically wherever the pattern occurs. See
    /// docs/superpowers/specs/2026-09-20-attribute-combination-pricing-design.md.
    /// </summary>
    public class AttributeCombinationPricingPlugin : BasePlugin, IMiscPlugin
    {
    }
}
