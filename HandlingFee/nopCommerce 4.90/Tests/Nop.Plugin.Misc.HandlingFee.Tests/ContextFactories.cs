using Moq;
using Nop.Core;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    /// <summary>
    /// Builds a <see cref="HandlingFeeContextFactory"/> from whichever mocks a test cares about;
    /// every other dependency is a bare mock
    /// </summary>
    public static class ContextFactories
    {
        public static HandlingFeeContextFactory Build(ICustomerService customerService = null,
            IShoppingCartService shoppingCartService = null,
            IStoreContext storeContext = null,
            ICategoryService categoryService = null,
            IAddressService addressService = null,
            IGenericAttributeService genericAttributeService = null)
        {
            return new HandlingFeeContextFactory(
                addressService ?? new Mock<IAddressService>().Object,
                categoryService ?? new Mock<ICategoryService>().Object,
                customerService ?? new Mock<ICustomerService>().Object,
                genericAttributeService ?? new Mock<IGenericAttributeService>().Object,
                shoppingCartService ?? new Mock<IShoppingCartService>().Object,
                storeContext ?? new Mock<IStoreContext>().Object);
        }
    }
}
