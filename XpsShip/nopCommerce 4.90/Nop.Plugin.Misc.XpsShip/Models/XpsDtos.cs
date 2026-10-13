using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nop.Plugin.Misc.XpsShip.Models;

// The JSON XPS reads from List Orders and Get Order. Names follow XPS's docs exactly, including shipping_total.

public class XpsOrderListResponse
{
    [JsonPropertyName("orders")]
    public IList<XpsOrder> Orders { get; set; } = new List<XpsOrder>();
}

public class XpsOrderResponse
{
    [JsonPropertyName("order")]
    public XpsOrder? Order { get; set; }
}

public class XpsOrder
{
    /// <summary>Order.OrderGuid. XPS echoes it back on Update Order and Get Order.</summary>
    [JsonPropertyName("orderId")] public string OrderId { get; set; } = string.Empty;
    [JsonPropertyName("orderNumber")] public string? OrderNumber { get; set; }
    [JsonPropertyName("orderDate")] public long OrderDate { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("customerNotes")] public string? CustomerNotes { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("sender")] public XpsAddress? Sender { get; set; }
    [JsonPropertyName("receiver")] public XpsAddress? Receiver { get; set; }
    [JsonPropertyName("shippingService")] public string? ShippingService { get; set; }
    [JsonPropertyName("shipperReference")] public string? ShipperReference { get; set; }
    [JsonPropertyName("shipping_total")] public string? ShippingTotal { get; set; }
    [JsonPropertyName("weightUnit")] public string WeightUnit { get; set; } = "lb";
    [JsonPropertyName("items")] public IList<XpsItem> Items { get; set; } = new List<XpsItem>();
}

public class XpsAddress
{
    [JsonPropertyName("company")] public string? Company { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("address1")] public string? Address1 { get; set; }
    [JsonPropertyName("address2")] public string? Address2 { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("zip")] public string? Zip { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
}

public class XpsItem
{
    /// <summary>OrderItem.OrderItemGuid, not the catalog product: XPS posts it back as items[n][id]</summary>
    [JsonPropertyName("productId")] public string ProductId { get; set; } = string.Empty;
    [JsonPropertyName("sku")] public string? Sku { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("price")] public string? Price { get; set; }
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
    [JsonPropertyName("weight")] public string? Weight { get; set; }
    [JsonPropertyName("imgUrl")] public string? ImgUrl { get; set; }
}

public static class XpsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // Nulls are sent: XPS's docs show null for absent values
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };

    public static string Serialize<T>(T value, bool indented = false) => JsonSerializer.Serialize(value, indented ? Indented : Options);
}
