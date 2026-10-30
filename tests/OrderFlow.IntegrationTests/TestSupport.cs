using System.Net.Http.Json;
using System.Threading.Channels;
using OrderFlow.Application.Orders;
using OrderFlow.Contracts;
using OrderFlow.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderFlow.IntegrationTests;

internal static class Api
{
    public static async Task<HttpResponseMessage> PlaceOrderAsync(HttpClient client, string? idempotencyKey, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(body ?? new
            {
                customerId = Guid.CreateVersion7(),
                currency = "NZD",
                lines = new[] { new { sku = "KEYBOARD", quantity = 2, unitPrice = 89.50m }, new { sku = "MOUSE", quantity = 1, unitPrice = 25m } },
            }),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<OrderResponse> PlaceOrderAsync(HttpClient client)
    {
        using var response = await PlaceOrderAsync(client, Guid.NewGuid().ToString());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Eventual consistency, asserted honestly: poll until the condition holds or time runs out.</summary>
    public static async Task<OrderResponse> WaitForOrderAsync(HttpClient client, Guid orderId, Func<OrderResponse, bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            var order = await client.GetFromJsonAsync<OrderResponse>($"/orders/{orderId}", timeout.Token);
            if (order is not null && condition(order))
            {
                return order;
            }

            await Task.Delay(100, timeout.Token);
        }
    }
}

/// <summary>Subscribes a temporary queue to an exchange and exposes what arrives.</summary>
internal sealed class RabbitMqProbe : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly Channel<byte[]> _received = Channel.CreateUnbounded<byte[]>();

    private RabbitMqProbe(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<RabbitMqProbe> SubscribeAsync(string connectionString, string exchange, string routingKey)
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await new ConnectionFactory { Uri = new Uri(connectionString) }.CreateConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        var queue = await channel.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: ct);
        await channel.QueueBindAsync(queue.QueueName, exchange, routingKey, cancellationToken: ct);

        var probe = new RabbitMqProbe(connection, channel);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            // The body buffer is only valid during the callback, so copy it.
            probe._received.Writer.TryWrite(delivery.Body.ToArray());
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, ct);
        return probe;
    }

    public static async Task PublishAsync(string connectionString, string exchange, IIntegrationEvent integrationEvent)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await new ConnectionFactory { Uri = new Uri(connectionString) }.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        var message = MessageSerializer.ToMessage(integrationEvent);
        var properties = new BasicProperties { MessageId = message.MessageId.ToString(), Type = message.Type, ContentType = "application/json" };
        await channel.BasicPublishAsync(exchange, message.Type, mandatory: false, properties, message.Body, ct);
    }

    public async Task<TEvent> WaitForAsync<TEvent>(Func<TEvent, bool> match)
        where TEvent : IIntegrationEvent
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await foreach (var body in _received.Reader.ReadAllAsync(timeout.Token))
        {
            var candidate = System.Text.Json.JsonSerializer.Deserialize<TEvent>(body, MessageSerializer.Options);
            if (candidate is not null && match(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The probe stopped receiving.");
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
