using OrderFlow.Application.Abstractions;
using OrderFlow.Domain.Common;

namespace OrderFlow.Application.Orders;

public sealed record CancelOrderCommand(Guid OrderId, string Reason) : ICommand<OrderResponse>;

public sealed class CancelOrderHandler(IOrderRepository orders, TimeProvider timeProvider)
    : ICommandHandler<CancelOrderCommand, OrderResponse>
{
    public Task<Result<OrderResponse>> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return orders.ChangeAsync(command.OrderId, order => order.Cancel(command.Reason, timeProvider.GetUtcNow()), cancellationToken);
    }
}

public sealed record ShipOrderCommand(Guid OrderId, string TrackingNumber) : ICommand<OrderResponse>;

public sealed class ShipOrderHandler(IOrderRepository orders, TimeProvider timeProvider)
    : ICommandHandler<ShipOrderCommand, OrderResponse>
{
    public Task<Result<OrderResponse>> HandleAsync(ShipOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return orders.ChangeAsync(command.OrderId, order => order.Ship(command.TrackingNumber, timeProvider.GetUtcNow()), cancellationToken);
    }
}

public sealed record RecordPaymentCommand(Guid OrderId, string PaymentId, decimal Amount, string Currency) : ICommand<OrderResponse>;

public sealed class RecordPaymentHandler(IOrderRepository orders, TimeProvider timeProvider)
    : ICommandHandler<RecordPaymentCommand, OrderResponse>
{
    public async Task<Result<OrderResponse>> HandleAsync(RecordPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var amount = Money.Create(command.Amount, command.Currency);
        if (amount.IsFailure)
        {
            return amount.Error;
        }

        return await orders.ChangeAsync(
            command.OrderId,
            order => order.MarkPaid(command.PaymentId, amount.Value, timeProvider.GetUtcNow()),
            cancellationToken);
    }
}
