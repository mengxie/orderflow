using OrderFlow.Application.Orders;
using OrderFlow.Contracts;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Abstractions;

/// <summary>Write-side persistence for the order aggregate.</summary>
public interface IOrderRepository
{
    Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken);

    Task<OrderId?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <exception cref="ConcurrencyConflictException">The idempotency key is already in use.</exception>
    Task AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken);

    /// <exception cref="ConcurrencyConflictException">The order changed since it was loaded.</exception>
    Task UpdateAsync(Order order, CancellationToken cancellationToken);
}

/// <summary>Read side: returns DTOs straight from storage, bypassing the domain model.</summary>
public interface IOrderQueries
{
    Task<OrderResponse?> GetAsync(Guid orderId, CancellationToken cancellationToken);
}

/// <summary>
/// One database transaction. Committing also writes the integration events raised by every
/// aggregate saved in it to the outbox, atomically with the state change.
/// </summary>
public interface IUnitOfWork
{
    Task BeginAsync(CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

/// <summary>Translates internal domain events into public, versioned integration events.</summary>
public interface IDomainEventMapper
{
    IIntegrationEvent? Map(IDomainEvent domainEvent);
}

/// <summary>Optimistic concurrency check failed: someone else changed the data first.</summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
