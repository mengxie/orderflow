using OrderFlow.Application.Abstractions;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Orders;

public sealed record PlaceOrderCommand(
    Guid CustomerId,
    string? Currency,
    IReadOnlyList<NewOrderLine> Lines,
    string IdempotencyKey) : ICommand<PlaceOrderResult>;

/// <param name="Created">False when the idempotency key matched an existing order.</param>
public sealed record PlaceOrderResult(OrderResponse Order, bool Created);

/// <summary>
/// Places an order exactly once per idempotency key, so clients can safely retry on timeouts.
/// </summary>
public sealed class PlaceOrderHandler(IOrderRepository orders, TimeProvider timeProvider)
    : ICommandHandler<PlaceOrderCommand, PlaceOrderResult>
{
    public const int MaxIdempotencyKeyLength = 100;

    public async Task<Result<PlaceOrderResult>> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            return ApplicationErrors.InvalidIdempotencyKey;
        }

        if (await orders.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken) is { } existingId)
        {
            var existing = await orders.GetAsync(existingId, cancellationToken)
                ?? throw new InvalidOperationException($"Order {existingId} disappeared while being read.");
            return new PlaceOrderResult(OrderResponse.From(existing), Created: false);
        }

        var placed = Order.Place(new CustomerId(command.CustomerId), command.Currency, command.Lines, timeProvider.GetUtcNow());
        if (placed.IsFailure)
        {
            return placed.Error;
        }

        await orders.AddAsync(placed.Value, command.IdempotencyKey, cancellationToken);
        return new PlaceOrderResult(OrderResponse.From(placed.Value), Created: true);
    }
}
