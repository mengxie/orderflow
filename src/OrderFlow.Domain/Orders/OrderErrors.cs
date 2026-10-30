using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Orders;

public static class OrderErrors
{
    public static readonly Error CustomerRequired =
        Error.Validation("order.customer_required", "An order needs a customer.");

    public static readonly Error NoLines =
        Error.Validation("order.no_lines", "An order needs at least one line.");

    public static readonly Error TooManyLines =
        Error.Validation("order.too_many_lines", $"An order can have at most {Order.MaxLines} lines.");

    public static readonly Error PaymentIdRequired =
        Error.Validation("order.payment_id_required", "A payment id is required.");

    public static readonly Error TrackingNumberRequired =
        Error.Validation("order.tracking_number_required", "A tracking number is required.");

    public static readonly Error ReasonRequired =
        Error.Validation("order.reason_required", "A cancellation reason is required.");

    public static Error DuplicateSku(Sku sku) =>
        Error.Validation("order.duplicate_sku", $"SKU {sku} appears more than once; combine the quantities into one line.");

    public static Error PaymentMismatch(Money expected, Money received) =>
        Error.Validation("order.payment_mismatch", $"A payment of {received} does not match the order total of {expected}.");

    public static Error InvalidTransition(OrderStatus status, string action) =>
        Error.Conflict("order.invalid_transition", $"Cannot {action} an order that is {status}.");

    public static Error NotFound(OrderId id) =>
        Error.NotFound("order.not_found", $"Order {id} does not exist.");
}
