using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Orders;

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineResponse> Lines,
    string? PaymentId,
    string? TrackingNumber,
    string? CancellationReason,
    int Version,
    DateTimeOffset PlacedAt,
    DateTimeOffset UpdatedAt)
{
    public static OrderResponse From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new OrderResponse(
            order.Id.Value,
            order.CustomerId.Value,
            order.Status.ToString(),
            order.Total.Amount,
            order.Currency,
            [.. order.Lines.Select(line => new OrderLineResponse(line.Sku.Value, line.Quantity, line.UnitPrice.Amount, line.LineTotal.Amount))],
            order.PaymentId,
            order.TrackingNumber,
            order.CancellationReason,
            order.Version,
            order.PlacedAt,
            order.UpdatedAt);
    }
}

public sealed record OrderLineResponse(string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);
