using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using LinqToDB.Data;
using Nop.Data;
using Nop.Plugin.Misc.XpsShip.Domain;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Plugin.Misc.XpsShip.Models;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.XpsShip.Services;

/// <summary>A ship-enabled order line with everything the rules, the JSON and the shipment need</summary>
/// <param name="Sku">The SKU of the variant ordered (an attribute combination's own SKU when it has one), or null</param>
public record OrderLine(OrderItem Item, Product Product, string? Sku, int Remaining, StockState Stock, decimal? UnitWeightLb);

public record ExcludedOrder(int OrderId, string OrderNumber, string Reason);

public class OrderListResult
{
    public IList<XpsOrder> Orders { get; } = new List<XpsOrder>();
    public IList<ExcludedOrder> Excluded { get; } = new List<ExcludedOrder>();
}

/// <summary>
/// Picks the orders XPS should see (the spec's selection rules) and builds their JSON from stock nopCommerce entities
/// </summary>
public class XpsOrderBuilder
{
    private readonly IAddressService _addressService;
    private readonly ICountryService _countryService;
    private readonly ICustomerService _customerService;
    private readonly ILogger _logger;
    private readonly IMeasureService _measureService;
    private readonly INopDataProvider _dataProvider;
    private readonly IOrderService _orderService;
    private readonly IRepository<Order> _orderRepository;
    private readonly IPictureService _pictureService;
    private readonly IProductAttributeParser _productAttributeParser;
    private readonly IProductService _productService;
    private readonly IStateProvinceService _stateProvinceService;
    private readonly MeasureSettings _measureSettings;
    private readonly XpsShipDataService _dataService;
    private readonly XpsShipSettings _settings;

    // Pounds conversion, looked up once per request: null = the base weight is already pounds
    private MeasureWeight? _pounds;
    private bool _poundsResolved;

    public XpsOrderBuilder(IAddressService addressService,
        ICountryService countryService,
        ICustomerService customerService,
        ILogger logger,
        IMeasureService measureService,
        INopDataProvider dataProvider,
        IOrderService orderService,
        IRepository<Order> orderRepository,
        IPictureService pictureService,
        IProductAttributeParser productAttributeParser,
        IProductService productService,
        IStateProvinceService stateProvinceService,
        MeasureSettings measureSettings,
        XpsShipDataService dataService,
        XpsShipSettings settings)
    {
        _addressService = addressService;
        _countryService = countryService;
        _customerService = customerService;
        _logger = logger;
        _measureService = measureService;
        _dataProvider = dataProvider;
        _orderService = orderService;
        _orderRepository = orderRepository;
        _pictureService = pictureService;
        _productAttributeParser = productAttributeParser;
        _productService = productService;
        _stateProvinceService = stateProvinceService;
        _measureSettings = measureSettings;
        _dataService = dataService;
        _settings = settings;
    }

