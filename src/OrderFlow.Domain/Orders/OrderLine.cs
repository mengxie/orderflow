using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Orders;

/// <summary>A requested line, as received from outside the domain and not yet validated.</summary>
public sealed record NewOrderLine(string? Sku, int Quantity, decimal UnitPrice);

/// <summary>A validated order line. Immutable once the order is placed.</summary>
public sealed record OrderLine
{
    public const int MaxQuantity = 1_000;

    private OrderLine(Sku sku, int quantity, Money unitPrice)
    {
        Sku = sku;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Sku Sku { get; }

    public int Quantity { get; }

    public Money UnitPrice { get; }

    public Money LineTotal => UnitPrice.Multiply(Quantity);

    public static Result<OrderLine> Create(NewOrderLine line, string currency)
    {
        ArgumentNullException.ThrowIfNull(line);
        var sku = Sku.Create(line.Sku);
        if (sku.IsFailure)
        {
            return sku.Error;
        }

        if (line.Quantity is < 1 or > MaxQuantity)
        {
            return Error.Validation("order_line.invalid_quantity", $"Quantity must be between 1 and {MaxQuantity}.");
        }

        var unitPrice = Money.Create(line.UnitPrice, currency);
        if (unitPrice.IsFailure)
        {
            return unitPrice.Error;
        }

        return new OrderLine(sku.Value, line.Quantity, unitPrice.Value);
    }
}
