namespace OrderFlow.Infrastructure.Persistence;

/// <summary>All SQL in one place: hand-written, reviewable, and validated against PostgreSQL in CI.</summary>
internal static class OrderSql
{
    public const string SelectOrder = """
        SELECT id, customer_id, status, currency, total, payment_id, tracking_number,
               cancellation_reason, version, placed_at, updated_at
        FROM orders
        WHERE id = @Id
        """;

    public const string SelectLines = """
        SELECT sku, quantity, unit_price
        FROM order_lines
        WHERE order_id = @OrderId
        ORDER BY line_no
        """;

    public const string SelectIdByIdempotencyKey = "SELECT id FROM orders WHERE idempotency_key = @IdempotencyKey";

    public const string InsertOrder = """
        INSERT INTO orders (id, customer_id, status, currency, total, payment_id, tracking_number,
                            cancellation_reason, idempotency_key, version, placed_at, updated_at)
        VALUES (@Id, @CustomerId, @Status, @Currency, @Total, @PaymentId, @TrackingNumber,
                @CancellationReason, @IdempotencyKey, @Version, @PlacedAt, @UpdatedAt)
        """;

    public const string InsertLine = """
        INSERT INTO order_lines (order_id, line_no, sku, quantity, unit_price)
        VALUES (@OrderId, @LineNo, @Sku, @Quantity, @UnitPrice)
        """;

    /// <summary>Optimistic concurrency: zero rows updated means someone else saved first.</summary>
    public const string UpdateOrder = """
        UPDATE orders
        SET status = @Status, payment_id = @PaymentId, tracking_number = @TrackingNumber,
            cancellation_reason = @CancellationReason, version = @Version, updated_at = @UpdatedAt
        WHERE id = @Id AND version = @ExpectedVersion
        """;
}

internal static class OutboxSql
{
    public const string Insert = """
        INSERT INTO outbox_messages (id, type, payload, trace_parent, occurred_at, next_attempt_at)
        VALUES (@Id, @Type, CAST(@Payload AS jsonb), @TraceParent, @OccurredAt, @NextAttemptAt)
        """;

    /// <summary>SKIP LOCKED lets any number of relays run concurrently without blocking or double-publishing.</summary>
    public const string ClaimBatch = """
        SELECT id, type, payload::text AS payload, trace_parent, occurred_at, attempts
        FROM outbox_messages
        WHERE processed_at IS NULL AND dead_lettered_at IS NULL AND next_attempt_at <= @Now
        ORDER BY occurred_at
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED
        """;

    public const string MarkPublished = """
        UPDATE outbox_messages
        SET processed_at = @PublishedAt, attempts = attempts + 1, last_error = NULL
        WHERE id = @Id
        """;

    public const string MarkFailed = """
        UPDATE outbox_messages
        SET attempts = @Attempts, next_attempt_at = @NextAttemptAt, last_error = @LastError,
            dead_lettered_at = @DeadLetteredAt
        WHERE id = @Id
        """;

    public const string DeleteProcessed = "DELETE FROM outbox_messages WHERE processed_at < @Cutoff";

    public const string Stats = """
        SELECT (SELECT min(occurred_at) FROM outbox_messages
                WHERE processed_at IS NULL AND dead_lettered_at IS NULL) AS oldest_pending,
               (SELECT count(*) FROM outbox_messages WHERE dead_lettered_at IS NOT NULL) AS dead_lettered
        """;
}

internal static class InboxSql
{
    public const string Insert = """
        INSERT INTO inbox_messages (message_id, consumer, processed_at)
        VALUES (@MessageId, @Consumer, @ProcessedAt)
        ON CONFLICT (message_id, consumer) DO NOTHING
        """;

    public const string DeleteProcessed = "DELETE FROM inbox_messages WHERE processed_at < @Cutoff";
}

internal static class SqlTime
{
    /// <summary>Npgsql returns timestamptz as a UTC <see cref="DateTime"/>.</summary>
    public static DateTimeOffset ToOffset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
