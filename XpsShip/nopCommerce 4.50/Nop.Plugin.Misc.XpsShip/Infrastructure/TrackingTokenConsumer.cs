using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Misc.XpsShip.Services;
using Nop.Services.Events;
using Nop.Services.Messages;

namespace Nop.Plugin.Misc.XpsShip.Infrastructure;

/// <summary>
/// Adds %Shipment.XpsTrackingUrl% to shipment emails. nopCommerce's own %Shipment.TrackingNumberURL% takes the
/// link from the checkout shipping method, which has no tracker for Free and points FedEx orders that now ship
/// UPS at FedEx. The plugin changes no template; the token is pasted into ShipmentSent.CustomerNotification.
/// </summary>
public class TrackingTokenConsumer : IConsumer<EntityTokensAddedEvent<Shipment, Token>>
{
    public const string TokenName = "Shipment.XpsTrackingUrl";

    private readonly TrackingLinkResolver _resolver;

    public TrackingTokenConsumer(TrackingLinkResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task HandleEventAsync(EntityTokensAddedEvent<Shipment, Token> eventMessage)
    {
        eventMessage.Tokens.Add(new Token(TokenName, await _resolver.ResolveAsync(eventMessage.Entity), true));
    }
}

/// <summary>Lists %Shipment.XpsTrackingUrl% among the allowed tokens on the admin message template page</summary>
public class TrackingTokenListConsumer : IConsumer<AdditionalTokensAddedEvent>
{
    public Task HandleEventAsync(AdditionalTokensAddedEvent eventMessage)
    {
        eventMessage.AddTokens($"%{TrackingTokenConsumer.TokenName}%");
        return Task.CompletedTask;
    }
}
