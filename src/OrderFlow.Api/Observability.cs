using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderFlow.Application.Abstractions;

namespace OrderFlow.Api;

internal static class Observability
{
    private const string ServiceName = "orderflow-api";

    /// <summary>
    /// Traces, metrics and logs over OTLP. The endpoint comes from OTEL_EXPORTER_OTLP_ENDPOINT,
    /// so the same build talks to the Aspire dashboard locally and to any OTLP backend in production.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(OrderFlowTelemetry.Name, "Npgsql")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(OrderFlowTelemetry.Name, "Npgsql")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithLogging()
            .UseOtlpExporter();
        return builder;
    }
}
