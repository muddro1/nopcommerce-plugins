using System.Text.RegularExpressions;
using FluentValidation;
using Nop.Plugin.DiscountRules.HasOnlyProducts.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.DiscountRules.HasOnlyProducts.Validators
{
    /// <summary>
    /// Represents an <see cref="RequirementModel"/> validator.
    /// </summary>
    public class RequirementModelValidator : BaseNopValidator<RequirementModel>
    {
        public RequirementModelValidator(ILocalizationService localizationService)
        {
            RuleFor(model => model.DiscountId)
                .NotEmpty()
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.DiscountRules.HasOnlyProducts.Fields.DiscountId.Required"));
            RuleFor(model => model.ProductIds)
                .NotEmpty()
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.DiscountRules.HasOnlyProducts.Fields.ProductIds.Required"));
            RuleFor(model => model.ProductIds)
                .Must(value => !Regex.IsMatch(value, @"(?!\d+)(?:[^ ,:-])"))
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.DiscountRules.HasOnlyProducts.Fields.ProductIds.InvalidFormat"))
                .When(model => !string.IsNullOrWhiteSpace(model.ProductIds));
        }
    }
}
