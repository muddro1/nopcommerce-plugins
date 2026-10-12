using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.DiscountRules.HasOnlyProducts.Models
{
    public record RequirementModel
    {
        public int DiscountId { get; set; }

        [NopResourceDisplayName("Plugins.DiscountRules.HasOnlyProducts.Fields.Products")]
        public string ProductIds { get; set; }

        [NopResourceDisplayName("Plugins.DiscountRules.HasOnlyProducts.Fields.MatchAnyProduct")]
        public bool MatchAnyProduct { get; set; }

        [NopResourceDisplayName("Plugins.DiscountRules.HasOnlyProducts.Fields.OnlyTheseProducts")]
        public bool OnlyTheseProducts { get; set; }

        public int RequirementId { get; set; }
    }
}
