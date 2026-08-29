using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.DiscountRules.HasAllProducts.Models
{
    public record RequirementModel
    {
        public int DiscountId { get; set; }

        [NopResourceDisplayName("Plugins.DiscountRules.HasAllProducts.Fields.Products")]
        public string ProductIds { get; set; }

        public int RequirementId { get; set; }
    }
}