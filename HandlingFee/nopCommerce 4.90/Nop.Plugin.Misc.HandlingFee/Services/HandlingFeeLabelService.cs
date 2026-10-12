using System.Collections.Generic;
using System.Linq;
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
            await CaptureMissingAsync(entries, null);

            return HandlingFeeLabelDefaults.SerialiseBackup(entries);
        }

        /// <summary>
        /// Bring a backup in line with the resources and languages that exist now: drop entries
        /// for resources no longer managed, and capture every (language, resource) pair the
        /// backup lacks - a resource a newer build manages, or a language added since the
        /// backup was taken. Call this before the label is applied again, while those pairs
        /// still hold nopCommerce's own wording; <see cref="ApplyAsync"/> writes every language.
        /// </summary>
        /// <param name="backupJson">Previously captured originals</param>
        /// <param name="appliedLabel">The label currently written into the resources, if any. A
        /// missing pair already holding exactly that wording is the plugin's own, not an original,
        /// and is never recorded as one - restoring it would put the label straight back.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the migrated backup, serialised</returns>
        public virtual async Task<string> MigrateBackupAsync(string backupJson, string appliedLabel = null)
        {
            var entries = HandlingFeeLabelDefaults.RetainManaged(HandlingFeeLabelDefaults.DeserialiseBackup(backupJson));
            await CaptureMissingAsync(entries, appliedLabel);

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
                if (entry.Absent)
                {
                    //applying created this resource; the language had none before
                    var created = await _localizationService.GetLocaleStringResourceByNameAsync(entry.ResourceName, entry.LanguageId, false);
                    if (created != null)
                        await _localizationService.DeleteLocaleStringResourceAsync(created);

                    continue;
                }

                await _localizationService.AddOrUpdateLocaleResourceAsync(
                    new Dictionary<string, string> { [entry.ResourceName] = entry.Value },
                    entry.LanguageId);
            }
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Add an entry for every managed (language, resource) pair not already in <paramref name="entries"/>
        /// </summary>
        protected virtual async Task CaptureMissingAsync(IList<LabelBackupEntry> entries, string appliedLabel)
        {
            var appliedValues = string.IsNullOrWhiteSpace(appliedLabel)
                ? new Dictionary<string, string>()
                : HandlingFeeLabelDefaults.BuildResourceValues(appliedLabel);

            //showHidden: unpublished languages still render for anyone using them
            var languages = await _languageService.GetAllLanguagesAsync(showHidden: true);

            foreach (var language in languages)
            {
                foreach (var resourceName in HandlingFeeLabelDefaults.ManagedResources)
                {
                    if (entries.Any(entry => entry.LanguageId == language.Id && entry.ResourceName == resourceName))
                        continue;

                    //logIfNotFound: false - a language legitimately may not define every resource
                    var resource = await _localizationService
                        .GetLocaleStringResourceByNameAsync(resourceName, language.Id, false);

                    //already holding the applied label: whatever was there first is gone, and
                    //recording the label as the "original" would make it permanent
                    if (resource != null && appliedValues.TryGetValue(resourceName, out var appliedValue)
                        && resource.ResourceValue == appliedValue)
                        continue;

                    entries.Add(new LabelBackupEntry
                    {
                        LanguageId = language.Id,
                        ResourceName = resourceName,
                        Value = resource?.ResourceValue,
                        Absent = resource == null
                    });
                }
            }
        }

        #endregion
    }
}
