using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Services.Localization;

namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// Writes a custom label into the core locale resources through which the handling fee
    /// is displayed, and puts the originals back afterwards.
    ///
    /// These are shared core resources, not plugin-owned ones, so the originals are captured
    /// before the first overwrite. Without that, uninstalling the plugin would leave the store
    /// permanently relabelled by something that is no longer installed.
    /// </summary>
    public class HandlingFeeLabelService
    {
        #region Fields

        private readonly ILanguageService _languageService;
        private readonly ILocalizationService _localizationService;

        #endregion

        #region Ctor

        public HandlingFeeLabelService(ILanguageService languageService,
            ILocalizationService localizationService)
        {
            _languageService = languageService;
            _localizationService = localizationService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Capture the current value of every managed resource, in every language,
        /// so it can be restored later
        /// </summary>
        /// <returns>The captured originals, serialised for storage in a setting</returns>
        public virtual async Task<string> CaptureOriginalsAsync()
        {
            var entries = new List<LabelBackupEntry>();

            //showHidden: unpublished languages still render for anyone using them
            var languages = await _languageService.GetAllLanguagesAsync(showHidden: true);

            foreach (var language in languages)
            {
                foreach (var resourceName in HandlingFeeLabelDefaults.ManagedResources)
                {
                    //logIfNotFound: false — a language legitimately may not define every resource
                    var resource = await _localizationService
                        .GetLocaleStringResourceByNameAsync(resourceName, language.Id, false);

                    if (resource == null)
                        continue;

                    entries.Add(new LabelBackupEntry
                    {
                        LanguageId = language.Id,
                        ResourceName = resourceName,
                        Value = resource.ResourceValue
                    });
                }
            }

            return HandlingFeeLabelDefaults.SerialiseBackup(entries);
        }

        /// <summary>
        /// Write the given label into every managed resource, in every language
        /// </summary>
        /// <param name="label">The label as typed by the store owner</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual async Task ApplyAsync(string label)
        {
            var values = HandlingFeeLabelDefaults.BuildResourceValues(label);
            var languages = await _languageService.GetAllLanguagesAsync(showHidden: true);

            foreach (var language in languages)
                await _localizationService.AddOrUpdateLocaleResourceAsync(values, language.Id);
        }

        /// <summary>
        /// Put the captured originals back
        /// </summary>
        /// <param name="backupJson">Previously captured originals</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual async Task RestoreAsync(string backupJson)
        {
            foreach (var entry in HandlingFeeLabelDefaults.DeserialiseBackup(backupJson))
            {
                await _localizationService.AddOrUpdateLocaleResourceAsync(
                    new Dictionary<string, string> { [entry.ResourceName] = entry.Value },
                    entry.LanguageId);
            }
        }

        #endregion
    }
}
