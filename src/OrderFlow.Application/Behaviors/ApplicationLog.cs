using Microsoft.Extensions.Logging;

namespace OrderFlow.Application.Behaviors;

/// <summary>Source-generated, allocation-free log messages.</summary>
internal static partial class ApplicationLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "{Command} succeeded in {ElapsedMs} ms")]
    public static partial void CommandSucceeded(ILogger logger, string command, double elapsedMs);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "{Command} rejected with {ErrorCode}: {ErrorMessage} ({ElapsedMs} ms)")]
    public static partial void CommandRejected(ILogger logger, string command, string errorCode, string errorMessage, double elapsedMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "{Command} failed")]
    public static partial void CommandFailed(ILogger logger, Exception exception, string command);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Warning, Message = "Payment {PaymentId} for order {OrderId} was not applied: {ErrorCode} {ErrorMessage}")]
    public static partial void PaymentNotApplied(ILogger logger, Guid orderId, string paymentId, string errorCode, string errorMessage);
}
