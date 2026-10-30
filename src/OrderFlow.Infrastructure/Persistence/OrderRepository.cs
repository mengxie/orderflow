using Dapper;
using Npgsql;
using OrderFlow.Application.Abstractions;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Infrastructure.Persistence;

internal sealed class OrderRepository(DbSession session) : IOrderRepository
{
    public async Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<OrderRow>(
            new CommandDefinition(OrderSql.SelectOrder, new { Id = id.Value }, session.Transaction, cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var lines = await connection.QueryAsync<OrderLineRow>(
            new CommandDefinition(OrderSql.SelectLines, new { OrderId = id.Value }, session.Transaction, cancellationToken: cancellationToken));

        var order = Order.Rehydrate(new OrderSnapshot(
            row.Id,
            row.CustomerId,
            row.Currency,
            Enum.Parse<OrderStatus>(row.Status),
            [.. lines.Select(line => new NewOrderLine(line.Sku, line.Quantity, line.UnitPrice))],
            SqlTime.ToOffset(row.PlacedAt),
            SqlTime.ToOffset(row.UpdatedAt),
            row.PaymentId,
            row.TrackingNumber,
            row.CancellationReason,
            row.Version));
        session.Track(order);
        return order;
    }

    public async Task<OrderId?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        var id = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            OrderSql.SelectIdByIdempotencyKey,
            new { IdempotencyKey = idempotencyKey },
            session.Transaction,
            cancellationToken: cancellationToken));
        return id is { } value ? new OrderId(value) : null;
    }

    public async Task AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        var transaction = session.RequireTransaction();
        var connection = await session.GetConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                OrderSql.InsertOrder,
                new
                {
                    Id = order.Id.Value,
                    CustomerId = order.CustomerId.Value,
                    Status = order.Status.ToString(),
                    order.Currency,
                    Total = order.Total.Amount,
                    order.PaymentId,
                    order.TrackingNumber,
                    order.CancellationReason,
                    IdempotencyKey = idempotencyKey,
                    order.Version,
                    PlacedAt = order.PlacedAt.UtcDateTime,
                    UpdatedAt = order.UpdatedAt.UtcDateTime,
                },
                transaction,
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == "uq_orders_idempotency_key")
        {
            throw new ConcurrencyConflictException(
                "Another request with the same Idempotency-Key is being processed; retry shortly.", exception);
        }

        var lines = order.Lines
            .Select((line, index) => new
            {
                OrderId = order.Id.Value,
                LineNo = index + 1,
                Sku = line.Sku.Value,
                line.Quantity,
                UnitPrice = line.UnitPrice.Amount,
            })
            .ToArray();
        await connection.ExecuteAsync(new CommandDefinition(OrderSql.InsertLine, lines, transaction, cancellationToken: cancellationToken));

        order.MarkPersisted();
        session.Track(order);
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        var transaction = session.RequireTransaction();
        var connection = await session.GetConnectionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            OrderSql.UpdateOrder,
            new
            {
                Id = order.Id.Value,
                Status = order.Status.ToString(),
                order.PaymentId,
                order.TrackingNumber,
                order.CancellationReason,
                order.Version,
                UpdatedAt = order.UpdatedAt.UtcDateTime,
                ExpectedVersion = order.PersistedVersion,
            },
            transaction,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            throw new ConcurrencyConflictException($"Order {order.Id} was changed by another request; reload it and retry.");
        }

        order.MarkPersisted();
        session.Track(order);
    }
}
