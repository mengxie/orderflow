using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Behaviors;
using OrderFlow.Application.Orders;
using OrderFlow.Contracts;

namespace OrderFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDomainEventMapper, DomainEventMapper>();

        services.AddCommand<PlaceOrderCommand, PlaceOrderResult, PlaceOrderHandler>();
        services.AddCommand<CancelOrderCommand, OrderResponse, CancelOrderHandler>();
        services.AddCommand<ShipOrderCommand, OrderResponse, ShipOrderHandler>();
        services.AddCommand<RecordPaymentCommand, OrderResponse, RecordPaymentHandler>();

        services.AddScoped<IQueryHandler<GetOrderQuery, OrderResponse>, GetOrderHandler>();
        services.AddScoped<IIntegrationEventConsumer<PaymentCapturedV1>, PaymentCapturedConsumer>();
        return services;
    }

    /// <summary>
    /// Registers a command handler behind its decorators, without a mediator library:
    /// instrumentation → transaction → handler. Endpoints depend on the interface; the
    /// concrete handler stays injectable for callers that already own a transaction.
    /// </summary>
    private static void AddCommand<TCommand, TResponse, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResponse>
        where THandler : class, ICommandHandler<TCommand, TResponse>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResponse>>(provider =>
            new InstrumentedCommandHandler<TCommand, TResponse>(
                new TransactionalCommandHandler<TCommand, TResponse>(
                    provider.GetRequiredService<THandler>(),
                    provider.GetRequiredService<IUnitOfWork>()),
                provider.GetRequiredService<ILogger<InstrumentedCommandHandler<TCommand, TResponse>>>()));
    }
}
