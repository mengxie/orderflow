namespace OrderFlow.Infrastructure.Persistence;

// Dapper materialises these with snake_case → PascalCase matching enabled.
internal sealed class OrderRow
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public string Status { get; set; } = "";

    public string Currency { get; set; } = "";

    public decimal Total { get; set; }

    public string? PaymentId { get; set; }

    public string? TrackingNumber { get; set; }

    public string? CancellationReason { get; set; }

    public int Version { get; set; }

    public DateTime PlacedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

internal sealed class OrderLineRow
{
    public string Sku { get; set; } = "";

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}

internal sealed class OutboxRow
{
    public Guid Id { get; set; }

    public string Type { get; set; } = "";

    public string Payload { get; set; } = "";

    public string? TraceParent { get; set; }

    public DateTime OccurredAt { get; set; }

    public int Attempts { get; set; }
}

internal sealed class OutboxStatsRow
{
    public DateTime? OldestPending { get; set; }

    public long DeadLettered { get; set; }
}

internal sealed record OutboxRecord(Guid Id, string Type, string Payload, string? TraceParent, DateTime OccurredAt, DateTime NextAttemptAt);
