using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

[assembly: AssemblyFixture(typeof(OrderFlow.IntegrationTests.OrderFlowApiFactory))]

namespace OrderFlow.IntegrationTests;

/// <summary>
/// The real application, wired to real PostgreSQL and RabbitMQ containers. One instance is
/// shared by the whole test assembly; tests isolate themselves with unique ids.
/// </summary>
public sealed class OrderFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public async ValueTask InitializeAsync() => await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("RabbitMq:ConnectionString", _rabbitMq.GetConnectionString());
        builder.UseSetting("Outbox:PollInterval", "00:00:00.100");
        builder.UseSetting("OTEL_SDK_DISABLED", "true");
    }
}
