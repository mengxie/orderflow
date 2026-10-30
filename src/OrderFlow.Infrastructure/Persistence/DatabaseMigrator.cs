using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace OrderFlow.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public bool MigrateOnStartup { get; set; } = true;
}

/// <summary>
/// Applies the embedded SQL scripts in order, once each, in a single transaction. A
/// transaction-scoped advisory lock makes concurrent startups of several instances safe.
/// </summary>
internal sealed class DatabaseMigrator(NpgsqlDataSource dataSource, ILogger<DatabaseMigrator> logger)
{
    private const long MigrationLockKey = 0x4F_52_44_45_52;
    private const string ResourcePrefix = "migrations/";

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@Key)", new { Key = MigrationLockKey }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "CREATE TABLE IF NOT EXISTS schema_migrations (version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())",
            transaction: transaction,
            cancellationToken: cancellationToken));

        var applied = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT version FROM schema_migrations", transaction: transaction, cancellationToken: cancellationToken))).ToHashSet(StringComparer.Ordinal);

        foreach (var (version, sql) in LoadScripts())
        {
            if (applied.Contains(version))
            {
                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_migrations (version) VALUES (@Version)", new { Version = version }, transaction, cancellationToken: cancellationToken));
            InfrastructureLog.MigrationApplied(logger, version);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    internal static IEnumerable<(string Version, string Sql)> LoadScripts()
    {
        var assembly = typeof(DatabaseMigrator).Assembly;
        foreach (var name in assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Migration {name} could not be read.");
            using var reader = new StreamReader(stream);
            yield return (Path.GetFileNameWithoutExtension(name[ResourcePrefix.Length..]), reader.ReadToEnd());
        }
    }
}

/// <summary>Runs before the outbox relay and consumer start, because hosted services start in order.</summary>
internal sealed class DatabaseMigrationHostedService(DatabaseMigrator migrator, IOptions<DatabaseOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        options.Value.MigrateOnStartup ? migrator.MigrateAsync(cancellationToken) : Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
