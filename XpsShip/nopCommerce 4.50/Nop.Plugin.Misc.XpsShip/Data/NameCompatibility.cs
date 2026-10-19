using Nop.Data.Mapping;
using Nop.Plugin.Misc.XpsShip.Domain;

namespace Nop.Plugin.Misc.XpsShip.Data;

/// <summary>Maps the entities to their XpsShip_-prefixed tables</summary>
public class NameCompatibility : INameCompatibility
{
    public Dictionary<Type, string> TableNames => new()
    {
        { typeof(XpsShipTrackingMap), XpsShipDefaults.TrackingMapTable },
        { typeof(XpsShipOrderFlag), XpsShipDefaults.OrderFlagTable },
        { typeof(XpsShipWebhookLog), XpsShipDefaults.WebhookLogTable }
    };

    public Dictionary<(Type, string), string> ColumnName => new();
}
