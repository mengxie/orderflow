using Microsoft.Extensions.Logging;

namespace OrderFlow.Infrastructure;

internal static partial class InfrastructureLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Applied database migration {Version}")]
    public static partial void MigrationApplied(ILogger logger, string version);

    [LoggerMessage(EventId = 2100, Level = LogLevel.Error, Message = "Outbox relay cycle failed")]
    public static partial void RelayCycleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "Publishing {MessageId} ({MessageType}) failed on attempt {Attempt}; retrying in {Delay}")]
    public static partial void PublishAttemptFailed(ILogger logger, Exception exception, Guid messageId, string messageType, int attempt, TimeSpan delay);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Error, Message = "Dead-lettered outbox message {MessageId} ({MessageType}) after {Attempts} attempts")]
    public static partial void MessageDeadLettered(ILogger logger, Exception exception, Guid messageId, string messageType, int attempts);

    [LoggerMessage(EventId = 2200, Level = LogLevel.Error, Message = "RabbitMQ consumer stopped; reconnecting")]
    public static partial void ConsumerStopped(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Warning, Message = "Rejected message {MessageId} ({MessageType}) to the dead-letter queue: {Reason}")]
    public static partial void MessageRejected(ILogger logger, string? messageId, string? messageType, string reason);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Error, Message = "Processing message {MessageId} ({MessageType}) failed; it will be redelivered")]
    public static partial void MessageFailed(ILogger logger, Exception exception, Guid messageId, string messageType);
}
