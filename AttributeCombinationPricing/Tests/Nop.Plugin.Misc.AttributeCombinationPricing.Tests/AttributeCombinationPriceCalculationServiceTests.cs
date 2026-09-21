using System.Threading.Tasks;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AttributeCombinationPricing.Services;
using Nop.Services.Catalog;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Tests
{
    [TestFixture]
    public class AttributeCombinationPriceCalculationServiceTests
    {
        private static AttributeCombinationPriceCalculationService Build(
            Mock<IProductAttributeParser> productAttributeParser,
            Mock<IProductAttributeService> productAttributeService)
        {
            //Every base-class dependency this test's code path never reaches is null - the override
            //only touches the two mocked here, plus GetFinalPriceAsync's own dependencies, which the
            //tests below avoid by always passing an explicit productPrice.
            return new AttributeCombinationPriceCalculationService(
                catalogSettings: null,
                currencySettings: null,
                categoryService: null,
                currencyService: null,
                customerService: null,
                discountService: null,
                manufacturerService: null,
                productAttributeParser: productAttributeParser.Object,
                productService: null,
                staticCacheManager: null,
                storeContext: null,
                productAttributeService: productAttributeService.Object);
        }

        [Test]
        public async Task Corrects_the_adjustment_when_a_combination_override_matches()
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

            //base price 299, override 320 -> corrected adjustment is +21
            var combination = new ProductAttributeCombination { OverriddenPrice = 320m };

            var productAttributeService = new Mock<IProductAttributeService>();
            productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(mapping.Id))
                .ReturnsAsync(mapping);

            var productAttributeParser = new Mock<IProductAttributeParser>();
            productAttributeParser
                .Setup(x => x.AddProductAttribute(string.Empty, mapping, value.Id.ToString(), null))
                .Returns("xml-value");
            productAttributeParser
                .Setup(x => x.FindProductAttributeCombinationAsync(product, "xml-value", true))
                .ReturnsAsync(combination);

            var service = Build(productAttributeParser, productAttributeService);

            //passing productPrice explicitly bypasses GetFinalPriceAsync's own (heavy, unrelated)
            //discount/tier-price computation, isolating this test to the override's own logic
            var adjustment = await service.GetProductAttributeValuePriceAdjustmentAsync(product, value, customer, productPrice: 299m);

            Assert.That(adjustment, Is.EqualTo(21m));
        }

        [Test]
        public async Task Falls_through_to_the_original_calculation_for_a_simple_value()
        {
            var product = new Product { Id = 1 };
            var customer = new Customer { Id = 1 };

            var value = new ProductAttributeValue
            {
                Id = 101,
                AttributeValueTypeId = (int)AttributeValueType.Simple,
                PriceAdjustmentUsePercentage = false,
                PriceAdjustment = 5m
            };

            var productAttributeService = new Mock<IProductAttributeService>();
            var productAttributeParser = new Mock<IProductAttributeParser>();

            var service = Build(productAttributeParser, productAttributeService);

            var adjustment = await service.GetProductAttributeValuePriceAdjustmentAsync(product, value, customer, productPrice: 299m);

            Assert.That(adjustment, Is.EqualTo(5m));
            //the override must never even look up a combination for a Simple-type value
            productAttributeParser.Verify(x => x.FindProductAttributeCombinationAsync(It.IsAny<Product>(), It.IsAny<string>(), true), Times.Never);
        }
    }
}
