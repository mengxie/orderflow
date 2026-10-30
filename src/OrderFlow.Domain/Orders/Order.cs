using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Orders;

public enum OrderStatus
{
    Placed,
    Paid,
    Shipped,
    Cancelled,
}

/// <summary>
/// The order aggregate. All invariants live here; callers can only change an order through
/// its behaviour methods, each of which validates, mutates and raises one event, or returns
/// an <see cref="Error"/> and changes nothing.
/// </summary>
/// <remarks>Lifecycle: Placed → Paid → Shipped, with Cancelled reachable from Placed or Paid.</remarks>
public sealed class Order : AggregateRoot<OrderId>
{
    public const int MaxLines = 50;

    private readonly List<OrderLine> _lines;

    private Order(OrderId id, CustomerId customerId, string currency, List<OrderLine> lines, DateTimeOffset placedAt, int version)
        : base(id, version)
    {
        CustomerId = customerId;
        Currency = currency;
        _lines = lines;
        PlacedAt = placedAt;
        UpdatedAt = placedAt;
    }

    public CustomerId CustomerId { get; }

    public string Currency { get; }

    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    public Money Total => _lines.Aggregate(Money.Zero(Currency), (total, line) => total.Add(line.LineTotal));

    public OrderStatus Status { get; private set; }

    public DateTimeOffset PlacedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public string? PaymentId { get; private set; }

    public string? TrackingNumber { get; private set; }

    public string? CancellationReason { get; private set; }

    public static Result<Order> Place(CustomerId customerId, string? currency, IReadOnlyCollection<NewOrderLine> lines, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (customerId.Value == Guid.Empty)
        {
            return OrderErrors.CustomerRequired;
        }

        var zero = Money.Create(0, currency);
        if (zero.IsFailure)
        {
            return zero.Error;
        }

        if (lines.Count == 0)
        {
            return OrderErrors.NoLines;
        }

        if (lines.Count > MaxLines)
        {
            return OrderErrors.TooManyLines;
        }

        var orderLines = new List<OrderLine>(lines.Count);
        foreach (var requested in lines)
        {
            var line = OrderLine.Create(requested, zero.Value.Currency);
            if (line.IsFailure)
            {
                return line.Error;
            }

            if (orderLines.Exists(existing => existing.Sku == line.Value.Sku))
            {
                return OrderErrors.DuplicateSku(line.Value.Sku);
            }

            orderLines.Add(line.Value);
        }

        var order = new Order(OrderId.New(), customerId, zero.Value.Currency, orderLines, now, version: 0);
        order.Raise(new OrderPlaced(order.Id, customerId, order.Total, order.Lines, order.NextVersion, now));
        return order;
    }

    /// <summary>
    /// Records a captured payment. Idempotent for the same payment id, because payment
    /// notifications are delivered at least once.
    /// </summary>
    public Result MarkPaid(string paymentId, Money amount, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return OrderErrors.PaymentIdRequired;
        }

        if (Status == OrderStatus.Paid && PaymentId == paymentId)
        {
            return Result.Success();
        }

        if (Status != OrderStatus.Placed)
        {
            return OrderErrors.InvalidTransition(Status, "pay");
        }

        if (amount != Total)
        {
            return OrderErrors.PaymentMismatch(Total, amount);
        }

        Status = OrderStatus.Paid;
        PaymentId = paymentId;
        UpdatedAt = now;
        Raise(new OrderPaid(Id, paymentId, amount, NextVersion, now));
        return Result.Success();
    }

    public Result Ship(string trackingNumber, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            return OrderErrors.TrackingNumberRequired;
        }

        if (Status != OrderStatus.Paid)
        {
            return OrderErrors.InvalidTransition(Status, "ship");
        }

        Status = OrderStatus.Shipped;
        TrackingNumber = trackingNumber;
        UpdatedAt = now;
        Raise(new OrderShipped(Id, trackingNumber, NextVersion, now));
        return Result.Success();
    }

    /// <summary>Cancels the order. A paid order needs a refund, which the event announces.</summary>
    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OrderErrors.ReasonRequired;
        }

        if (Status == OrderStatus.Cancelled)
        {
            return Result.Success();
        }

        if (Status == OrderStatus.Shipped)
        {
            return OrderErrors.InvalidTransition(Status, "cancel");
        }

        var refundRequired = Status == OrderStatus.Paid;
        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        UpdatedAt = now;
        Raise(new OrderCancelled(Id, reason, refundRequired, NextVersion, now));
        return Result.Success();
    }

    /// <summary>Rebuilds an order from storage. Throws if stored data violates an invariant.</summary>
    public static Order Rehydrate(OrderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var lines = snapshot.Lines.Select(line => OrderLine.Create(line, snapshot.Currency).Value).ToList();
        return new Order(new OrderId(snapshot.Id), new CustomerId(snapshot.CustomerId), snapshot.Currency, lines, snapshot.PlacedAt, snapshot.Version)
        {
            Status = snapshot.Status,
            UpdatedAt = snapshot.UpdatedAt,
            PaymentId = snapshot.PaymentId,
            TrackingNumber = snapshot.TrackingNumber,
            CancellationReason = snapshot.CancellationReason,
        };
    }
}

/// <summary>The persisted shape of an order.</summary>
public sealed record OrderSnapshot(
    Guid Id,
    Guid CustomerId,
    string Currency,
    OrderStatus Status,
    IReadOnlyList<NewOrderLine> Lines,
    DateTimeOffset PlacedAt,
    DateTimeOffset UpdatedAt,
    string? PaymentId,
    string? TrackingNumber,
    string? CancellationReason,
    int Version);
