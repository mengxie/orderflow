using System.Text.RegularExpressions;
using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Orders;

/// <summary>Stock keeping unit: 2 to 32 upper-case letters, digits or hyphens.</summary>
public sealed partial record Sku
{
    private Sku(string value) => Value = value;

    public string Value { get; }

    public static Result<Sku> Create(string? value)
    {
        if (value is null || !SkuPattern().IsMatch(value))
        {
            return Error.Validation("sku.invalid", $"'{value}' is not a valid SKU: use 2 to 32 upper-case letters, digits or hyphens.");
        }

        return new Sku(value);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{1,31}$")]
    private static partial Regex SkuPattern();
}
