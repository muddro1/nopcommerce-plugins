using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Nop.Core.Domain.Localization;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Localization;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    [TestFixture]
    public class HandlingFeeLabelServiceTests
    {
        private const string LABEL = "Small Order Handling Charge";

        //(language, resource name) -> value: an in-memory stand-in for the locale string resource table
        private Dictionary<(int, string), string> _resources;
        private List<Language> _languages;
        private HandlingFeeLabelService _service;

        [SetUp]
        public void SetUp()
        {
            _resources = new Dictionary<(int, string), string>();
            _languages = new List<Language> { new() { Id = 1 } };

            var languageService = new Mock<ILanguageService>();
            languageService.Setup(x => x.GetAllLanguagesAsync(true, 0)).ReturnsAsync(() => _languages.ToList());

            var localizationService = new Mock<ILocalizationService>();
            localizationService.Setup(x => x.GetLocaleStringResourceByNameAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()))
                .ReturnsAsync((string name, int languageId, bool _) => _resources.TryGetValue((languageId, name), out var value)
                    ? new LocaleStringResource { LanguageId = languageId, ResourceName = name, ResourceValue = value }
                    : null);
            localizationService.Setup(x => x.AddOrUpdateLocaleResourceAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<int?>()))
                .Callback((IDictionary<string, string> values, int? languageId) =>
                {
                    foreach (var (name, value) in values)
                        _resources[(languageId ?? 0, name)] = value;
                })
                .Returns(Task.CompletedTask);
            localizationService.Setup(x => x.DeleteLocaleStringResourceAsync(It.IsAny<LocaleStringResource>()))
                .Callback((LocaleStringResource resource) => _resources.Remove((resource.LanguageId, resource.ResourceName)))
                .Returns(Task.CompletedTask);

            _service = new HandlingFeeLabelService(languageService.Object, localizationService.Object);
        }

        /// <summary>
        /// Give a language nopCommerce's own wording for every managed resource, tagged by language
        /// </summary>
        private void AddLanguageWithOriginals(int languageId)
        {
            if (_languages.All(language => language.Id != languageId))
                _languages.Add(new Language { Id = languageId });

            foreach (var name in HandlingFeeLabelDefaults.ManagedResources)
                _resources[(languageId, name)] = $"original {languageId} {name}";
        }

        private void ShouldHoldOriginals(int languageId)
        {
            foreach (var name in HandlingFeeLabelDefaults.ManagedResources)
                _resources[(languageId, name)].Should().Be($"original {languageId} {name}");
        }

        /// <summary>
        /// What the configuration page's Save does with a non-empty label
        /// </summary>
        private async Task<string> SaveLabelAsync(string backup, string appliedLabel, string newLabel)
        {
            backup = string.IsNullOrEmpty(backup)
                ? await _service.CaptureOriginalsAsync()
                : await _service.MigrateBackupAsync(backup, appliedLabel);

            await _service.ApplyAsync(newLabel);
            return backup;
        }

        [Test]
        public async Task A_language_added_after_the_first_capture_gets_its_own_wording_back()
        {
            AddLanguageWithOriginals(1);
            var backup = await SaveLabelAsync(null, null, LABEL);

            //the merchant adds French and imports its pack, then saves again with the label unchanged
            AddLanguageWithOriginals(2);
            backup = await SaveLabelAsync(backup, LABEL, LABEL);

            _resources[(2, HandlingFeeLabelDefaults.CART_TOTALS)].Should().Be(LABEL);

            await _service.RestoreAsync(backup);

            ShouldHoldOriginals(1);
            ShouldHoldOriginals(2);
        }

        [Test]
        public async Task A_resource_the_language_never_had_is_deleted_on_restore_not_left_holding_the_label()
        {
            AddLanguageWithOriginals(1);
            //a partial language pack without the cart totals resource
            _resources.Remove((1, HandlingFeeLabelDefaults.CART_TOTALS));

            var backup = await SaveLabelAsync(null, null, LABEL);
            _resources[(1, HandlingFeeLabelDefaults.CART_TOTALS)].Should().Be(LABEL); //applying created it

            await _service.RestoreAsync(backup);

            _resources.Should().NotContainKey((1, HandlingFeeLabelDefaults.CART_TOTALS));
        }

        [Test]
        public async Task Migrating_never_records_the_applied_label_as_an_original()
        {
            //a backup that predates absent-tracking: the label was applied over a resource the
            //language never had, and the backup holds no entry for it
            AddLanguageWithOriginals(1);
            _resources.Remove((1, HandlingFeeLabelDefaults.CART_TOTALS));
            var oldStyleBackup = HandlingFeeLabelDefaults.SerialiseBackup(
                HandlingFeeLabelDefaults.ManagedResources
                    .Where(name => name != HandlingFeeLabelDefaults.CART_TOTALS)
                    .Select(name => new LabelBackupEntry { LanguageId = 1, ResourceName = name, Value = $"original 1 {name}" }));
            await _service.ApplyAsync(LABEL);

            var backup = await SaveLabelAsync(oldStyleBackup, LABEL, "Handling");

            HandlingFeeLabelDefaults.DeserialiseBackup(backup)
                .Should().NotContain(entry => entry.ResourceName == HandlingFeeLabelDefaults.CART_TOTALS);
        }

        [Test]
        public async Task Restoring_a_backup_captured_before_absent_tracking_still_works()
        {
            AddLanguageWithOriginals(1);
            var json = "[{\"LanguageId\":1,\"ResourceName\":\"" + HandlingFeeLabelDefaults.CART_TOTALS + "\",\"Value\":\"Payment fee\"}]";
            await _service.ApplyAsync(LABEL);

            await _service.RestoreAsync(json);

            _resources[(1, HandlingFeeLabelDefaults.CART_TOTALS)].Should().Be("Payment fee");
        }
    }
}
