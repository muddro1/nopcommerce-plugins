using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Plugin.Misc.HandlingFee.Models;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Shipping;
using Nop.Web.Areas.Admin.Factories;
using Nop.Web.Areas.Admin.Models.Catalog;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.HandlingFee.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.ADMIN)]
    [AutoValidateAntiforgeryToken]
    public class MiscHandlingFeeController : BasePluginController
    {
        #region Constants

        private const string VIEW_PATH = "~/Plugins/Misc.HandlingFee/Views/";
        private const string PROVIDER_PREFIX = "provider:";
        private const string METHOD_PREFIX = "method:";

        #endregion

        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly HandlingFeeLabelService _labelService;
        private readonly ICategoryService _categoryService;
        private readonly ICountryService _countryService;
        private readonly ICurrencyService _currencyService;
        private readonly ICustomerService _customerService;
        private readonly ILocalizationService _localizationService;
        private readonly INotificationService _notificationService;
        private readonly IPriceCalculationService _priceCalculationService;
        private readonly IPriceFormatter _priceFormatter;
        private readonly IProductModelFactory _productModelFactory;
        private readonly IProductService _productService;
        private readonly ISettingService _settingService;
        private readonly IShippingPluginManager _shippingPluginManager;
        private readonly IShippingMethodsService _shippingMethodsService;
        private readonly IStoreContext _storeContext;

        #endregion

        #region Ctor

        public MiscHandlingFeeController(CurrencySettings currencySettings,
            HandlingFeeLabelService labelService,
            ICategoryService categoryService,
            ICountryService countryService,
            ICurrencyService currencyService,
            ICustomerService customerService,
            ILocalizationService localizationService,
            INotificationService notificationService,
            IPriceCalculationService priceCalculationService,
            IPriceFormatter priceFormatter,
            IProductModelFactory productModelFactory,
            IProductService productService,
            ISettingService settingService,
            IShippingPluginManager shippingPluginManager,
            IShippingMethodsService shippingMethodsService,
            IStoreContext storeContext)
        {
            _currencySettings = currencySettings;
            _labelService = labelService;
            _categoryService = categoryService;
            _countryService = countryService;
            _currencyService = currencyService;
            _customerService = customerService;
            _localizationService = localizationService;
            _notificationService = notificationService;
            _priceCalculationService = priceCalculationService;
            _priceFormatter = priceFormatter;
            _productModelFactory = productModelFactory;
            _productService = productService;
            _settingService = settingService;
            _shippingPluginManager = shippingPluginManager;
            _shippingMethodsService = shippingMethodsService;
            _storeContext = storeContext;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Copy the form onto a settings object. Shared by Save and the live preview, so the
        /// preview always evaluates exactly what Save would store.
        /// </summary>
        private static void ApplyModel(ConfigurationModel model, HandlingFeeSettings settings)
        {
            settings.Enabled = model.Enabled;
            settings.FeeType = Enum.IsDefined(typeof(HandlingFeeType), model.FeeType) ? (HandlingFeeType)model.FeeType : HandlingFeeType.Flat;
            settings.ThresholdAmount = model.ThresholdAmount;
            settings.FeeAmount = model.FeeAmount;
            settings.FeePercent = model.FeePercent;
            settings.FeeMinimum = model.FeeMinimum;
            settings.FeeMaximum = model.FeeMaximum;
            settings.TiersJson = HandlingFeeRule.SerialiseTiers(HandlingFeeRule.ParseTiers(model.TiersJson));
            settings.SubtotalBasis = Enum.IsDefined(typeof(HandlingFeeSubtotalBasis), model.SubtotalBasis)
                ? (HandlingFeeSubtotalBasis)model.SubtotalBasis
                : HandlingFeeSubtotalBasis.AfterDiscounts;
            settings.RequireShippedItem = model.RequireShippedItem;
            settings.SuppressWhenShippingCharged = model.SuppressWhenShippingCharged;
            settings.ShippingMethodsJson = HandlingFeeRule.SerialiseTokens(model.SelectedShippingMethods);
            settings.CountryIds = HandlingFeeRule.JoinIds(model.SelectedCountryIds);
            settings.ExemptCustomerRoleIds = HandlingFeeRule.JoinIds(model.SelectedExemptCustomerRoleIds);
            settings.ExclusionMode = Enum.IsDefined(typeof(HandlingFeeExclusionMode), model.ExclusionMode)
                ? (HandlingFeeExclusionMode)model.ExclusionMode
                : HandlingFeeExclusionMode.WaiveFee;
            settings.ExcludedCategoryIds = HandlingFeeRule.JoinIds(model.SelectedExcludedCategoryIds);
            settings.ExcludedProductIds = HandlingFeeRule.JoinIds(HandlingFeeRule.ParseIds(model.ExcludedProductIds));
        }

        private static ConfigurationModel ToModel(HandlingFeeSettings settings, int storeScope)
        {
            return new ConfigurationModel
            {
                ActiveStoreScopeConfiguration = storeScope,
                Label = settings.Label,
                Enabled = settings.Enabled,
                FeeType = (int)settings.FeeType,
                ThresholdAmount = settings.ThresholdAmount,
                FeeAmount = settings.FeeAmount,
                FeePercent = settings.FeePercent,
                FeeMinimum = settings.FeeMinimum,
                FeeMaximum = settings.FeeMaximum,
                TiersJson = HandlingFeeRule.SerialiseTiers(HandlingFeeRule.ParseTiers(settings.TiersJson)),
                SubtotalBasis = (int)settings.SubtotalBasis,
                RequireShippedItem = settings.RequireShippedItem,
                SuppressWhenShippingCharged = settings.SuppressWhenShippingCharged,
                SelectedShippingMethods = HandlingFeeRule.ParseTokens(settings.ShippingMethodsJson),
                SelectedCountryIds = HandlingFeeRule.ParseIds(settings.CountryIds).ToList(),
                SelectedExemptCustomerRoleIds = HandlingFeeRule.ParseIds(settings.ExemptCustomerRoleIds).ToList(),
                ExclusionMode = (int)settings.ExclusionMode,
                SelectedExcludedCategoryIds = HandlingFeeRule.ParseIds(settings.ExcludedCategoryIds).ToList(),
                ExcludedProductIds = settings.ExcludedProductIds
            };
        }

        /// <summary>
        /// Fill the select lists, and return the display names the rule summary needs
        /// </summary>
        private async Task<HandlingFeeRuleNames> PrepareListsAsync(ConfigurationModel model)
        {
            //shipping: every provider as a whole ("UPS (any service)"), then every named method
            var shippingNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in await _shippingPluginManager.LoadAllPluginsAsync())
                shippingNames[PROVIDER_PREFIX + provider.PluginDescriptor.SystemName] = $"{provider.PluginDescriptor.FriendlyName} (any service)";
            foreach (var method in await _shippingMethodsService.GetAllShippingMethodsAsync())
                shippingNames[METHOD_PREFIX + method.Name] = method.Name;

            //a stored token whose provider was uninstalled or method renamed stays visible, so it
            //is never silently dropped on the next save
            foreach (var token in model.SelectedShippingMethods.Where(token => !shippingNames.ContainsKey(token)))
                shippingNames[token] = $"{token} (no longer available)";

            model.AvailableShippingMethods = shippingNames
                .Select(pair => new SelectListItem(pair.Value, pair.Key, model.SelectedShippingMethods.Contains(pair.Key)))
                .ToList();

            var countries = await _countryService.GetAllCountriesAsync(showHidden: true);
            model.AvailableCountries = countries
                .Select(country => new SelectListItem(country.Name, country.Id.ToString(), model.SelectedCountryIds.Contains(country.Id)))
                .ToList();

            var roles = await _customerService.GetAllCustomerRolesAsync(true);
            model.AvailableCustomerRoles = roles
                .Select(role => new SelectListItem(role.Name, role.Id.ToString(), model.SelectedExemptCustomerRoleIds.Contains(role.Id)))
                .ToList();

            var categories = await _categoryService.GetAllCategoriesAsync(showHidden: true);
            var categoryNames = new Dictionary<int, string>();
            foreach (var category in categories)
                categoryNames[category.Id] = await _categoryService.GetFormattedBreadCrumbAsync(category, categories);
            model.AvailableCategories = categoryNames
                .OrderBy(pair => pair.Value)
                .Select(pair => new SelectListItem(pair.Value, pair.Key.ToString(), model.SelectedExcludedCategoryIds.Contains(pair.Key)))
                .ToList();

            var products = await _productService.GetProductsByIdsAsync(HandlingFeeRule.ParseIds(model.ExcludedProductIds).ToArray());

            return new HandlingFeeRuleNames
            {
                ShippingMethods = shippingNames,
                Countries = countries.ToDictionary(country => country.Id, country => country.Name),
                Roles = roles.ToDictionary(role => role.Id, role => role.Name),
                Categories = categoryNames,
                Products = products.ToDictionary(product => product.Id, product => product.Name)
            };
        }

        /// <summary>
        /// Format every amount the summary or result can mention, up front, so the synchronous
        /// describer can look them up. All settings amounts are in the primary store currency,
        /// whatever the admin's working currency is.
        /// </summary>
        private async Task<Func<decimal, string>> GetMoneyFormatterAsync(HandlingFeeSettings settings, HandlingFeeDecision decision)
        {
            var primaryCurrency = await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId);

            var amounts = new[] { settings.ThresholdAmount, settings.FeeAmount, settings.FeeMinimum, settings.FeeMaximum, decision.Fee, decision.MeasuredSubtotal }
                .Concat(HandlingFeeRule.ParseTiers(settings.TiersJson).SelectMany(tier => new[] { tier.UpTo, tier.Fee }))
                .Distinct();

            var formatted = new Dictionary<decimal, string>();
            foreach (var amount in amounts)
                formatted[amount] = await _priceFormatter.FormatPriceAsync(amount, true, primaryCurrency);

            return amount => formatted.TryGetValue(amount, out var text) ? text : amount.ToString("0.00");
        }

        #endregion

        #region Methods

        [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
        public async Task<IActionResult> Configure()
        {
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var settings = await _settingService.LoadSettingAsync<HandlingFeeSettings>(storeScope);

            var model = ToModel(settings, storeScope);

            if (storeScope > 0)
            {
                model.Enabled_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.Enabled, storeScope);
                model.FeeType_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.FeeType, storeScope);
                model.ThresholdAmount_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ThresholdAmount, storeScope);
                model.FeeAmount_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.FeeAmount, storeScope);
                model.FeePercent_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.FeePercent, storeScope);
                model.FeeMinimum_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.FeeMinimum, storeScope);
                model.FeeMaximum_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.FeeMaximum, storeScope);
                model.TiersJson_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.TiersJson, storeScope);
                model.SubtotalBasis_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.SubtotalBasis, storeScope);
                model.RequireShippedItem_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.RequireShippedItem, storeScope);
                model.SuppressWhenShippingCharged_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.SuppressWhenShippingCharged, storeScope);
                model.SelectedShippingMethods_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ShippingMethodsJson, storeScope);
                model.SelectedCountryIds_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.CountryIds, storeScope);
                model.SelectedExemptCustomerRoleIds_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ExemptCustomerRoleIds, storeScope);
                model.ExclusionMode_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ExclusionMode, storeScope);
                model.SelectedExcludedCategoryIds_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ExcludedCategoryIds, storeScope);
                model.ExcludedProductIds_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ExcludedProductIds, storeScope);
            }

            await PrepareListsAsync(model);

            return View(VIEW_PATH + "Configure.cshtml", model);
        }

        [HttpPost]
        [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();

            if (!ModelState.IsValid)
            {
                //redisplay what was entered, with the errors, rather than reloading the saved values
                model.ActiveStoreScopeConfiguration = storeScope;
                await PrepareListsAsync(model);
                return View(VIEW_PATH + "Configure.cshtml", model);
            }

            var settings = await _settingService.LoadSettingAsync<HandlingFeeSettings>(storeScope);

            var newLabel = (model.Label ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(newLabel))
            {
                //capture the originals once, before the first overwrite, so they can be put back
                if (string.IsNullOrEmpty(settings.LabelBackupJson))
                    settings.LabelBackupJson = await _labelService.CaptureOriginalsAsync();

                await _labelService.ApplyAsync(newLabel);
            }
            else if (!string.IsNullOrEmpty(settings.LabelBackupJson))
            {
                //label cleared: put nopCommerce's own wording back
                await _labelService.RestoreAsync(settings.LabelBackupJson);
                settings.LabelBackupJson = string.Empty;
            }

            settings.Label = newLabel;

            ApplyModel(model, settings);

            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.Enabled, model.Enabled_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.FeeType, model.FeeType_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ThresholdAmount, model.ThresholdAmount_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.FeeAmount, model.FeeAmount_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.FeePercent, model.FeePercent_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.FeeMinimum, model.FeeMinimum_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.FeeMaximum, model.FeeMaximum_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.TiersJson, model.TiersJson_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.SubtotalBasis, model.SubtotalBasis_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.RequireShippedItem, model.RequireShippedItem_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.SuppressWhenShippingCharged, model.SuppressWhenShippingCharged_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ShippingMethodsJson, model.SelectedShippingMethods_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.CountryIds, model.SelectedCountryIds_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ExemptCustomerRoleIds, model.SelectedExemptCustomerRoleIds_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ExclusionMode, model.ExclusionMode_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ExcludedCategoryIds, model.SelectedExcludedCategoryIds_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ExcludedProductIds, model.ExcludedProductIds_OverrideForStore, storeScope, false);

            //Label and LabelBackupJson are global, not per store, because the locale
            //resources they drive are per language
            await _settingService.SaveSettingAsync(settings, x => x.Label, 0, false);
            await _settingService.SaveSettingAsync(settings, x => x.LabelBackupJson, 0, false);

            await _settingService.ClearCacheAsync();

            _notificationService.SuccessNotification(
                await _localizationService.GetResourceAsync("Plugins.Misc.HandlingFee.Configuration.Saved"));

            return await Configure();
        }

        /// <summary>
        /// The live rule summary and "try a cart" result, for the form as it stands - unsaved.
        /// Runs the same <see cref="HandlingFeeRule"/> checkout runs; nothing is stored.
        /// </summary>
        [HttpPost]
        [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
        public async Task<IActionResult> PreviewRule(ConfigurationModel model, [Bind(Prefix = "Test")] RulePreviewCartModel test)
        {
            //validation errors are for Save to report; the preview describes whatever is entered
            ModelState.Clear();

            var settings = new HandlingFeeSettings();
            ApplyModel(model, settings);

            var names = await PrepareListsAsync(model);

            test ??= new RulePreviewCartModel();
            var token = test.ShippingMethod ?? string.Empty;
            var context = new HandlingFeeContext
            {
                SubtotalAfterDiscounts = test.Subtotal,
                SubtotalBeforeOrderDiscounts = test.SubtotalBeforeOrderDiscounts ?? test.Subtotal,
                ShippingTotal = test.Shipping,
                HasShippedItem = test.HasShippedItem,
                HasExcludedItem = test.HasExcludedItem || test.OnlyExcludedItems,
                OnlyExcludedItems = test.OnlyExcludedItems,
                CustomerRoleIds = test.RoleId > 0 ? new[] { test.RoleId } : Array.Empty<int>(),
                CountryId = test.CountryId > 0 ? test.CountryId : null,
                ShippingProviderSystemName = token.StartsWith(PROVIDER_PREFIX, StringComparison.OrdinalIgnoreCase) ? token[PROVIDER_PREFIX.Length..] : null,
                ShippingOptionName = token.StartsWith(METHOD_PREFIX, StringComparison.OrdinalIgnoreCase) ? token[METHOD_PREFIX.Length..] : null
            };

            var decision = HandlingFeeRule.Evaluate(settings, context);

            //the same currency rounding the payment service applies, so the preview matches checkout
            if (HandlingFeeRule.NeedsCurrencyRounding(settings, decision))
                decision = decision with { Fee = await _priceCalculationService.RoundPriceAsync(decision.Fee) };
            var money = await GetMoneyFormatterAsync(settings, decision);

            return Json(new
            {
                Summary = HandlingFeeRuleDescriber.Describe(settings, names, money),
                Result = HandlingFeeRuleDescriber.Explain(decision, money),
                Applied = decision.Reason == HandlingFeeReason.Applied
            });
        }

        [CheckPermission(StandardPermission.Catalog.PRODUCTS_VIEW)]
        public async Task<IActionResult> ProductAddPopup(string btnId, string productIdsInput)
        {
            var model = await _productModelFactory.PrepareProductSearchModelAsync(new ProductSearchModel());

            ViewBag.productIdsInput = productIdsInput;
            ViewBag.btnId = btnId;

            return View(VIEW_PATH + "ProductAddPopup.cshtml", model);
        }

        [HttpPost]
        [CheckPermission(StandardPermission.Catalog.PRODUCTS_VIEW)]
        public async Task<IActionResult> LoadProductFriendlyNames(string productIds)
        {
            var products = await _productService.GetProductsByIdsAsync(HandlingFeeRule.ParseIds(productIds).ToArray());

            return Json(new { Text = string.Join(", ", products.Select(product => product.Name)) });
        }

        #endregion
    }
}
