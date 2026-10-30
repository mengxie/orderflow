using System.Diagnostics;
using Dapper;
using Npgsql;
using OrderFlow.Application.Abstractions;
using OrderFlow.Contracts;
using OrderFlow.Domain.Common;
using OrderFlow.Infrastructure.Messaging;

namespace OrderFlow.Infrastructure.Persistence;

/// <summary>
/// The scoped connection and transaction shared by repositories, queries and the inbox.
/// <see cref="CommitAsync"/> writes the integration events of every aggregate saved in the
/// transaction to the outbox before committing, so state changes and the events announcing
/// them are stored atomically: never one without the other.
/// </summary>
public sealed class DbSession(NpgsqlDataSource dataSource, IDomainEventMapper eventMapper, TimeProvider timeProvider)
    : IUnitOfWork, IAsyncDisposable
{
    private readonly List<IAggregateRoot> _tracked = [];
    private NpgsqlConnection? _connection;

    public NpgsqlTransaction? Transaction { get; private set; }

    public async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken) =>
        _connection ??= await dataSource.OpenConnectionAsync(cancellationToken);

    public NpgsqlTransaction RequireTransaction() =>
        Transaction ?? throw new InvalidOperationException("Writes must happen inside a unit of work.");

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (Transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }

        var connection = await GetConnectionAsync(cancellationToken);
        Transaction = await connection.BeginTransactionAsync(cancellationToken);
    }

    public void Track(IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        if (!_tracked.Contains(aggregate))
        {
            _tracked.Add(aggregate);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = RequireTransaction();
        try
        {
            await WriteOutboxAsync(transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await EndTransactionAsync();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (Transaction is null)
        {
            return;
        }

        try
        {
            await Transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await EndTransactionAsync();
        }
    }

    /// <summary>Returns false when this consumer already processed the message.</summary>
    internal async Task<bool> TryRecordInboxMessageAsync(Guid messageId, string consumer, CancellationToken cancellationToken)
    {
        var transaction = RequireTransaction();
        var connection = await GetConnectionAsync(cancellationToken);
        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            InboxSql.Insert,
            new { MessageId = messageId, Consumer = consumer, ProcessedAt = timeProvider.GetUtcNow().UtcDateTime },
            transaction,
            cancellationToken: cancellationToken));
        return inserted == 1;
    }

    public async ValueTask DisposeAsync()
    {
        await EndTransactionAsync();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private async Task WriteOutboxAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var traceParent = Activity.Current?.Id;
        var records = new List<OutboxRecord>();
        foreach (var aggregate in _tracked)
        {
            foreach (var domainEvent in aggregate.DequeueDomainEvents())
            {
                if (eventMapper.Map(domainEvent) is not { } integrationEvent)
                {
                    continue;
                }

                records.Add(new OutboxRecord(
                    integrationEvent.EventId,
                    MessageTypes.NameOf(integrationEvent.GetType()),
                    MessageSerializer.Serialize(integrationEvent),
                    traceParent,
                    integrationEvent.OccurredAt.UtcDateTime,
                    now));
            }
        }

        if (records.Count > 0)
        {
            var connection = await GetConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(OutboxSql.Insert, records, transaction, cancellationToken: cancellationToken));
        }
    }

    private async Task EndTransactionAsync()
    {
        if (Transaction is not null)
        {
            await Transaction.DisposeAsync();
            Transaction = null;
        }

        _tracked.Clear();
    }
}
