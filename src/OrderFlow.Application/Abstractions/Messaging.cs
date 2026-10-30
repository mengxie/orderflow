using OrderFlow.Contracts;
using OrderFlow.Domain.Common;

namespace OrderFlow.Application.Abstractions;

/// <summary>A request to change state. Handled by exactly one <see cref="ICommandHandler{TCommand,TResponse}"/>.</summary>
public interface ICommand<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>A request to read state. Never changes anything.</summary>
public interface IQuery<TResponse>;

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Reacts to an integration event received from another service.</summary>
public interface IIntegrationEventConsumer<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
