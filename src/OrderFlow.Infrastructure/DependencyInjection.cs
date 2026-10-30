using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OrderFlow.Application.Abstractions;
using OrderFlow.Infrastructure.Health;
using OrderFlow.Infrastructure.Inbox;
using OrderFlow.Infrastructure.Messaging;
using OrderFlow.Infrastructure.Outbox;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.Section));
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.Section));
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.Section));

        // Persistence: one session (connection + transaction) per scope.
        services.AddScoped<DbSession>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<DbSession>());
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderQueries, OrderQueries>();
        services.AddSingleton<DatabaseMigrator>();

        // Messaging
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddSingleton<MessageTypeRegistry>();
        services.AddSingleton<InboxProcessor>();

        // Hosted services start in registration order: migrate first, then relay and consume.
        services.AddHostedService<DatabaseMigrationHostedService>();
        services.AddHostedService<OutboxRelay>();
        services.AddHostedService<RabbitMqConsumer>();

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"])
            .AddCheck<OutboxHealthCheck>("outbox", tags: ["ready"]);

        return services;
    }
}
