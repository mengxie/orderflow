using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Abstractions;
using OrderFlow.Contracts;
using OrderFlow.Infrastructure.Inbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderFlow.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string Section = "RabbitMq";

    public string ConnectionString { get; set; } = "amqp://orderflow:orderflow@localhost:5672";

    /// <summary>Topic exchange this service publishes its events to.</summary>
    public string EventsExchange { get; set; } = "orderflow.events";

    /// <summary>Topic exchange owned by the payments service.</summary>
    public string PaymentsExchange { get; set; } = "payments.events";

    public string InboundQueue { get; set; } = "orderflow.payments";

    public string DeadLetterExchange { get; set; } = "orderflow.dead-letter";

    public string DeadLetterQueue { get; set; } = "orderflow.dead-letter";

    public ushort PrefetchCount { get; set; } = 16;

    /// <summary>Deliveries before the quorum queue dead-letters a poison message.</summary>
    public int DeliveryLimit { get; set; } = 5;

    public bool ConsumerEnabled { get; set; } = true;
}

/// <summary>One long-lived connection per process, created lazily and re-created if it closes.</summary>
internal sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } open)
        {
            return open;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } current)
            {
                return current;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            var factory = new ConnectionFactory
            {
                Uri = new Uri(options.Value.ConnectionString),
                ClientProvidedName = "orderflow",
                AutomaticRecoveryEnabled = true,
            };
            var connection = await factory.CreateConnectionAsync(cancellationToken);
            await DeclareTopologyAsync(connection, options.Value, cancellationToken);
            _connection = connection;
            return connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }

    /// <summary>
    /// Idempotent topology declaration. The inbound queue is a quorum queue with a delivery
    /// limit, so a message that keeps failing is moved to the dead-letter queue automatically.
    /// </summary>
    private static async Task DeclareTopologyAsync(IConnection connection, RabbitMqOptions settings, CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(settings.EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(settings.PaymentsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(settings.DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(settings.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(settings.DeadLetterQueue, settings.DeadLetterExchange, routingKey: string.Empty, cancellationToken: cancellationToken);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-delivery-limit"] = settings.DeliveryLimit,
            ["x-dead-letter-exchange"] = settings.DeadLetterExchange,
        };
        await channel.QueueDeclareAsync(settings.InboundQueue, durable: true, exclusive: false, autoDelete: false, queueArguments, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(settings.InboundQueue, settings.PaymentsExchange, MessageTypes.NameOf<PaymentCapturedV1>(), cancellationToken: cancellationToken);
    }
}

/// <summary>Publishes with publisher confirms: a publish completes only once the broker has the message.</summary>
internal sealed class RabbitMqPublisher(RabbitMqConnection connection) : IMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(string exchange, OutgoingMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var headers = new Dictionary<string, object?>();
        TraceContext.Inject(Activity.Current, headers);
        var properties = new BasicProperties
        {
            MessageId = message.MessageId.ToString(),
            Type = message.Type,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            Headers = headers,
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(exchange, message.Type, mandatory: false, properties, message.Body, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _gate.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } open)
        {
            return open;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        var amqp = await connection.GetAsync(cancellationToken);
        _channel = await amqp.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        return _channel;
    }
}

/// <summary>
/// Consumes the inbound queue with manual acknowledgements. Each delivery runs through the
/// idempotent inbox; failures are re-queued and poison messages end up in the dead-letter queue.
/// </summary>
internal sealed class RabbitMqConsumer(
    RabbitMqConnection connection,
    InboxProcessor inbox,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumer> logger) : BackgroundService
{
    public const string ConsumerName = "orderflow.payments";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ConsumerEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var amqp = await connection.GetAsync(stoppingToken);
                await using var channel = await amqp.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: options.Value.PrefetchCount, global: false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);
                await channel.BasicConsumeAsync(options.Value.InboundQueue, autoAck: false, consumer, stoppingToken);

                // Automatic recovery keeps the consumer alive across connection drops.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                InfrastructureLog.ConsumerStopped(logger, exception);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var properties = delivery.BasicProperties;
        using var activity = OrderFlowTelemetry.ActivitySource.StartActivity(
            $"{properties.Type ?? "unknown"} process", ActivityKind.Consumer, TraceContext.Extract(properties.Headers));

        if (!Guid.TryParse(properties.MessageId, out var messageId) || string.IsNullOrEmpty(properties.Type))
        {
            InfrastructureLog.MessageRejected(logger, properties.MessageId, properties.Type, "missing or invalid message id or type");
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            return;
        }

        try
        {
            var outcome = await inbox.ProcessAsync(new IncomingMessage(messageId, properties.Type, delivery.Body), ConsumerName, cancellationToken);
            InfrastructureMetrics.MessagesConsumed.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));
            if (outcome is InboxOutcome.UnknownType or InboxOutcome.Malformed)
            {
                InfrastructureLog.MessageRejected(logger, properties.MessageId, properties.Type, outcome.ToString());
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
                return;
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            InfrastructureLog.MessageFailed(logger, exception, messageId, properties.Type);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }
}
