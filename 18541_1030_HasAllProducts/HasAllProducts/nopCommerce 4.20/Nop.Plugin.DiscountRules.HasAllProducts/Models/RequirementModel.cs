using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.DiscountRules.HasAllProducts.Models
{
    public class RequirementModel
    {
        public int DiscountId { get; set; }

        [NopResourceDisplayName("Plugins.DiscountRules.HasAllProducts.Fields.Products")]
        public string Products { get; set; }

        public int RequirementId { get; set; }
    }
}