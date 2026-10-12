using System.Threading.Tasks;
using Nop.Core.Domain.Discounts;
using Nop.Core.Events;
using Nop.Services.Configuration;
using Nop.Services.Events;

namespace Nop.Plugin.DiscountRules.HasOnlyProducts.Infrastructure.Cache
{
    /// <summary>
    /// Discount requirement rule event consumer (used for removing unused settings)
    /// </summary>
    public partial class DiscountRequirementEventConsumer : IConsumer<EntityDeletedEvent<DiscountRequirement>>
    {
        #region Fields

        private readonly ISettingService _settingService;

        #endregion

        #region Ctor

        public DiscountRequirementEventConsumer(ISettingService settingService)
        {
            _settingService = settingService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Handle discount requirement deleted event
        /// </summary>
        /// <param name="eventMessage">Event message</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task HandleEventAsync(EntityDeletedEvent<DiscountRequirement> eventMessage)
        {
            var discountRequirement = eventMessage?.Entity;
            if (discountRequirement == null)
                return;

            //delete saved restricted products if exists
            var setting = await _settingService.GetSettingAsync(string.Format(DiscountRequirementDefaults.SETTINGS_KEY, discountRequirement.Id));
            if (setting != null)
                await _settingService.DeleteSettingAsync(setting);

            //delete the saved exclusivity flag if exists
            var exclusiveSetting = await _settingService.GetSettingAsync(string.Format(DiscountRequirementDefaults.EXCLUSIVE_SETTINGS_KEY, discountRequirement.Id));
            if (exclusiveSetting != null)
                await _settingService.DeleteSettingAsync(exclusiveSetting);

            //delete the saved match mode if exists
            var matchAnySetting = await _settingService.GetSettingAsync(string.Format(DiscountRequirementDefaults.MATCH_ANY_SETTINGS_KEY, discountRequirement.Id));
            if (matchAnySetting != null)
                await _settingService.DeleteSettingAsync(matchAnySetting);
        }

        #endregion
    }
}
