namespace OrderFlow.Api.Endpoints;

internal sealed record PlaceOrderRequest(Guid CustomerId, string? Currency, IReadOnlyList<PlaceOrderLineRequest>? Lines);

internal sealed record PlaceOrderLineRequest(string? Sku, int Quantity, decimal UnitPrice);

internal sealed record CancelOrderRequest(string? Reason);

internal sealed record ShipOrderRequest(string? TrackingNumber);

internal sealed record SimulatePaymentRequest(Guid OrderId, decimal Amount, string Currency, string? PaymentId);
