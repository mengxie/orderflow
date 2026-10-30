using OrderFlow.Application.Abstractions;
using OrderFlow.Contracts;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application;

/// <summary>
/// The anti-corruption boundary between the internal model and the public contracts. Domain
/// events can be refactored freely; integration events cannot.
/// </summary>
public sealed class DomainEventMapper : IDomainEventMapper
{
    public IIntegrationEvent? Map(IDomainEvent domainEvent) => domainEvent switch
    {
        OrderPlaced placed => new OrderPlacedV1(
            Guid.CreateVersion7(),
            placed.OccurredAt,
            placed.OrderId.Value,
            placed.CustomerId.Value,
            placed.Total.Amount,
            placed.Total.Currency,
            [.. placed.Lines.Select(line => new OrderLineV1(line.Sku.Value, line.Quantity, line.UnitPrice.Amount))],
            placed.AggregateVersion),
        OrderPaid paid => new OrderPaidV1(
            Guid.CreateVersion7(),
            paid.OccurredAt,
            paid.OrderId.Value,
            paid.PaymentId,
            paid.Amount.Amount,
            paid.Amount.Currency,
            paid.AggregateVersion),
        OrderShipped shipped => new OrderShippedV1(
            Guid.CreateVersion7(),
            shipped.OccurredAt,
            shipped.OrderId.Value,
            shipped.TrackingNumber,
            shipped.AggregateVersion),
        OrderCancelled cancelled => new OrderCancelledV1(
            Guid.CreateVersion7(),
            cancelled.OccurredAt,
            cancelled.OrderId.Value,
            cancelled.Reason,
            cancelled.RefundRequired,
            cancelled.AggregateVersion),
        _ => null,
    };
}
