using FluentValidation;
using Nop.Plugin.Misc.HandlingFee.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Misc.HandlingFee.Validators
{
    /// <summary>
    /// Represents an <see cref="ConfigurationModel"/> validator.
    /// </summary>
    public class ConfigurationModelValidator : BaseNopValidator<ConfigurationModel>
    {
        public ConfigurationModelValidator(ILocalizationService localizationService)
        {
            RuleFor(model => model.FeeAmount)
                .GreaterThanOrEqualTo(decimal.Zero)
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.HandlingFee.Fields.FeeAmount.Negative"));
            RuleFor(model => model.ThresholdAmount)
                .GreaterThanOrEqualTo(decimal.Zero)
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.HandlingFee.Fields.ThresholdAmount.Negative"));
        }
    }
}
