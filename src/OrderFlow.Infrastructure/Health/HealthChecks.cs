using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using OrderFlow.Infrastructure.Messaging;
using OrderFlow.Infrastructure.Outbox;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.Infrastructure.Health;

internal sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (NpgsqlException exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unreachable.", exception);
        }
    }
}

internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await connection.GetAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", exception);
        }
    }
}

/// <summary>Degraded when events are not leaving the outbox: the earliest symptom of a broker problem.</summary>
internal sealed class OutboxHealthCheck(NpgsqlDataSource dataSource, IOptions<OutboxOptions> options, TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var stats = await connection.QuerySingleAsync<OutboxStatsRow>(new CommandDefinition(OutboxSql.Stats, cancellationToken: cancellationToken));
        var data = new Dictionary<string, object> { ["deadLettered"] = stats.DeadLettered };

        if (stats.OldestPending is { } oldest)
        {
            var lag = timeProvider.GetUtcNow() - SqlTime.ToOffset(oldest);
            data["lagSeconds"] = Math.Round(lag.TotalSeconds, 1);
            if (lag > options.Value.LagWarningThreshold)
            {
                return HealthCheckResult.Degraded($"The oldest unpublished event is {lag.TotalSeconds:0}s old.", data: data);
            }
        }

        return stats.DeadLettered > 0
            ? HealthCheckResult.Degraded($"{stats.DeadLettered} event(s) were dead-lettered.", data: data)
            : HealthCheckResult.Healthy(data: data);
    }
}
