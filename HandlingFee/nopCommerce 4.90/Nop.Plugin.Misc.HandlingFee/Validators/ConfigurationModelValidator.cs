using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentValidation;
using Nop.Plugin.Misc.HandlingFee.Models;
using Nop.Plugin.Misc.HandlingFee.Services;
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

            RuleFor(model => model.FeePercent)
                .InclusiveBetween(decimal.Zero, 100m)
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.HandlingFee.Fields.FeePercent.Range"));
            RuleFor(model => model.FeeMinimum)
                .GreaterThanOrEqualTo(decimal.Zero)
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.HandlingFee.Fields.FeeMinimum.Negative"));
            RuleFor(model => model.FeeMaximum)
                .GreaterThanOrEqualTo(decimal.Zero)
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.HandlingFee.Fields.FeeMaximum.Negative"));
            //zero means "no maximum", so the comparison only applies to a real maximum
            RuleFor(model => model.FeeMaximum)
                .GreaterThanOrEqualTo(model => model.FeeMinimum)
                .When(model => model.FeeMaximum > decimal.Zero)
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.HandlingFee.Fields.FeeMaximum.BelowMinimum"));

            //the bands are only required, and only checked, when they are in use
            RuleFor(model => model.TiersJson)
                .Must(AreValidTiers)
                .When(model => model.FeeType == (int)HandlingFeeType.Tiered)
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.HandlingFee.Fields.Tiers.Invalid"));
        }

        /// <summary>
        /// At least one band, every limit zero or more, every fee above zero, no limit used twice
        /// </summary>
        public static bool AreValidTiers(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            List<HandlingFeeTier> tiers;
            try
            {
                tiers = JsonSerializer.Deserialize<List<HandlingFeeTier>>(json);
            }
            catch (JsonException)
            {
                return false;
            }

            return tiers is { Count: > 0 }
                && tiers.All(tier => tier != null && tier.UpTo >= decimal.Zero && tier.Fee > decimal.Zero)
                && tiers.Select(tier => tier.UpTo).Distinct().Count() == tiers.Count;
        }
    }
}