    /// <summary>
    /// Every order XPS should see right now, oldest first, plus the open orders left out and why.
    /// Orders whose special instruction says "do not ship" are put on hold here, the first time they're seen.
    /// </summary>
    public async Task<OrderListResult> BuildListAsync()
    {
        var result = new OrderListResult();

        // Queried directly, not through SearchOrdersAsync: its signature differs between 4.50 builds (MissingMethodException on the live store)
        var orderStatuses = new[] { OrderRules.OrderPending, OrderRules.OrderProcessing };
        var shippingStatuses = new[] { OrderRules.NotYetShipped, OrderRules.PartiallyShipped };
        var query = _orderRepository.Table
            .Where(o => !o.Deleted && orderStatuses.Contains(o.OrderStatusId) && shippingStatuses.Contains(o.ShippingStatusId));
        if (_settings.OrdersPlacedFromUtc is DateTime from)
            query = query.Where(o => o.CreatedOnUtc >= from);
        var candidates = await query.OrderBy(o => o.CreatedOnUtc).ToListAsync();

        var flags = (await _dataService.GetFlagsAsync(candidates.Select(o => o.OrderGuid))).ToLookup(f => f.OrderGuid);
        var ruleSettings = new RuleSettings(XpsFormat.SplitList(_settings.ShipBeforePaymentMethods),
            _settings.HideFullyBackorderedOrders, _settings.ExchangeSkuSuffix);
        var max = Math.Max(_settings.MaxOrdersPerList, 1);

        foreach (var order in candidates)
        {
            try
            {
                var lines = await LoadLinesAsync(order);
                var orderFlags = flags[order.OrderGuid].ToList();
                var hold = orderFlags.FirstOrDefault(f => f.Flag == OrderFlags.Hold);
                var snapshot = new OrderSnapshot(order.OrderStatusId, order.ShippingStatusId, order.PaymentStatusId,
                    order.PaymentMethodSystemName, order.PickupInStore, order.ShippingAddressId.HasValue,
                    InstructionsOf(order),
                    lines.Select(l => new LineSnapshot(l.Item.OrderItemGuid, l.Sku, l.Remaining, l.Stock)).ToList(),
                    hold?.Reason, hold != null,
                    orderFlags.Any(f => f.Flag == OrderFlags.HoldCleared),
                    orderFlags.Any(f => f.Flag == OrderFlags.ReleaseExchange));

                var decision = OrderRules.Evaluate(snapshot, ruleSettings);
                if (decision.AutoHold)
                {
                    await _dataService.InsertFlagAsync(new XpsShipOrderFlag
                    {
                        OrderGuid = order.OrderGuid,
                        CustomOrderNumber = order.CustomOrderNumber,
                        Flag = OrderFlags.Hold,
                        Reason = Instructions.ToPlainText(order.CheckoutAttributeDescription, " "),
                        CreatedBy = OrderFlags.AutoCreatedBy,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                }

                if (!decision.Include)
                {
                    result.Excluded.Add(new ExcludedOrder(order.Id, order.CustomOrderNumber, decision.Reason ?? string.Empty));
                    continue;
                }

                if (result.Orders.Count >= max)
                {
                    result.Excluded.Add(new ExcludedOrder(order.Id, order.CustomOrderNumber, $"over the {max}-order cap"));
                    continue;
                }

                result.Orders.Add(await MapAsync(order, lines));
            }
            catch (Exception ex)
            {
                // One broken order (a deleted product, a bad address) mustn't stop every other order reaching XPS
                await _logger.ErrorAsync($"XPS Ship: order {order.CustomOrderNumber} could not be built for List Orders", ex);
                result.Excluded.Add(new ExcludedOrder(order.Id, order.CustomOrderNumber, $"error: {ex.Message}"));
            }
        }

        return result;
    }

    /// <summary>One order for Get Order: only lines still to ship, so a fully shipped order has no items</summary>
    public async Task<XpsOrder> BuildOrderAsync(Order order) => await MapAsync(order, await LoadLinesAsync(order));

    /// <summary>The order's ship-enabled lines with how many of each can still go into a shipment</summary>
    public async Task<IList<OrderLine>> LoadLinesAsync(Order order)
    {
        var lines = new List<OrderLine>();
        foreach (var item in await _orderService.GetOrderItemsAsync(order.Id, isShipEnabled: true))
        {
            var product = await _productService.GetProductByIdAsync(item.ProductId);
            if (product == null || !IsPhysical(product))
                continue;

            var sku = await _productService.FormatSkuAsync(product, item.AttributesXml);
            var remaining = await _orderService.GetTotalNumberOfItemsCanBeAddedToShipmentAsync(item);
            lines.Add(new OrderLine(item, product, string.IsNullOrWhiteSpace(sku) ? null : sku, remaining,
                await StockOfAsync(product, item), await UnitWeightLbAsync(item, product)));
        }

        return lines;
    }

    /// <summary>
    /// Virtual gift cards and downloads never go to XPS, even when the product is marked ship-enabled
    /// (nopCommerce's own sample virtual gift card is). Physical gift cards ship like anything else.
    /// </summary>
    private static bool IsPhysical(Product product) =>
        !product.IsDownload && !(product.IsGiftCard && product.GiftCardType == GiftCardType.Virtual);

    private async Task<StockState> StockOfAsync(Product product, OrderItem item)
    {
        switch (product.ManageInventoryMethod)
        {
            case ManageInventoryMethod.ManageStock:
                return OrderRules.Classify(true, await _productService.GetTotalStockQuantityAsync(product));

            case ManageInventoryMethod.ManageStockByAttributes:
                var combination = await _productAttributeParser.FindProductAttributeCombinationAsync(product, item.AttributesXml);
                return combination == null ? StockState.NotTracked : OrderRules.Classify(true, combination.StockQuantity);

            default:
                return StockState.NotTracked;
        }
    }

    /// <summary>
    /// The pick bins for a line: the attribute combination's bin, else the product's, plus the bin of every
    /// "Associated to product" value, whose stock comes off the linked product.
    /// shortcut: reads the live store's custom 4.50 columns Product.BinLocation and ProductAttributeCombination.AttBinLocation,
    /// which stock nopCommerce and the 4.90 install don't have; 4.50 build only.
    /// </summary>
    private async Task<string?> BinLabelAsync(Product product, OrderItem item)
    {
        string? mainBin = null;
        var linked = new List<(string Name, string? Bin)>();

        if (!string.IsNullOrEmpty(item.AttributesXml))
        {
            var combination = await _productAttributeParser.FindProductAttributeCombinationAsync(product, item.AttributesXml);
            if (combination != null)
                mainBin = await BinColumnAsync("SELECT AttBinLocation FROM ProductAttributeCombination WHERE Id = @id", combination.Id);

            foreach (var value in await _productAttributeParser.ParseProductAttributeValuesAsync(item.AttributesXml))
                if (value.AttributeValueType == AttributeValueType.AssociatedToProduct && value.AssociatedProductId > 0)
                    linked.Add((value.Name, await BinColumnAsync("SELECT BinLocation FROM Product WHERE Id = @id", value.AssociatedProductId)));
        }

        if (string.IsNullOrWhiteSpace(mainBin))
            mainBin = await BinColumnAsync("SELECT BinLocation FROM Product WHERE Id = @id", product.Id);

        return Instructions.BinLabel(mainBin, linked);
    }

    private async Task<string?> BinColumnAsync(string sql, int id) =>
        (await _dataProvider.QueryAsync<string?>(sql, new DataParameter("id", id))).FirstOrDefault();

    /// <summary>OrderItem.ItemWeight (per unit, includes attribute weight), else the product's current weight; null when both are 0</summary>
    private async Task<decimal?> UnitWeightLbAsync(OrderItem item, Product product)
    {
        var weight = item.ItemWeight is > 0 ? item.ItemWeight.Value : product.Weight;
        if (weight <= 0)
            return null;

        if (!_poundsResolved)
        {
            var baseWeight = await _measureService.GetMeasureWeightByIdAsync(_measureSettings.BaseWeightId);
            _pounds = string.Equals(baseWeight?.SystemKeyword, "lb", StringComparison.OrdinalIgnoreCase)
                ? null
                : await _measureService.GetMeasureWeightBySystemKeywordAsync("lb");
            _poundsResolved = true;
        }

        return _pounds == null ? weight : await _measureService.ConvertFromPrimaryMeasureWeightAsync(weight, _pounds);
    }

    private static string InstructionsOf(Order order) => Instructions.ToPlainText(order.CheckoutAttributeDescription, "\n");

    private async Task<XpsOrder> MapAsync(Order order, IList<OrderLine> lines)
    {
        var instructions = InstructionsOf(order);

        var xps = new XpsOrder
        {
            OrderId = XpsFormat.OrderId(order.OrderGuid,
                (await _dataService.GetMapsForOrderAsync(order.OrderGuid)).Count(m => m.Status == TrackingMapStatus.Voided)),
            OrderNumber = order.CustomOrderNumber,
            OrderDate = XpsFormat.UnixSeconds(order.CreatedOnUtc),
            CustomerNotes = Instructions.CustomerNotes(order.ShippingMethod, instructions),
            Currency = string.IsNullOrEmpty(order.CustomerCurrencyCode) ? null : order.CustomerCurrencyCode,
            Sender = Sender(),
            Receiver = await ReceiverAsync(order),
            ShippingService = ServiceKeys.Resolve(order.ShippingMethod, ServiceKeys.Parse(_settings.ServiceKeyMap), _settings.DefaultServiceKey),
            ShipperReference = Instructions.ShipperReference(order.CustomOrderNumber, Instructions.FindPo(instructions)),
            ShippingTotal = XpsFormat.Money(order.OrderShippingInclTax)
        };

        foreach (var line in lines.Where(l => l.Remaining > 0))
        {
            var sku = line.Sku;
            if (sku == null)
                await _dataService.AddWarningOnceAsync(order.OrderGuid, order.CustomOrderNumber, $"No SKU: {line.Product.Name}");

            if (line.UnitWeightLb == null)
                await _dataService.AddWarningOnceAsync(order.OrderGuid, order.CustomOrderNumber, $"No weight: {sku ?? line.Product.Name}");

            xps.Items.Add(new XpsItem
            {
                ProductId = line.Item.OrderItemGuid.ToString(),
                Sku = sku,
                Title = Instructions.LineTitle(line.Product.Name, line.Item.AttributeDescription, line.Stock == StockState.Backordered,
                    await BinLabelAsync(line.Product, line.Item)),
                Price = XpsFormat.Money(line.Item.UnitPriceExclTax),
                Quantity = line.Remaining,
                Weight = line.UnitWeightLb.HasValue ? XpsFormat.Weight(line.UnitWeightLb.Value) : null,
                ImgUrl = await ImageUrlAsync(line)
            });
        }

        return xps;
    }

    private XpsAddress Sender() => new()
    {
        Name = _settings.SenderName,
        Company = _settings.SenderCompany,
        Address1 = _settings.SenderAddress1,
        Address2 = _settings.SenderAddress2,
        City = _settings.SenderCity,
        State = _settings.SenderState,
        Zip = _settings.SenderZip,
        Country = _settings.SenderCountry,
        Phone = XpsFormat.Phone(_settings.SenderPhone),
        Email = _settings.SenderEmail
    };

    private async Task<XpsAddress?> ReceiverAsync(Order order)
    {
        if (!order.ShippingAddressId.HasValue)
            return null;

        var address = await _addressService.GetAddressByIdAsync(order.ShippingAddressId.Value);
        if (address == null)
            return null;

        var state = address.StateProvinceId is > 0 ? await _stateProvinceService.GetStateProvinceByIdAsync(address.StateProvinceId.Value) : null;
        var country = address.CountryId is > 0 ? await _countryService.GetCountryByIdAsync(address.CountryId.Value) : null;

        var email = address.Email;
        if (string.IsNullOrWhiteSpace(email))
            email = (await _customerService.GetCustomerByIdAsync(order.CustomerId))?.Email;

        return new XpsAddress
        {
            Company = address.Company,
            Name = $"{address.FirstName} {address.LastName}".Trim(),
            Address1 = address.Address1,
            Address2 = address.Address2,
            City = address.City,
            State = state?.Abbreviation ?? address.County,
            Zip = address.ZipPostalCode,
            Country = country?.TwoLetterIsoCode,
            Phone = XpsFormat.Phone(address.PhoneNumber),
            Email = email
        };
    }

    private async Task<string?> ImageUrlAsync(OrderLine line)
    {
        try
        {
            var picture = await _pictureService.GetProductPictureAsync(line.Product, line.Item.AttributesXml);
            if (picture == null)
                return null;

            var (url, _) = await _pictureService.GetPictureUrlAsync(picture, 100);
            return string.IsNullOrEmpty(url) ? null : url;
        }
        catch
        {
            // The image is a convenience for packers; a missing file must not cost the order its place in XPS
            return null;
        }
    }
}
