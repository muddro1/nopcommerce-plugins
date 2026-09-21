using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Discounts;
using Nop.Plugin.Misc.AttributeCombinationPricing.Factories;
using Nop.Services.Catalog;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Tax;
using Nop.Web.Models.Catalog;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Tests
{
    [TestFixture]
    public class AttributeCombinationProductModelFactoryTests
    {
        private static AttributeCombinationProductModelFactory Build(
            Mock<IPriceCalculationService> priceCalculationService,
            Mock<IProductAttributeService> productAttributeService,
            Mock<IProductAttributeParser> productAttributeParser,
            Mock<ITaxService> taxService,
            Mock<ICurrencyService> currencyService,
            Mock<IPriceFormatter> priceFormatter,
            Mock<IWorkContext> workContext)
        {
            //Every base-class dependency this test's code path never reaches is null.
            //Only the seven mocked above are touched by ApplyCorrectedAdjustmentsAsync.
            return new AttributeCombinationProductModelFactory(
                captchaSettings: null,
                catalogSettings: null,
                customerSettings: null,
                categoryService: null,
                currencyService: currencyService.Object,
                customerService: null,
                dateRangeService: null,
                dateTimeHelper: null,
                downloadService: null,
                genericAttributeService: null,
                localizationService: null,
                manufacturerService: null,
                permissionService: null,
                pictureService: null,
                priceCalculationService: priceCalculationService.Object,
                priceFormatter: priceFormatter.Object,
                productAttributeParser: productAttributeParser.Object,
                productAttributeService: productAttributeService.Object,
                productService: null,
                productTagService: null,
                productTemplateService: null,
                reviewTypeService: null,
                specificationAttributeService: null,
                staticCacheManager: null,
                storeContext: null,
                shoppingCartModelFactory: null,
                taxService: taxService.Object,
                urlRecordService: null,
                vendorService: null,
                webHelper: null,
                workContext: workContext.Object,
                mediaSettings: null,
                orderSettings: null,
                seoSettings: null,
                shippingSettings: null,
                vendorSettings: null);
        }

        [Test]
        public async Task Corrects_only_the_value_backed_by_an_override_combination()
        {
            var product = new Product { Id = 1 };
            var customer = new Customer { Id = 1 };
            var mapping = new ProductAttributeMapping { Id = 10, ProductId = product.Id };

            //Right-handed mount: has a matching combination with an Overridden price of 320,
            //against a base price of 299 -> corrected adjustment should be +21.
            var overriddenValue = new ProductAttributeValue
            {
                Id = 100,
                ProductAttributeMappingId = mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct,
                AssociatedProductId = 200
            };

            //Left-handed mount: also Associated-to-Product, but no matching combination exists ->
            //must be left exactly as core computed it.
            var plainValue = new ProductAttributeValue
            {
                Id = 101,
                ProductAttributeMappingId = mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct,
                AssociatedProductId = 201
            };

            //A Simple-type value on the same attribute -> must never be looked up at all.
            var simpleValue = new ProductAttributeValue
            {
                Id = 102,
                ProductAttributeMappingId = mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.Simple,
                PriceAdjustment = 5m
            };

            //Center mount: Associated-to-Product, and a matching combination IS found, but that
            //combination's OverriddenPrice is left null -> distinct from "no combination at all"
            //(the `combination?.OverriddenPrice` navigation still yields null), must be left
            //exactly as core computed it.
            var noOverrideValue = new ProductAttributeValue
            {
                Id = 103,
                ProductAttributeMappingId = mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct,
                AssociatedProductId = 202
            };

            var combination = new ProductAttributeCombination { OverriddenPrice = 320m };
            var combinationWithoutOverride = new ProductAttributeCombination();

            var priceCalculationService = new Mock<IPriceCalculationService>();
            priceCalculationService.Setup(x => x.GetFinalPriceAsync(product, customer, 0, true, 1))
                .ReturnsAsync((299m, 299m, 0m, new List<Discount>()));

            var productAttributeService = new Mock<IProductAttributeService>();
            productAttributeService.Setup(x => x.GetProductAttributeValuesAsync(mapping.Id))
                .ReturnsAsync(new List<ProductAttributeValue> { overriddenValue, plainValue, simpleValue, noOverrideValue });
            productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(mapping.Id))
                .ReturnsAsync(mapping);

            var productAttributeParser = new Mock<IProductAttributeParser>();
            productAttributeParser
                .Setup(x => x.AddProductAttribute(string.Empty, mapping, overriddenValue.Id.ToString(), null))
                .Returns("xml-overridden");
            productAttributeParser
                .Setup(x => x.AddProductAttribute(string.Empty, mapping, plainValue.Id.ToString(), null))
                .Returns("xml-plain");
            productAttributeParser
                .Setup(x => x.AddProductAttribute(string.Empty, mapping, noOverrideValue.Id.ToString(), null))
                .Returns("xml-no-override");
            productAttributeParser
                .Setup(x => x.FindProductAttributeCombinationAsync(product, "xml-overridden", true))
                .ReturnsAsync(combination);
            productAttributeParser
                .Setup(x => x.FindProductAttributeCombinationAsync(product, "xml-plain", true))
                .ReturnsAsync((ProductAttributeCombination)null);
            productAttributeParser
                .Setup(x => x.FindProductAttributeCombinationAsync(product, "xml-no-override", true))
                .ReturnsAsync(combinationWithoutOverride);

            //tax and currency pass through unchanged, so the assertions below can check exact figures
            var taxService = new Mock<ITaxService>();
            taxService.Setup(x => x.GetProductPriceAsync(product, It.IsAny<decimal>()))
                .Returns((Product _, decimal price) => Task.FromResult((price, 0m)));

            var currency = new Currency { Id = 1 };
            var workContext = new Mock<IWorkContext>();
            workContext.Setup(x => x.GetWorkingCurrencyAsync()).ReturnsAsync(currency);

            var currencyService = new Mock<ICurrencyService>();
            currencyService.Setup(x => x.ConvertFromPrimaryStoreCurrencyAsync(It.IsAny<decimal>(), currency))
                .Returns((decimal amount, Currency _) => Task.FromResult(amount));

            var priceFormatter = new Mock<IPriceFormatter>();
            priceFormatter.Setup(x => x.FormatPriceAsync(It.IsAny<decimal>(), false, false))
                .Returns((decimal amount, bool _, bool __) => Task.FromResult($"${amount:0.00}"));

            var model = new List<ProductDetailsModel.ProductAttributeModel>
            {
                new ProductDetailsModel.ProductAttributeModel
                {
                    Id = mapping.Id,
                    ProductId = product.Id,
                    ProductAttributeId = 5,
                    Values =
                    {
                        new ProductDetailsModel.ProductAttributeValueModel
                            { Id = overriddenValue.Id, Name = "Right-handed mount", PriceAdjustment = "+$50.00", PriceAdjustmentValue = 50m },
                        new ProductDetailsModel.ProductAttributeValueModel
                            { Id = plainValue.Id, Name = "Left-handed mount", PriceAdjustment = "+$45.00", PriceAdjustmentValue = 45m },
                        new ProductDetailsModel.ProductAttributeValueModel
                            { Id = simpleValue.Id, Name = "No mount", PriceAdjustment = "+$5.00", PriceAdjustmentValue = 5m },
                        new ProductDetailsModel.ProductAttributeValueModel
                            { Id = noOverrideValue.Id, Name = "Center mount", PriceAdjustment = "+$40.00", PriceAdjustmentValue = 40m }
                    }
                }
            };

            var factory = Build(priceCalculationService, productAttributeService, productAttributeParser,
                taxService, currencyService, priceFormatter, workContext);

            await factory.ApplyCorrectedAdjustmentsAsync(product, model, customer);

            var values = model[0].Values;
            Assert.That(values[0].PriceAdjustment, Is.EqualTo("+$21.00"));
            Assert.That(values[0].PriceAdjustmentValue, Is.EqualTo(21m));
            Assert.That(values[1].PriceAdjustment, Is.EqualTo("+$45.00"));
            Assert.That(values[1].PriceAdjustmentValue, Is.EqualTo(45m));
            Assert.That(values[2].PriceAdjustment, Is.EqualTo("+$5.00"));
            Assert.That(values[2].PriceAdjustmentValue, Is.EqualTo(5m));
            Assert.That(values[3].PriceAdjustment, Is.EqualTo("+$40.00"));
            Assert.That(values[3].PriceAdjustmentValue, Is.EqualTo(40m));
        }

        [Test]
        public async Task A_negative_corrected_delta_gets_a_minus_prefix()
        {
            var product = new Product { Id = 1 };
            var customer = new Customer { Id = 1 };
            var mapping = new ProductAttributeMapping { Id = 10, ProductId = product.Id };

            var value = new ProductAttributeValue
            {
                Id = 100,
                ProductAttributeMappingId = mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct,
                AssociatedProductId = 200
            };

            //base price 299, override 285 -> corrected adjustment is -14
            var combination = new ProductAttributeCombination { OverriddenPrice = 285m };

            var priceCalculationService = new Mock<IPriceCalculationService>();
            priceCalculationService.Setup(x => x.GetFinalPriceAsync(product, customer, 0, true, 1))
                .ReturnsAsync((299m, 299m, 0m, new List<Discount>()));

            var productAttributeService = new Mock<IProductAttributeService>();
            productAttributeService.Setup(x => x.GetProductAttributeValuesAsync(mapping.Id))
                .ReturnsAsync(new List<ProductAttributeValue> { value });
            productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(mapping.Id))
                .ReturnsAsync(mapping);

            var productAttributeParser = new Mock<IProductAttributeParser>();
            productAttributeParser
                .Setup(x => x.AddProductAttribute(string.Empty, mapping, value.Id.ToString(), null))
                .Returns("xml-value");
            productAttributeParser
                .Setup(x => x.FindProductAttributeCombinationAsync(product, "xml-value", true))
                .ReturnsAsync(combination);

            var taxService = new Mock<ITaxService>();
            taxService.Setup(x => x.GetProductPriceAsync(product, It.IsAny<decimal>()))
                .Returns((Product _, decimal price) => Task.FromResult((price, 0m)));

            var currency = new Currency { Id = 1 };
            var workContext = new Mock<IWorkContext>();
            workContext.Setup(x => x.GetWorkingCurrencyAsync()).ReturnsAsync(currency);

            var currencyService = new Mock<ICurrencyService>();
            currencyService.Setup(x => x.ConvertFromPrimaryStoreCurrencyAsync(It.IsAny<decimal>(), currency))
                .Returns((decimal amount, Currency _) => Task.FromResult(amount));

            var priceFormatter = new Mock<IPriceFormatter>();
            priceFormatter.Setup(x => x.FormatPriceAsync(It.IsAny<decimal>(), false, false))
                .Returns((decimal amount, bool _, bool __) => Task.FromResult($"${amount:0.00}"));

            var model = new List<ProductDetailsModel.ProductAttributeModel>
            {
                new ProductDetailsModel.ProductAttributeModel
                {
                    Id = mapping.Id,
                    Values = { new ProductDetailsModel.ProductAttributeValueModel { Id = value.Id, PriceAdjustment = "+$50.00", PriceAdjustmentValue = 50m } }
                }
            };

            var factory = Build(priceCalculationService, productAttributeService, productAttributeParser,
                taxService, currencyService, priceFormatter, workContext);

            await factory.ApplyCorrectedAdjustmentsAsync(product, model, customer);

            Assert.That(model[0].Values[0].PriceAdjustment, Is.EqualTo("-$14.00"));
            Assert.That(model[0].Values[0].PriceAdjustmentValue, Is.EqualTo(-14m));
        }
    }
}
