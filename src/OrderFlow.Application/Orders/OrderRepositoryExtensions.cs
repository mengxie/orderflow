using OrderFlow.Application.Abstractions;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Orders;

internal static class OrderRepositoryExtensions
{
    /// <summary>Load, apply one behaviour, save if anything changed. The shape of every state change.</summary>
    public static async Task<Result<OrderResponse>> ChangeAsync(
        this IOrderRepository orders,
        Guid orderId,
        Func<Order, Result> change,
        CancellationToken cancellationToken)
    {
        var id = new OrderId(orderId);
        var order = await orders.GetAsync(id, cancellationToken);
        if (order is null)
        {
            return OrderErrors.NotFound(id);
        }

        var result = change(order);
        if (result.IsFailure)
        {
            return result.Error;
        }

        if (order.HasChanges)
        {
            await orders.UpdateAsync(order, cancellationToken);
        }

        return OrderResponse.From(order);
    }
}
