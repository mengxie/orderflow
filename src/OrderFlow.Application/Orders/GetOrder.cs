using OrderFlow.Application.Abstractions;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Orders;

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderResponse>;

public sealed class GetOrderHandler(IOrderQueries queries) : IQueryHandler<GetOrderQuery, OrderResponse>
{
    public async Task<Result<OrderResponse>> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var order = await queries.GetAsync(query.OrderId, cancellationToken);
        if (order is null)
        {
            return OrderErrors.NotFound(new OrderId(query.OrderId));
        }

        return order;
    }
}
