using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using OrderFlow.Application.Orders;
using OrderFlow.Contracts;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Tests;

public sealed class IntegrationEventTests
{
    private readonly DomainEventMapper _mapper = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Mapper_TranslatesOrderPlacedIntoThePublicContract()
    {
        var order = TestData.NewOrder();

        var message = Assert.IsType<OrderPlacedV1>(_mapper.Map(order.DomainEvents[0]));

        Assert.Equal(order.Id.Value, message.OrderId);
        Assert.Equal(179m, message.Total);
        Assert.Equal("NZD", message.Currency);
        Assert.Equal(1, message.OrderVersion);
        Assert.Equal(new OrderLineV1("KEYBOARD", 2, 89.50m), Assert.Single(message.Lines));
        Assert.NotEqual(Guid.Empty, message.EventId);
    }

    [Fact]
    public void Mapper_CoversEveryOrderEvent()
    {
        var order = TestData.NewOrder();
        order.MarkPaid("pay_1", Money.Create(179m, "NZD").Value, TestData.Now);
        order.Ship("NZ1", TestData.Now);
        var cancelled = TestData.NewOrder();
        cancelled.Cancel("duplicate", TestData.Now);

        var mapped = order.DomainEvents.Concat(cancelled.DomainEvents).Select(_mapper.Map).ToList();

        Assert.All(mapped, message => Assert.NotNull(message));
        Assert.Contains(mapped, message => message is OrderPaidV1 { PaymentId: "pay_1", OrderVersion: 2 });
        Assert.Contains(mapped, message => message is OrderShippedV1 { TrackingNumber: "NZ1", OrderVersion: 3 });
        Assert.Contains(mapped, message => message is OrderCancelledV1 { RefundRequired: false });
    }

    [Fact]
    public void Contracts_HaveUniqueVersionedWireNames()
    {
        var contracts = typeof(IIntegrationEvent).Assembly.GetTypes()
            .Where(type => type.IsClass && typeof(IIntegrationEvent).IsAssignableFrom(type))
            .ToList();

        Assert.Equal(contracts.Count, MessageTypes.All.Count);
        Assert.All(MessageTypes.All.Keys, name => Assert.Matches(new Regex(@"^[a-z]+\.[a-z-]+\.v\d+$"), name));
    }

    [Fact]
    public async Task PaymentCaptured_MarksTheOrderPaid()
    {
        var orders = new InMemoryOrderRepository();
        var order = orders.Seed(TestData.NewOrder());
        var consumer = new PaymentCapturedConsumer(new RecordPaymentHandler(orders, TestData.Clock), NullLogger<PaymentCapturedConsumer>.Instance);

        await consumer.HandleAsync(new PaymentCapturedV1(Guid.CreateVersion7(), TestData.Now, order.Id.Value, "pay_1", 179m, "NZD"), Ct);

        Assert.Equal(OrderStatus.Paid, (await orders.GetAsync(order.Id, Ct))?.Status);
    }

    [Fact]
    public async Task PaymentCaptured_ThatCannotBeApplied_IsLoggedNotThrown()
    {
        var orders = new InMemoryOrderRepository();
        var order = orders.Seed(TestData.NewOrder());
        var consumer = new PaymentCapturedConsumer(new RecordPaymentHandler(orders, TestData.Clock), NullLogger<PaymentCapturedConsumer>.Instance);

        await consumer.HandleAsync(new PaymentCapturedV1(Guid.CreateVersion7(), TestData.Now, order.Id.Value, "pay_1", 1m, "NZD"), Ct);

        Assert.Equal(OrderStatus.Placed, (await orders.GetAsync(order.Id, Ct))?.Status);
    }
}
