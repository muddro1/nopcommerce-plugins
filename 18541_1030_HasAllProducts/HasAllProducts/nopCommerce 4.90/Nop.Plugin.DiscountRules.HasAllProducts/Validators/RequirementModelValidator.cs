using System.Text.RegularExpressions;
using FluentValidation;
using Nop.Plugin.DiscountRules.HasAllProducts.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.DiscountRules.HasAllProducts.Validators;

/// <summary>
/// Represents an <see cref="RequirementModel"/> validator.
/// </summary>
public partial class RequirementModelValidator : BaseNopValidator<RequirementModel>
{
    [GeneratedRegex(@"(?!\d+)(?:[^ ,:-])")]
    private static partial Regex NotIdsRegex();

    public RequirementModelValidator(ILocalizationService localizationService)
    {
        RuleFor(model => model.DiscountId)
            .NotEmpty()
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.DiscountRules.HasAllProducts.Fields.DiscountId.Required"));
        RuleFor(model => model.ProductIds)
            .NotEmpty()
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.DiscountRules.HasAllProducts.Fields.ProductIds.Required"));
        RuleFor(model => model.ProductIds)
            .Must(value => !NotIdsRegex().IsMatch(value))
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.DiscountRules.HasAllProducts.Fields.ProductIds.InvalidFormat"))
            .When(model => !string.IsNullOrWhiteSpace(model.ProductIds));
    }
}
