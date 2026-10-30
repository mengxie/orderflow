using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Abstractions;
using OrderFlow.Contracts;
using OrderFlow.Infrastructure.Messaging;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.Infrastructure.Inbox;

public sealed record IncomingMessage(Guid MessageId, string Type, ReadOnlyMemory<byte> Body);

public enum InboxOutcome
{
    Processed,
    Duplicate,
    UnknownType,
    Malformed,
}

/// <summary>
/// Exactly-once processing on top of at-least-once delivery: the inbox row, the consumer's
/// state changes and any events it raises are committed in one transaction. A redelivered
/// message finds its inbox row and is skipped.
/// </summary>
internal sealed class InboxProcessor(IServiceScopeFactory scopeFactory, MessageTypeRegistry registry)
{
    public async Task<InboxOutcome> ProcessAsync(IncomingMessage message, string consumer, CancellationToken cancellationToken)
    {
        if (!registry.TryGet(message.Type, out var registration))
        {
            return InboxOutcome.UnknownType;
        }

        IIntegrationEvent integrationEvent;
        try
        {
            if (JsonSerializer.Deserialize(message.Body.Span, registration.EventType, MessageSerializer.Options) is not IIntegrationEvent parsed)
            {
                return InboxOutcome.Malformed;
            }

            integrationEvent = parsed;
        }
        catch (JsonException)
        {
            return InboxOutcome.Malformed;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<DbSession>();
        await session.BeginAsync(cancellationToken);
        try
        {
            if (!await session.TryRecordInboxMessageAsync(message.MessageId, consumer, cancellationToken))
            {
                await session.RollbackAsync(cancellationToken);
                return InboxOutcome.Duplicate;
            }

            await registration.Invoker.InvokeAsync(scope.ServiceProvider, integrationEvent, cancellationToken);
            await session.CommitAsync(cancellationToken);
            return InboxOutcome.Processed;
        }
        catch
        {
            await session.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

/// <summary>Maps wire names to contract types and to a typed dispatcher for their consumers.</summary>
internal sealed class MessageTypeRegistry
{
    private readonly Dictionary<string, Registration> _registrations = MessageTypes.All.ToDictionary(
        entry => entry.Key,
        entry => new Registration(entry.Value, ConsumerInvoker.For(entry.Value)),
        StringComparer.Ordinal);

    public bool TryGet(string messageType, [NotNullWhen(true)] out Registration? registration) =>
        _registrations.TryGetValue(messageType, out registration);

    internal sealed record Registration(Type EventType, ConsumerInvoker Invoker);
}

/// <summary>Bridges from a runtime <see cref="Type"/> to strongly typed consumers, without per-call reflection.</summary>
internal abstract class ConsumerInvoker
{
    public static ConsumerInvoker For(Type eventType) =>
        (ConsumerInvoker)Activator.CreateInstance(typeof(ConsumerInvoker<>).MakeGenericType(eventType))!;

    public abstract Task InvokeAsync(IServiceProvider services, IIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}

internal sealed class ConsumerInvoker<TEvent> : ConsumerInvoker
    where TEvent : IIntegrationEvent
{
    public override async Task InvokeAsync(IServiceProvider services, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var consumer in services.GetServices<IIntegrationEventConsumer<TEvent>>())
        {
            await consumer.HandleAsync((TEvent)integrationEvent, cancellationToken);
        }
    }
}
