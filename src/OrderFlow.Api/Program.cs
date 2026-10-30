using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderFlow.Api;
using OrderFlow.Api.Endpoints;
using OrderFlow.Application;
using OrderFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapDevEndpoints();
}

app.MapOrderEndpoints();

// Liveness: the process is up. Readiness: dependencies are reachable and events are flowing.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();

/// <summary>Entry point; public so the integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
