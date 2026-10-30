using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Abstractions;
using OrderFlow.Domain.Common;

namespace OrderFlow.Application.Behaviors;

/// <summary>Adds a trace span, a duration metric and structured logs around every command.</summary>
public sealed class InstrumentedCommandHandler<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    ILogger<InstrumentedCommandHandler<TCommand, TResponse>> logger) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    private static readonly string CommandName = typeof(TCommand).Name;

    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        using var activity = OrderFlowTelemetry.ActivitySource.StartActivity(CommandName);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await inner.HandleAsync(command, cancellationToken);
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (result.IsSuccess)
            {
                ApplicationLog.CommandSucceeded(logger, CommandName, elapsedMs);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                ApplicationLog.CommandRejected(logger, CommandName, result.Error.Code, result.Error.Message, elapsedMs);
            }

            Record(elapsedMs, result.IsSuccess ? "success" : "rejected");
            return result;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
            ApplicationLog.CommandFailed(logger, exception, CommandName);
            Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, "error");
            throw;
        }
    }

    private static void Record(double elapsedMs, string outcome) =>
        OrderFlowTelemetry.CommandDuration.Record(
            elapsedMs,
            new KeyValuePair<string, object?>("command", CommandName),
            new KeyValuePair<string, object?>("outcome", outcome));
}
