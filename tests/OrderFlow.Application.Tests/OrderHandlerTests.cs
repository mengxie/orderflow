using OrderFlow.Application.Orders;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Tests;

public sealed class OrderHandlerTests
{
    private readonly InMemoryOrderRepository _orders = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PlaceOrder_CreatesTheOrder()
    {
        var result = await new PlaceOrderHandler(_orders, TestData.Clock).HandleAsync(TestData.PlaceOrder().Build(), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        Assert.Equal("Placed", result.Value.Order.Status);
        Assert.Equal(179m, result.Value.Order.Total);
        Assert.Single(_orders.Saved);
    }

    [Fact]
    public async Task PlaceOrder_RetriedWithTheSameKey_ReturnsTheOriginalOrder()
    {
        var handler = new PlaceOrderHandler(_orders, TestData.Clock);
        var command = TestData.PlaceOrder().Build();

        var first = await handler.HandleAsync(command, Ct);
        var retry = await handler.HandleAsync(command, Ct);

        Assert.False(retry.Value.Created);
        Assert.Equal(first.Value.Order.Id, retry.Value.Order.Id);
        Assert.Single(_orders.Saved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task PlaceOrder_WithoutAUsableIdempotencyKey_IsRejected(string key)
    {
        var builder = TestData.PlaceOrder();
        builder.IdempotencyKey = key;

        var result = await new PlaceOrderHandler(_orders, TestData.Clock).HandleAsync(builder.Build(), Ct);

        Assert.Equal(ApplicationErrors.InvalidIdempotencyKey, result.Error);
    }

    [Fact]
    public async Task PlaceOrder_WithAnOverlongIdempotencyKey_IsRejected()
    {
        var builder = TestData.PlaceOrder();
        builder.IdempotencyKey = new string('k', PlaceOrderHandler.MaxIdempotencyKeyLength + 1);

        var result = await new PlaceOrderHandler(_orders, TestData.Clock).HandleAsync(builder.Build(), Ct);

        Assert.Equal(ApplicationErrors.InvalidIdempotencyKey, result.Error);
    }

    [Fact]
    public async Task PlaceOrder_InvalidByDomainRules_SavesNothing()
    {
        var result = await new PlaceOrderHandler(_orders, TestData.Clock)
            .HandleAsync(TestData.PlaceOrder().Build(new NewOrderLine("bad sku", 1, 1m)), Ct);

        Assert.Equal("sku.invalid", result.Error?.Code);
        Assert.Empty(_orders.Saved);
    }

    [Fact]
    public async Task CancelOrder_UnknownOrder_IsNotFound()
    {
        var result = await new CancelOrderHandler(_orders, TestData.Clock)
            .HandleAsync(new CancelOrderCommand(Guid.CreateVersion7(), "duplicate"), Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
    }

    [Fact]
    public async Task CancelOrder_AlreadyCancelled_DoesNotWriteAgain()
    {
        var order = _orders.Seed(TestData.NewOrder());
        var handler = new CancelOrderHandler(_orders, TestData.Clock);

        await handler.HandleAsync(new CancelOrderCommand(order.Id.Value, "duplicate"), Ct);
        var again = await handler.HandleAsync(new CancelOrderCommand(order.Id.Value, "duplicate"), Ct);

        Assert.True(again.IsSuccess);
        Assert.Single(_orders.Saved);
    }

    [Fact]
    public async Task RecordPayment_ThenShip_MovesThroughTheLifecycle()
    {
        var order = _orders.Seed(TestData.NewOrder());

        var paid = await new RecordPaymentHandler(_orders, TestData.Clock)
            .HandleAsync(new RecordPaymentCommand(order.Id.Value, "pay_1", 179m, "NZD"), Ct);
        var shipped = await new ShipOrderHandler(_orders, TestData.Clock)
            .HandleAsync(new ShipOrderCommand(order.Id.Value, "NZ000123"), Ct);

        Assert.Equal("Paid", paid.Value.Status);
        Assert.Equal("Shipped", shipped.Value.Status);
        Assert.Equal(3, shipped.Value.Version);
    }

    [Fact]
    public async Task RecordPayment_WithAnInvalidCurrency_IsRejectedBeforeLoading()
    {
        var result = await new RecordPaymentHandler(_orders, TestData.Clock)
            .HandleAsync(new RecordPaymentCommand(Guid.CreateVersion7(), "pay_1", 1m, "dollars"), Ct);

        Assert.Equal("money.invalid_currency", result.Error?.Code);
    }

    [Fact]
    public async Task GetOrder_UnknownOrder_IsNotFound()
    {
        var result = await new GetOrderHandler(new EmptyOrderQueries()).HandleAsync(new GetOrderQuery(Guid.CreateVersion7()), Ct);

        Assert.Equal("order.not_found", result.Error?.Code);
    }
}
