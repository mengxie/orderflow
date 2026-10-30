using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Orders;

public sealed record OrderPlaced(
    OrderId OrderId,
    CustomerId CustomerId,
    Money Total,
    IReadOnlyList<OrderLine> Lines,
    int AggregateVersion,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderPaid(
    OrderId OrderId,
    string PaymentId,
    Money Amount,
    int AggregateVersion,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderShipped(
    OrderId OrderId,
    string TrackingNumber,
    int AggregateVersion,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderCancelled(
    OrderId OrderId,
    string Reason,
    bool RefundRequired,
    int AggregateVersion,
    DateTimeOffset OccurredAt) : IDomainEvent;
