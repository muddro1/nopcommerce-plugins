using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Plugin.Misc.HandlingFee;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Orders;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    [TestFixture]
    public class HandlingFeeContextFactoryTests
    {
        private Customer _customer;
        private Mock<ICustomerService> _customerService;
        private Mock<IShoppingCartService> _cartService;
        private Mock<ICategoryService> _categoryService;
        private Mock<IAddressService> _addressService;
        private Mock<IGenericAttributeService> _genericAttributeService;
        private Mock<IStoreContext> _storeContext;
        private Mock<IOrderTotalCalculationService> _totals;

        //the cart the totals service last measured, so tests can see which items were counted
        private List<IList<ShoppingCartItem>> _measuredCarts;

        private static readonly ShoppingCartItem Shirt = new() { Id = 1, ProductId = 10 };
        private static readonly ShoppingCartItem GiftCard = new() { Id = 2, ProductId = 20 };

        [SetUp]
        public void SetUp()
        {
            _customer = new Customer { Id = 5, ShippingAddressId = 77 };
            _measuredCarts = new List<IList<ShoppingCartItem>>();

            _customerService = new Mock<ICustomerService>();
            _customerService.Setup(x => x.GetShoppingCartCustomerAsync(It.IsAny<IList<ShoppingCartItem>>())).ReturnsAsync(_customer);

            //only the shirt ships
            _cartService = new Mock<IShoppingCartService>();
            _cartService.Setup(x => x.ShoppingCartRequiresShippingAsync(It.IsAny<IList<ShoppingCartItem>>()))
                .ReturnsAsync((IList<ShoppingCartItem> cart) => cart.Contains(Shirt));

            _categoryService = new Mock<ICategoryService>();
            _addressService = new Mock<IAddressService>();
            _genericAttributeService = new Mock<IGenericAttributeService>();
            _storeContext = new Mock<IStoreContext>();
            _storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Store { Id = 1 });

            //each item costs 10 before order discounts and 9 after
            _totals = new Mock<IOrderTotalCalculationService>();
            _totals.Setup(x => x.GetShoppingCartSubTotalAsync(It.IsAny<IList<ShoppingCartItem>>(), false))
                .ReturnsAsync((IList<ShoppingCartItem> cart, bool _) =>
                {
                    _measuredCarts.Add(cart);
                    return (cart.Count * 1m, new List<Discount>(), cart.Count * 10m, cart.Count * 9m, new SortedDictionary<decimal, decimal>());
                });
            _totals.Setup(x => x.GetShoppingCartShippingTotalAsync(It.IsAny<IList<ShoppingCartItem>>(), false))
                .ReturnsAsync(((decimal?)0m, 0m, new List<Discount>()));
        }

        private Task<HandlingFeeContext> CreateAsync(HandlingFeeSettings settings, params ShoppingCartItem[] cart)
        {
            var factory = ContextFactories.Build(_customerService.Object, _cartService.Object, _storeContext.Object,
                _categoryService.Object, _addressService.Object, _genericAttributeService.Object);

            return factory.CreateAsync(cart.ToList(), settings, _totals.Object);
        }

        [Test]
        public async Task Default_settings_make_no_lookups_beyond_what_200_made()
        {
            var context = await CreateAsync(new HandlingFeeSettings(), Shirt, GiftCard);

            context.Should().BeEquivalentTo(new HandlingFeeContext
            {
                SubtotalAfterDiscounts = 18m,
                SubtotalBeforeOrderDiscounts = 20m,
                ShippingTotal = 0m,
                HasShippedItem = true
            });

            _categoryService.VerifyNoOtherCalls();
            _addressService.VerifyNoOtherCalls();
            _genericAttributeService.VerifyNoOtherCalls();
            _customerService.Verify(x => x.GetCustomerRoleIdsAsync(It.IsAny<Customer>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public async Task An_excluded_product_is_flagged_and_still_counted_in_waive_mode()
        {
            var context = await CreateAsync(new HandlingFeeSettings { ExcludedProductIds = "20" }, Shirt, GiftCard);

            context.HasExcludedItem.Should().BeTrue();
            context.SubtotalAfterDiscounts.Should().Be(18m);
        }

        [Test]
        public async Task A_product_in_a_subcategory_of_an_excluded_category_is_excluded()
        {
            _categoryService.Setup(x => x.GetChildCategoryIdsAsync(3, 0, true)).ReturnsAsync(new List<int> { 31, 32 });
            _categoryService.Setup(x => x.GetProductCategoryIdsAsync(It.IsAny<int[]>()))
                .ReturnsAsync(new Dictionary<int, int[]> { [10] = new[] { 1 }, [20] = new[] { 32 } });

            var settings = new HandlingFeeSettings { ExcludedCategoryIds = "3", ExclusionMode = HandlingFeeExclusionMode.LeaveOut };
            var context = await CreateAsync(settings, Shirt, GiftCard);

            context.HasExcludedItem.Should().BeTrue();
            context.SubtotalAfterDiscounts.Should().Be(9m); //the shirt alone
            _measuredCarts.Single().Should().Equal(Shirt);
        }

        [Test]
        public async Task Leaving_out_the_only_shipped_item_leaves_no_shipped_item()
        {
            var settings = new HandlingFeeSettings { ExcludedProductIds = "10", ExclusionMode = HandlingFeeExclusionMode.LeaveOut };
            var context = await CreateAsync(settings, Shirt, GiftCard);

            context.HasShippedItem.Should().BeFalse();
            context.SubtotalAfterDiscounts.Should().Be(9m); //the gift card alone
            context.OnlyExcludedItems.Should().BeFalse();
        }

        [Test]
        public async Task A_cart_of_only_excluded_items_measures_nothing_in_leave_out_mode()
        {
            var settings = new HandlingFeeSettings { ExcludedProductIds = "10,20", ExclusionMode = HandlingFeeExclusionMode.LeaveOut };
            var context = await CreateAsync(settings, Shirt, GiftCard);

            context.HasShippedItem.Should().BeFalse();
            context.SubtotalAfterDiscounts.Should().Be(0m);
            context.OnlyExcludedItems.Should().BeTrue();
            _measuredCarts.Should().BeEmpty();
        }

        [Test]
        public async Task Country_comes_from_the_shipping_address_when_a_country_filter_is_set()
        {
            _addressService.Setup(x => x.GetAddressByIdAsync(77)).ReturnsAsync(new Address { Id = 77, CountryId = 4 });

            (await CreateAsync(new HandlingFeeSettings { CountryIds = "1" }, Shirt)).CountryId.Should().Be(4);
        }

        [Test]
        public async Task No_shipping_address_yet_leaves_the_country_unknown()
        {
            _customer.ShippingAddressId = null;

            (await CreateAsync(new HandlingFeeSettings { CountryIds = "1" }, Shirt)).CountryId.Should().BeNull();
        }

        [Test]
        public async Task The_selected_shipping_option_is_read_when_a_method_filter_is_set()
        {
            _genericAttributeService.Setup(x => x.GetAttributeAsync<ShippingOption>(_customer,
                    NopCustomerDefaults.SelectedShippingOptionAttribute, 1, null))
                .ReturnsAsync(new ShippingOption { Name = "UPS Ground", ShippingRateComputationMethodSystemName = "Shipping.UPS" });

            var settings = new HandlingFeeSettings { ShippingMethodsJson = HandlingFeeRule.SerialiseTokens(new[] { "provider:Shipping.UPS" }) };
            var context = await CreateAsync(settings, Shirt);

            context.ShippingProviderSystemName.Should().Be("Shipping.UPS");
            context.ShippingOptionName.Should().Be("UPS Ground");
        }

        [Test]
        public async Task Customer_roles_are_read_when_an_exempt_role_is_set()
        {
            _customerService.Setup(x => x.GetCustomerRoleIdsAsync(_customer, false)).ReturnsAsync(new[] { 3, 9 });

            (await CreateAsync(new HandlingFeeSettings { ExemptCustomerRoleIds = "9" }, Shirt)).CustomerRoleIds.Should().Equal(3, 9);
        }
    }
}
