using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OrderFlow.Infrastructure.Messaging;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 100;

    /// <summary>Attempts before a message is dead-lettered (kept, but no longer retried).</summary>
    public int MaxAttempts { get; set; } = 10;

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long processed outbox and inbox rows are kept for auditing and deduplication.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Health turns Degraded when the oldest pending message is older than this.</summary>
    public TimeSpan LagWarningThreshold { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Publishes outbox messages. Each batch is claimed with FOR UPDATE SKIP LOCKED, so several
/// instances can relay in parallel without publishing the same message twice in the normal
/// case. Delivery is still at least once (a crash between publish and commit re-publishes),
/// which is why every message carries a stable id for consumers to deduplicate on.
/// </summary>
internal sealed class OutboxRelay(
    NpgsqlDataSource dataSource,
    IMessagePublisher publisher,
    IOptions<OutboxOptions> options,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    TimeProvider timeProvider,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(settings.PollInterval, timeProvider);
        do
        {
            try
            {
                // Drain a backlog without waiting for the next tick.
                while (await RelayBatchAsync(stoppingToken) == settings.BatchSize)
                {
                }

                await CleanUpAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                InfrastructureLog.RelayCycleFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<int> RelayBatchAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var batch = (await connection.QueryAsync<OutboxRow>(new CommandDefinition(
            OutboxSql.ClaimBatch,
            new { Now = timeProvider.GetUtcNow().UtcDateTime, options.Value.BatchSize },
            transaction,
            cancellationToken: cancellationToken))).AsList();

        foreach (var message in batch)
        {
            await RelayAsync(connection, transaction, message, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    private async Task RelayAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OutboxRow message, CancellationToken cancellationToken)
    {
        var exchange = rabbitMqOptions.Value.EventsExchange;
        using var activity = TraceContext.StartActivity($"{message.Type} publish", ActivityKind.Producer, message.TraceParent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", exchange);
        activity?.SetTag("messaging.message.id", message.Id.ToString());

        try
        {
            var body = System.Text.Encoding.UTF8.GetBytes(message.Payload);
            await publisher.PublishAsync(exchange, new OutgoingMessage(message.Id, message.Type, body, SqlTime.ToOffset(message.OccurredAt)), cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                OutboxSql.MarkPublished,
                new { message.Id, PublishedAt = timeProvider.GetUtcNow().UtcDateTime },
                transaction,
                cancellationToken: cancellationToken));
            InfrastructureMetrics.OutboxPublished.Add(1, new KeyValuePair<string, object?>("type", message.Type));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            var now = timeProvider.GetUtcNow();
            var attempts = message.Attempts + 1;
            var deadLetter = attempts >= options.Value.MaxAttempts;
            var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempts), options.Value.MaxRetryDelay.TotalSeconds));
            await connection.ExecuteAsync(new CommandDefinition(
                OutboxSql.MarkFailed,
                new
                {
                    message.Id,
                    Attempts = attempts,
                    NextAttemptAt = (now + delay).UtcDateTime,
                    LastError = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message,
                    DeadLetteredAt = deadLetter ? now.UtcDateTime : (DateTime?)null,
                },
                transaction,
                cancellationToken: cancellationToken));

            if (deadLetter)
            {
                InfrastructureMetrics.OutboxDeadLettered.Add(1, new KeyValuePair<string, object?>("type", message.Type));
                InfrastructureLog.MessageDeadLettered(logger, exception, message.Id, message.Type, attempts);
            }
            else
            {
                InfrastructureMetrics.OutboxFailed.Add(1, new KeyValuePair<string, object?>("type", message.Type));
                InfrastructureLog.PublishAttemptFailed(logger, exception, message.Id, message.Type, attempts, delay);
            }
        }
    }

    private async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now - _lastCleanup < TimeSpan.FromHours(1))
        {
            return;
        }

        _lastCleanup = now;
        var cutoff = new { Cutoff = (now - options.Value.Retention).UtcDateTime };
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(OutboxSql.DeleteProcessed, cutoff, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(InboxSql.DeleteProcessed, cutoff, cancellationToken: cancellationToken));
    }
}
