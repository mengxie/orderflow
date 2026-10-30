using System.Globalization;
using System.Text.RegularExpressions;

namespace OrderFlow.Domain.Common;

/// <summary>An amount in a specific currency. Immutable; arithmetic never mixes currencies.</summary>
public sealed partial record Money
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    /// <summary>ISO 4217 code, such as <c>NZD</c>.</summary>
    public string Currency { get; }

    public static Result<Money> Create(decimal amount, string? currency)
    {
        if (currency is null || !CurrencyPattern().IsMatch(currency))
        {
            return Error.Validation("money.invalid_currency", "Currency must be a three-letter ISO 4217 code such as NZD.");
        }

        if (amount < 0)
        {
            return Error.Validation("money.negative_amount", "Amounts cannot be negative.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            return Error.Validation("money.too_precise", "Amounts can have at most two decimal places.");
        }

        return new Money(amount, currency);
    }

    public static Money Zero(string currency) => Create(0, currency).Value;

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException($"Cannot add {other.Currency} to {Currency}.");
        }

        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int factor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(factor);
        return new Money(Amount * factor, Currency);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyPattern();
}
