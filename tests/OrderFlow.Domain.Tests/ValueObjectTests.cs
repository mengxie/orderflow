using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Domain.Tests;

public sealed class ValueObjectTests
{
    [Fact]
    public void Money_EqualityIsByValue() =>
        Assert.Equal(Money.Create(10.0m, "NZD").Value, Money.Create(10.00m, "NZD").Value);

    [Fact]
    public void Money_ArithmeticKeepsTheCurrency()
    {
        var total = Money.Create(12.50m, "NZD").Value.Multiply(3).Add(Money.Create(2.50m, "NZD").Value);

        Assert.Equal("40.00 NZD", total.ToString());
    }

    [Fact]
    public void Money_RefusesToMixCurrencies() =>
        Assert.Throws<InvalidOperationException>(() => Money.Zero("NZD").Add(Money.Zero("AUD")));

    [Theory]
    [InlineData("A1")]
    [InlineData("KEYBOARD-MX-2026")]
    public void Sku_AcceptsValidCodes(string value) => Assert.Equal(value, Sku.Create(value).Value.Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-LEADING")]
    [InlineData("lower")]
    [InlineData("WAY-TOO-LONG-FOR-A-SKU-0123456789")]
    public void Sku_RejectsInvalidCodes(string? value) => Assert.Equal("sku.invalid", Sku.Create(value).Error?.Code);

    [Fact]
    public void Result_ValueOfAFailure_Throws()
    {
        Result<int> result = Error.NotFound("thing.missing", "No thing.");

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void OrderId_IsTimeOrdered()
    {
        var first = OrderId.New();
        var second = OrderId.New();

        Assert.True(first.Value.CompareTo(second.Value) < 0);
    }
}
