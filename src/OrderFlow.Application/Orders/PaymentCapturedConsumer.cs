using Microsoft.Extensions.Logging;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Behaviors;
using OrderFlow.Contracts;

namespace OrderFlow.Application.Orders;

/// <summary>
/// Applies payments captured by the payments service. Runs inside the inbox transaction, so
/// it calls the undecorated <see cref="RecordPaymentHandler"/> rather than opening its own.
/// A payment that cannot be applied is a business outcome, not a retryable fault: it is
/// logged and the message is acknowledged.
/// </summary>
public sealed class PaymentCapturedConsumer(RecordPaymentHandler recordPayment, ILogger<PaymentCapturedConsumer> logger)
    : IIntegrationEventConsumer<PaymentCapturedV1>
{
    public async Task HandleAsync(PaymentCapturedV1 integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var command = new RecordPaymentCommand(
            integrationEvent.OrderId,
            integrationEvent.PaymentId,
            integrationEvent.Amount,
            integrationEvent.Currency);

        var result = await recordPayment.HandleAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            ApplicationLog.PaymentNotApplied(logger, integrationEvent.OrderId, integrationEvent.PaymentId, result.Error.Code, result.Error.Message);
        }
    }
}
