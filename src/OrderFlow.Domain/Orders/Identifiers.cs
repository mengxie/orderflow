namespace OrderFlow.Domain.Orders;

/// <summary>Strongly typed id; cannot be confused with a <see cref="CustomerId"/> at compile time.</summary>
public readonly record struct OrderId(Guid Value)
{
    /// <summary>Version 7 UUIDs are time-ordered, which keeps B-tree index inserts append-only.</summary>
    public static OrderId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct CustomerId(Guid Value)
{
    public override string ToString() => Value.ToString();
}
