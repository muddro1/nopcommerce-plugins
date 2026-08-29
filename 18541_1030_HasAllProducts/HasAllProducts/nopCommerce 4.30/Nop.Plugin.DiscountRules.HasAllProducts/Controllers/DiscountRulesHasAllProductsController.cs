using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Discounts;
using Nop.Plugin.DiscountRules.HasAllProducts.Models;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Discounts;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Factories;
using Nop.Web.Areas.Admin.Models.Catalog;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.DiscountRules.HasAllProducts.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.Admin)]
    [AutoValidateAntiforgeryToken]
    public class DiscountRulesHasAllProductsController : BasePluginController
    {
        #region Fields

        private readonly IDiscountService _discountService;
        private readonly IPermissionService _permissionService;
        private readonly IProductModelFactory _productModelFactory;
        private readonly IProductService _productService;
        private readonly ISettingService _settingService;

        #endregion

        #region Ctor

        public DiscountRulesHasAllProductsController(IDiscountService discountService,
            IPermissionService permissionService,
            IProductModelFactory productModelFactory,
            IProductService productService,
            ISettingService settingService)
        {
            _discountService = discountService;
            _permissionService = permissionService;
            _productModelFactory = productModelFactory;
            _productService = productService;
            _settingService = settingService;
        }

        #endregion

        #region Methods

        public IActionResult Configure(int discountId, int? discountRequirementId)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageDiscounts))
                return Content("Access denied");

            var discount = _discountService.GetDiscountById(discountId);
            if (discount == null)
                throw new ArgumentException("Discount could not be loaded");

            //check whether the discount requirement exists
            if (discountRequirementId.HasValue && _discountService.GetDiscountRequirementById(discountRequirementId.Value) is null)
                return Content("Failed to load requirement.");

            var restrictedProductIds = _settingService.GetSettingByKey<string>(string.Format(DiscountRequirementDefaults.SETTINGS_KEY, discountRequirementId ?? 0));

            var model = new RequirementModel
            {
                RequirementId = discountRequirementId ?? 0,
                DiscountId = discountId,
                ProductIds = restrictedProductIds
            };

            //add a prefix
            ViewData.TemplateInfo.HtmlFieldPrefix = string.Format(DiscountRequirementDefaults.HTML_FIELD_PREFIX, discountRequirementId ?? 0);

            return View("~/Plugins/DiscountRules.HasAllProducts/Views/Configure.cshtml", model);
        }

        [HttpPost]
        public IActionResult Configure(RequirementModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageDiscounts))
                return Content("Access denied");

            if (ModelState.IsValid)
            {
                //load the discount
                var discount = _discountService.GetDiscountById(model.DiscountId);
                if (discount == null)
                    return NotFound(new { Errors = new[] { "Discount could not be loaded" } });

                //get the discount requirement
                var discountRequirement = _discountService.GetDiscountRequirementById(model.RequirementId);

                //the discount requirement does not exist, so create a new one
                if (discountRequirement == null)
                {
                    discountRequirement = new DiscountRequirement
                    {
                        DiscountId = discount.Id,
                        DiscountRequirementRuleSystemName = DiscountRequirementDefaults.SYSTEM_NAME
                    };

                    _discountService.InsertDiscountRequirement(discountRequirement);
                }

                //save restricted customer role identifier
                _settingService.SetSetting(string.Format(DiscountRequirementDefaults.SETTINGS_KEY, discountRequirement.Id), model.ProductIds);

                return Ok(new { NewRequirementId = discountRequirement.Id });
            }

            return BadRequest(new { Errors = GetErrorsFromModelState() });
        }

        public IActionResult ProductAddPopup(string btnId, string productIdsInput)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageProducts))
                return AccessDeniedView();

            //prepare model
            var model = _productModelFactory.PrepareProductSearchModel(new ProductSearchModel());

            ViewBag.productIdsInput = productIdsInput;
            ViewBag.btnId = btnId;

            return View("~/Plugins/DiscountRules.HasAllProducts/Views/ProductAddPopup.cshtml", model);
        }

        [HttpPost]
        public IActionResult LoadProductFriendlyNames(string productIds)
        {
            var result = "";

            if (!_permissionService.Authorize(StandardPermissionProvider.ManageProducts))
                return Json(new { Text = result });

            if (string.IsNullOrWhiteSpace(productIds))
                return Json(new { Text = result });

            var ids = new List<int>();
            var rangeArray = productIds
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToList();

            //we support three ways of specifying products:
            //1. The comma-separated list of product identifiers (e.g. 77, 123, 156).
            //2. The comma-separated list of product identifiers with quantities.
            //      {Product ID}:{Quantity}. For example, 77:1, 123:2, 156:3
            //3. The comma-separated list of product identifiers with quantity range.
            //      {Product ID}:{Min quantity}-{Max quantity}. For example, 77:1-3, 123:2-5, 156:3-8
            foreach (var productQuantityPair in rangeArray)
            {
                var temp = productQuantityPair;

                //we do not display specified quantities and ranges
                //so let's parse only product names (before : sign)
                if (productQuantityPair.Contains(":"))
                    temp = productQuantityPair.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries)[0];

                if (int.TryParse(temp, out var productId))
                    ids.Add(productId);
            }

            var products = _productService.GetProductsByIds(ids.ToArray());
            result = string.Join(", ", products.Select(p => p.Name));

            return Json(new { Text = result });
        }

        #endregion

        #region Utilities

        private IEnumerable<string> GetErrorsFromModelState()
        {
            return ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage));
        }

        #endregion
    }
}