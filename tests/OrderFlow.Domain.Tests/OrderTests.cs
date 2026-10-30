using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Domain.Tests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly CustomerId Customer = new(Guid.Parse("7f1c5e4a-2b8d-4c71-9a63-1d2e3f4a5b6c"));

    [Fact]
    public void Place_ValidOrder_CalculatesTotalAndRaisesOrderPlaced()
    {
        var result = Order.Place(Customer, "NZD", [new("KEYBOARD", 2, 89.50m), new("MOUSE", 1, 25m)], Now);

        Assert.True(result.IsSuccess);
        var order = result.Value;
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(204.00m, order.Total.Amount);
        Assert.Equal("NZD", order.Total.Currency);
        Assert.Equal(1, order.Version);
        Assert.True(order.HasChanges);
        var placed = Assert.IsType<OrderPlaced>(Assert.Single(order.DomainEvents));
        Assert.Equal(order.Id, placed.OrderId);
        Assert.Equal(1, placed.AggregateVersion);
        Assert.Equal(2, placed.Lines.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nzd")]
    [InlineData("NZ")]
    [InlineData("NZDX")]
    public void Place_InvalidCurrency_IsRejected(string? currency)
    {
        var result = Order.Place(Customer, currency, [new("KEYBOARD", 1, 10m)], Now);

        Assert.Equal("money.invalid_currency", result.Error?.Code);
    }

    [Theory]
    [InlineData("keyboard", 1, 10, "sku.invalid")]
    [InlineData("K", 1, 10, "sku.invalid")]
    [InlineData("KEYBOARD", 0, 10, "order_line.invalid_quantity")]
    [InlineData("KEYBOARD", 1001, 10, "order_line.invalid_quantity")]
    [InlineData("KEYBOARD", 1, -1, "money.negative_amount")]
    [InlineData("KEYBOARD", 1, 9.999, "money.too_precise")]
    public void Place_InvalidLine_IsRejected(string sku, int quantity, double unitPrice, string expectedCode)
    {
        var result = Order.Place(Customer, "NZD", [new(sku, quantity, (decimal)unitPrice)], Now);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Place_WithoutLines_IsRejected() =>
        Assert.Equal(OrderErrors.NoLines, Order.Place(Customer, "NZD", [], Now).Error);

    [Fact]
    public void Place_WithTooManyLines_IsRejected()
    {
        var lines = Enumerable.Range(1, Order.MaxLines + 1).Select(i => new NewOrderLine($"SKU-{i}", 1, 1m)).ToList();

        Assert.Equal(OrderErrors.TooManyLines, Order.Place(Customer, "NZD", lines, Now).Error);
    }

    [Fact]
    public void Place_WithDuplicateSku_IsRejected() =>
        Assert.Equal("order.duplicate_sku", Order.Place(Customer, "NZD", [new("MOUSE", 1, 5m), new("MOUSE", 2, 5m)], Now).Error?.Code);

    [Fact]
    public void Place_WithoutCustomer_IsRejected() =>
        Assert.Equal(OrderErrors.CustomerRequired, Order.Place(new CustomerId(Guid.Empty), "NZD", [new("MOUSE", 1, 5m)], Now).Error);

    [Fact]
    public void MarkPaid_WithMatchingAmount_TransitionsToPaid()
    {
        var order = PlacedOrder();

        var result = order.MarkPaid("pay_1", Money(179m), Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal("pay_1", order.PaymentId);
        Assert.Equal(2, order.Version);
        var paid = Assert.IsType<OrderPaid>(order.DomainEvents[^1]);
        Assert.Equal(2, paid.AggregateVersion);
    }

    [Fact]
    public void MarkPaid_RedeliveredForSamePayment_IsIdempotent()
    {
        var order = PlacedOrder();
        order.MarkPaid("pay_1", Money(179m), Now);

        var again = order.MarkPaid("pay_1", Money(179m), Now);

        Assert.True(again.IsSuccess);
        Assert.Equal(2, order.Version);
        Assert.Equal(2, order.DomainEvents.Count);
    }

    [Fact]
    public void MarkPaid_ForADifferentPaymentOnceAlreadyPaid_IsAConflict()
    {
        var order = PlacedOrder();
        order.MarkPaid("pay_1", Money(179m), Now);

        var result = order.MarkPaid("pay_2", Money(179m), Now);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
    }

    [Theory]
    [InlineData(178.99, "NZD")]
    [InlineData(179, "AUD")]
    public void MarkPaid_WithWrongAmountOrCurrency_IsRejected(double amount, string currency)
    {
        var order = PlacedOrder();

        var result = order.MarkPaid("pay_1", Common.Money.Create((decimal)amount, currency).Value, Now);

        Assert.Equal("order.payment_mismatch", result.Error?.Code);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Single(order.DomainEvents);
    }

    [Fact]
    public void Ship_BeforePayment_IsAConflict()
    {
        var order = PlacedOrder();

        Assert.Equal("order.invalid_transition", order.Ship("NZ123", Now).Error?.Code);
    }

    [Fact]
    public void Ship_AfterPayment_RecordsTrackingNumber()
    {
        var order = PlacedOrder();
        order.MarkPaid("pay_1", Money(179m), Now);

        Assert.True(order.Ship("NZ123", Now).IsSuccess);
        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.Equal("NZ123", Assert.IsType<OrderShipped>(order.DomainEvents[^1]).TrackingNumber);
    }

    [Fact]
    public void Cancel_PaidOrder_AnnouncesThatARefundIsRequired()
    {
        var order = PlacedOrder();
        order.MarkPaid("pay_1", Money(179m), Now);

        order.Cancel("customer changed their mind", Now);

        Assert.True(Assert.IsType<OrderCancelled>(order.DomainEvents[^1]).RefundRequired);
    }

    [Fact]
    public void Cancel_UnpaidOrder_NeedsNoRefund()
    {
        var order = PlacedOrder();

        order.Cancel("duplicate order", Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.False(Assert.IsType<OrderCancelled>(order.DomainEvents[^1]).RefundRequired);
    }

    [Fact]
    public void Cancel_Twice_IsIdempotent()
    {
        var order = PlacedOrder();
        order.Cancel("duplicate order", Now);

        Assert.True(order.Cancel("duplicate order", Now).IsSuccess);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void Cancel_ShippedOrder_IsAConflict()
    {
        var order = PlacedOrder();
        order.MarkPaid("pay_1", Money(179m), Now);
        order.Ship("NZ123", Now);

        Assert.Equal(ErrorKind.Conflict, order.Cancel("too late", Now).Error?.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Commands_RequireTheirArguments(string blank)
    {
        var order = PlacedOrder();

        Assert.Equal(OrderErrors.ReasonRequired, order.Cancel(blank, Now).Error);
        Assert.Equal(OrderErrors.PaymentIdRequired, order.MarkPaid(blank, Money(179m), Now).Error);
        Assert.Equal(OrderErrors.TrackingNumberRequired, order.Ship(blank, Now).Error);
        Assert.Equal(1, order.Version);
    }

    [Fact]
    public void Rehydrate_RestoresStateWithoutRaisingEvents()
    {
        var snapshot = new OrderSnapshot(
            Guid.CreateVersion7(), Customer.Value, "NZD", OrderStatus.Paid, [new("KEYBOARD", 2, 89.50m)],
            Now, Now.AddHours(1), "pay_1", null, null, Version: 2);

        var order = Order.Rehydrate(snapshot);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(179m, order.Total.Amount);
        Assert.Equal(2, order.Version);
        Assert.False(order.HasChanges);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void Rehydrate_CorruptData_Throws()
    {
        var snapshot = new OrderSnapshot(
            Guid.CreateVersion7(), Customer.Value, "NZD", OrderStatus.Placed, [new("not a sku", 1, 1m)],
            Now, Now, null, null, null, Version: 1);

        Assert.Throws<InvalidOperationException>(() => Order.Rehydrate(snapshot));
    }

    [Fact]
    public void DequeueAndMarkPersisted_ResetChangeTracking()
    {
        var order = PlacedOrder();

        var events = order.DequeueDomainEvents();
        order.MarkPersisted();

        Assert.Single(events);
        Assert.Empty(order.DomainEvents);
        Assert.False(order.HasChanges);
    }

    private static Order PlacedOrder() => Order.Place(Customer, "NZD", [new("KEYBOARD", 2, 89.50m)], Now).Value;

    private static Money Money(decimal amount) => Common.Money.Create(amount, "NZD").Value;
}
