namespace OrderFlow.Domain.Common;

/// <summary>Something that happened inside an aggregate, in the past tense.</summary>
public interface IDomainEvent
{
    /// <summary>The aggregate version this event produced; consumers use it to order and deduplicate.</summary>
    int AggregateVersion { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>Non-generic view of an aggregate, used by the unit of work to collect events.</summary>
public interface IAggregateRoot
{
    int Version { get; }

    int PersistedVersion { get; }

    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    IReadOnlyList<IDomainEvent> DequeueDomainEvents();

    void MarkPersisted();
}

/// <summary>
/// Consistency boundary. Every state change raises exactly one domain event and increments
/// <see cref="Version"/>, which doubles as the optimistic-concurrency token.
/// </summary>
public abstract class AggregateRoot<TId> : IAggregateRoot
    where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id, int version)
    {
        Id = id;
        Version = version;
        PersistedVersion = version;
    }

    public TId Id { get; }

    public int Version { get; private set; }

    /// <summary>The version this aggregate had when it was loaded or last saved.</summary>
    public int PersistedVersion { get; private set; }

    public bool HasChanges => Version != PersistedVersion;

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected int NextVersion => Version + 1;

    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }

    public void MarkPersisted() => PersistedVersion = Version;

    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.AggregateVersion != NextVersion)
        {
            throw new InvalidOperationException(
                $"Event {domainEvent.GetType().Name} has version {domainEvent.AggregateVersion}, expected {NextVersion}.");
        }

        _domainEvents.Add(domainEvent);
        Version = domainEvent.AggregateVersion;
    }
}
