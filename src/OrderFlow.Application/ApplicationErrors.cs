using OrderFlow.Application.Orders;
using OrderFlow.Domain.Common;

namespace OrderFlow.Application;

public static class ApplicationErrors
{
    public static readonly Error InvalidIdempotencyKey = Error.Validation(
        "request.invalid_idempotency_key",
        $"Send an Idempotency-Key header of 1 to {PlaceOrderHandler.MaxIdempotencyKeyLength} characters.");

    public static Error ConcurrencyConflict(string detail) => Error.Conflict("request.concurrency_conflict", detail);
}
