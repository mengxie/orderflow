using Dapper;
using Npgsql;
using OrderFlow.Contracts;

namespace OrderFlow.IntegrationTests;

/// <summary>End-to-end proofs of the outbox and inbox guarantees against a real broker and database.</summary>
public sealed class MessagingTests(OrderFlowApiFactory factory)
{
    private const string EventsExchange = "orderflow.events";
    private const string PaymentsExchange = "payments.events";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PlacingAnOrder_PublishesOrderPlacedV1_ViaTheOutbox()
    {
        await using var probe = await RabbitMqProbe.SubscribeAsync(factory.RabbitMqConnectionString, EventsExchange, MessageTypes.NameOf<OrderPlacedV1>());

        var order = await Api.PlaceOrderAsync(_client);

        var published = await probe.WaitForAsync<OrderPlacedV1>(message => message.OrderId == order.Id);
        Assert.Equal(order.Total, published.Total);
        Assert.Equal(2, published.Lines.Count);
        Assert.Equal(1, published.OrderVersion);
    }

    [Fact]
    public async Task PaymentCaptured_DeliveredTwice_IsAppliedExactlyOnce()
    {
        await using var probe = await RabbitMqProbe.SubscribeAsync(factory.RabbitMqConnectionString, EventsExchange, MessageTypes.NameOf<OrderPaidV1>());
        var order = await Api.PlaceOrderAsync(_client);
        var payment = new PaymentCapturedV1(Guid.CreateVersion7(), DateTimeOffset.UtcNow, order.Id, "pay_twice", order.Total, order.Currency);

        // At-least-once delivery: the same message (same id) arrives twice.
        await RabbitMqProbe.PublishAsync(factory.RabbitMqConnectionString, PaymentsExchange, payment);
        await RabbitMqProbe.PublishAsync(factory.RabbitMqConnectionString, PaymentsExchange, payment);

        var paid = await Api.WaitForOrderAsync(_client, order.Id, current => current.Status == "Paid");
        await probe.WaitForAsync<OrderPaidV1>(message => message.OrderId == order.Id);
        Assert.Equal(2, paid.Version);

        await using var dataSource = NpgsqlDataSource.Create(factory.PostgresConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var inboxRows = await connection.QuerySingleAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM inbox_messages WHERE message_id = @Id", new { Id = payment.EventId }));
        var paidEvents = await connection.QuerySingleAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM outbox_messages WHERE type = 'orders.order-paid.v1' AND payload->>'orderId' = @OrderId",
            new { OrderId = order.Id.ToString() }));
        Assert.Equal(1, inboxRows);
        Assert.Equal(1, paidEvents);
    }

    [Fact]
    public async Task PaymentForTheWrongAmount_IsAcknowledged_AndTheOrderStaysPlaced()
    {
        var order = await Api.PlaceOrderAsync(_client);
        var wrong = new PaymentCapturedV1(Guid.CreateVersion7(), DateTimeOffset.UtcNow, order.Id, "pay_short", order.Total - 1, order.Currency);
        var right = wrong with { EventId = Guid.CreateVersion7(), PaymentId = "pay_full", Amount = order.Total };

        await RabbitMqProbe.PublishAsync(factory.RabbitMqConnectionString, PaymentsExchange, wrong);
        await RabbitMqProbe.PublishAsync(factory.RabbitMqConnectionString, PaymentsExchange, right);

        // Messages are consumed in order, so once the correct payment is applied the wrong one was handled (and rejected) first.
        var paid = await Api.WaitForOrderAsync(_client, order.Id, current => current.Status == "Paid");
        Assert.Equal("pay_full", paid.PaymentId);
    }
}
