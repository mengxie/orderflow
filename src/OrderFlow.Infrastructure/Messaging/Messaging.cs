using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using OrderFlow.Application.Abstractions;
using OrderFlow.Contracts;

namespace OrderFlow.Infrastructure.Messaging;

public sealed record OutgoingMessage(Guid MessageId, string Type, ReadOnlyMemory<byte> Body, DateTimeOffset OccurredAt);

/// <summary>Broker-agnostic publishing port; RabbitMQ is one adapter.</summary>
public interface IMessagePublisher
{
    Task PublishAsync(string exchange, OutgoingMessage message, CancellationToken cancellationToken);
}

public static class MessageSerializer
{
    public static JsonSerializerOptions Options => JsonSerializerOptions.Web;

    public static string Serialize(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), Options);
    }

    public static OutgoingMessage ToMessage(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return new OutgoingMessage(
            integrationEvent.EventId,
            MessageTypes.NameOf(integrationEvent.GetType()),
            Encoding.UTF8.GetBytes(Serialize(integrationEvent)),
            integrationEvent.OccurredAt);
    }
}

/// <summary>
/// W3C trace-context propagation through message headers, using only the BCL. A request that
/// places an order, the relay that publishes the event and the consumer that handles it all
/// appear as one distributed trace.
/// </summary>
internal static class TraceContext
{
    public static Activity? StartActivity(string name, ActivityKind kind, string? traceParent)
    {
        ActivityContext.TryParse(traceParent, null, out var parent);
        return OrderFlowTelemetry.ActivitySource.StartActivity(name, kind, parent);
    }

    public static void Inject(Activity? activity, IDictionary<string, object?> headers) =>
        DistributedContextPropagator.Current.Inject(activity, headers, static (carrier, key, value) =>
        {
            if (carrier is IDictionary<string, object?> target)
            {
                target[key] = value;
            }
        });

    public static ActivityContext Extract(IDictionary<string, object?>? headers)
    {
        DistributedContextPropagator.Current.ExtractTraceIdAndState(
            headers,
            static (object? carrier, string fieldName, out string? fieldValue, out IEnumerable<string>? fieldValues) =>
            {
                fieldValues = null;
                fieldValue = carrier is IDictionary<string, object?> source && source.TryGetValue(fieldName, out var raw)
                    ? raw switch
                    {
                        byte[] bytes => Encoding.UTF8.GetString(bytes),
                        string text => text,
                        _ => null,
                    }
                    : null;
            },
            out var traceParent,
            out var traceState);
        return ActivityContext.TryParse(traceParent, traceState, out var context) ? context : default;
    }
}

internal static class InfrastructureMetrics
{
    public static readonly Counter<long> OutboxPublished =
        OrderFlowTelemetry.Meter.CreateCounter<long>("orderflow.outbox.published", description: "Outbox messages published.");

    public static readonly Counter<long> OutboxFailed =
        OrderFlowTelemetry.Meter.CreateCounter<long>("orderflow.outbox.failed", description: "Failed outbox publish attempts.");

    public static readonly Counter<long> OutboxDeadLettered =
        OrderFlowTelemetry.Meter.CreateCounter<long>("orderflow.outbox.dead_lettered", description: "Outbox messages that exhausted their retries.");

    public static readonly Counter<long> MessagesConsumed =
        OrderFlowTelemetry.Meter.CreateCounter<long>("orderflow.messages.consumed", description: "Inbound messages by outcome.");
}
