using Nop.Core;

namespace Nop.Plugin.Misc.XpsShip.Domain;

/// <summary>A staff decision about one order: held back from XPS, or an exchange order released to ship</summary>
public class XpsShipOrderFlag : BaseEntity
{
    public Guid OrderGuid { get; set; }
    public string CustomOrderNumber { get; set; } = string.Empty;

    /// <summary>One of <see cref="OrderFlags"/></summary>
    public string Flag { get; set; } = string.Empty;

    public string? Reason { get; set; }

    /// <summary>Admin email, or "auto" for the do-not-ship rule</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOnUtc { get; set; }
}

public static class OrderFlags
{
    public const string Hold = "Hold";
    public const string ReleaseExchange = "ReleaseExchange";

    /// <summary>
    /// Written when staff take an order off hold, so the do-not-ship rule doesn't put it straight back.
    /// The rule only holds an order the first time it is seen.
    /// </summary>
    public const string HoldCleared = "HoldCleared";

    public const string AutoCreatedBy = "auto";
}
