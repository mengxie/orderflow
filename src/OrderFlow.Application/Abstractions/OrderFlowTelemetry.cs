using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OrderFlow.Application.Abstractions;

/// <summary>
/// The service's own traces and metrics, built on the BCL so that no layer depends on an
/// observability vendor. The API host wires these into OpenTelemetry.
/// </summary>
public static class OrderFlowTelemetry
{
    public const string Name = "OrderFlow";

    public static readonly ActivitySource ActivitySource = new(Name);

    public static readonly Meter Meter = new(Name);

    public static readonly Histogram<double> CommandDuration =
        Meter.CreateHistogram<double>("orderflow.command.duration", unit: "ms", description: "Time to handle a command.");
}
