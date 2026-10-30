using Dapper;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Orders;

namespace OrderFlow.Infrastructure.Persistence;

/// <summary>CQRS read side: maps rows straight to the response, without loading the aggregate.</summary>
internal sealed class OrderQueries(DbSession session) : IOrderQueries
{
    public async Task<OrderResponse?> GetAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<OrderRow>(
            new CommandDefinition(OrderSql.SelectOrder, new { Id = orderId }, session.Transaction, cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var lines = await connection.QueryAsync<OrderLineRow>(
            new CommandDefinition(OrderSql.SelectLines, new { OrderId = orderId }, session.Transaction, cancellationToken: cancellationToken));

        return new OrderResponse(
            row.Id,
            row.CustomerId,
            row.Status,
            row.Total,
            row.Currency,
            [.. lines.Select(line => new OrderLineResponse(line.Sku, line.Quantity, line.UnitPrice, line.UnitPrice * line.Quantity))],
            row.PaymentId,
            row.TrackingNumber,
            row.CancellationReason,
            row.Version,
            SqlTime.ToOffset(row.PlacedAt),
            SqlTime.ToOffset(row.UpdatedAt));
    }
}
