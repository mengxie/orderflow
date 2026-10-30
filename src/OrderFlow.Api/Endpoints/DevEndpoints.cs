using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts;
using OrderFlow.Infrastructure.Messaging;

namespace OrderFlow.Api.Endpoints;

/// <summary>Development-only helpers that stand in for other services.</summary>
internal static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/dev").WithTags("Development")
            .MapPost("/payments", SimulatePaymentAsync)
            .WithSummary("Publishes PaymentCapturedV1 to RabbitMQ, exactly as the payments service would.");
        return app;
    }

    private static async Task<Accepted<PaymentCapturedV1>> SimulatePaymentAsync(
        SimulatePaymentRequest request,
        IMessagePublisher publisher,
        IOptions<RabbitMqOptions> options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var payment = new PaymentCapturedV1(
            Guid.CreateVersion7(),
            timeProvider.GetUtcNow(),
            request.OrderId,
            request.PaymentId ?? "pay_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            request.Amount,
            request.Currency);
        await publisher.PublishAsync(options.Value.PaymentsExchange, MessageSerializer.ToMessage(payment), cancellationToken);
        return TypedResults.Accepted((string?)null, payment);
    }
}
