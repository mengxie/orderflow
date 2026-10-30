using System.Reflection;

namespace OrderFlow.Contracts;

/// <summary>
/// A message published to, or received from, other services. Contracts are public and versioned:
/// never change a published one, add a V2 instead. This assembly has no dependencies so that
/// other services can reference it safely.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>The wire name of a message, also used as its routing key.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageTypeAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public static class MessageTypes
{
    /// <summary>Every contract in this assembly, by wire name.</summary>
    public static IReadOnlyDictionary<string, Type> All { get; } = typeof(MessageTypes).Assembly
        .GetTypes()
        .Where(type => type.GetCustomAttribute<MessageTypeAttribute>() is not null)
        .ToDictionary(NameOf, StringComparer.Ordinal);

    public static string NameOf<TEvent>()
        where TEvent : IIntegrationEvent => NameOf(typeof(TEvent));

    public static string NameOf(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return eventType.GetCustomAttribute<MessageTypeAttribute>()?.Name
            ?? throw new InvalidOperationException($"{eventType.Name} has no [MessageType] attribute.");
    }
}

[MessageType("orders.order-placed.v1")]
public sealed record OrderPlacedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    Guid CustomerId,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineV1> Lines,
    int OrderVersion) : IIntegrationEvent;

public sealed record OrderLineV1(string Sku, int Quantity, decimal UnitPrice);

[MessageType("orders.order-paid.v1")]
public sealed record OrderPaidV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string PaymentId,
    decimal Amount,
    string Currency,
    int OrderVersion) : IIntegrationEvent;

[MessageType("orders.order-shipped.v1")]
public sealed record OrderShippedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string TrackingNumber,
    int OrderVersion) : IIntegrationEvent;

[MessageType("orders.order-cancelled.v1")]
public sealed record OrderCancelledV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string Reason,
    bool RefundRequired,
    int OrderVersion) : IIntegrationEvent;

/// <summary>Published by the payments service; consumed here to mark orders as paid.</summary>
[MessageType("payments.payment-captured.v1")]
public sealed record PaymentCapturedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string PaymentId,
    decimal Amount,
    string Currency) : IIntegrationEvent;
