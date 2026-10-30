using OrderFlow.Application.Abstractions;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Tests;

/// <summary>Stores snapshots, not live objects, so every load behaves like a real round trip.</summary>
internal sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<OrderId, OrderSnapshot> _orders = [];
    private readonly Dictionary<string, OrderId> _idempotencyKeys = new(StringComparer.Ordinal);

    public List<Order> Saved { get; } = [];

    public Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken) =>
        Task.FromResult(_orders.TryGetValue(id, out var snapshot) ? Order.Rehydrate(snapshot) : null);

    public Task<OrderId?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult<OrderId?>(_idempotencyKeys.TryGetValue(idempotencyKey, out var id) ? id : null);

    public Task AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!_idempotencyKeys.TryAdd(idempotencyKey, order.Id))
        {
            throw new ConcurrencyConflictException("Duplicate idempotency key.");
        }

        Save(order);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Order order, CancellationToken cancellationToken)
    {
        if (_orders[order.Id].Version != order.PersistedVersion)
        {
            throw new ConcurrencyConflictException("Stale order.");
        }

        Save(order);
        return Task.CompletedTask;
    }

    public Order Seed(Order order)
    {
        Save(order);
        Saved.Clear();
        return order;
    }

    private void Save(Order order)
    {
        _orders[order.Id] = new OrderSnapshot(
            order.Id.Value, order.CustomerId.Value, order.Currency, order.Status,
            [.. order.Lines.Select(line => new NewOrderLine(line.Sku.Value, line.Quantity, line.UnitPrice.Amount))],
            order.PlacedAt, order.UpdatedAt, order.PaymentId, order.TrackingNumber, order.CancellationReason, order.Version);
        order.MarkPersisted();
        Saved.Add(order);
    }
}

internal sealed class EmptyOrderQueries : IOrderQueries
{
    public Task<Orders.OrderResponse?> GetAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult<Orders.OrderResponse?>(null);
}

internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    public List<string> Calls { get; } = [];

    public Task BeginAsync(CancellationToken cancellationToken) => Record("begin");

    public Task CommitAsync(CancellationToken cancellationToken) => Record("commit");

    public Task RollbackAsync(CancellationToken cancellationToken) => Record("rollback");

    private Task Record(string call)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    public static readonly TimeProvider Clock = new FixedTimeProvider(Now);

    public static Order NewOrder() =>
        Order.Place(new CustomerId(Guid.CreateVersion7()), "NZD", [new("KEYBOARD", 2, 89.50m)], Now).Value;

    public static PlaceOrderCommandBuilder PlaceOrder() => new();
}

internal sealed class PlaceOrderCommandBuilder
{
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString();

    public Orders.PlaceOrderCommand Build(params NewOrderLine[] lines) =>
        new(Guid.CreateVersion7(), "NZD", lines.Length > 0 ? lines : [new("KEYBOARD", 2, 89.50m)], IdempotencyKey);
}
