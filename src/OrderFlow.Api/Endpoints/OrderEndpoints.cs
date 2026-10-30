using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Orders;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Api.Endpoints;

internal static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");

        orders.MapPost("/", PlaceOrderAsync)
            .WithName("PlaceOrder")
            .WithSummary("Places an order. Retrying with the same Idempotency-Key returns the original order.");
        orders.MapGet("/{orderId:guid}", GetOrderAsync).WithName("GetOrder");
        orders.MapPost("/{orderId:guid}/cancel", CancelOrderAsync).WithName("CancelOrder");
        orders.MapPost("/{orderId:guid}/ship", ShipOrderAsync).WithName("ShipOrder");

        return app;
    }

    private static async Task<Results<Created<OrderResponse>, Ok<OrderResponse>, ProblemHttpResult>> PlaceOrderAsync(
        PlaceOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ICommandHandler<PlaceOrderCommand, PlaceOrderResult> handler,
        CancellationToken cancellationToken)
    {
        var command = new PlaceOrderCommand(
            request.CustomerId,
            request.Currency,
            [.. (request.Lines ?? []).Select(line => new NewOrderLine(line.Sku, line.Quantity, line.UnitPrice))],
            idempotencyKey ?? string.Empty);

        var result = await handler.HandleAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        var (order, created) = result.Value;
        return created ? TypedResults.Created($"/orders/{order.Id}", order) : TypedResults.Ok(order);
    }

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> GetOrderAsync(
        Guid orderId,
        IQueryHandler<GetOrderQuery, OrderResponse> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetOrderQuery(orderId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> CancelOrderAsync(
        Guid orderId,
        CancelOrderRequest request,
        ICommandHandler<CancelOrderCommand, OrderResponse> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CancelOrderCommand(orderId, request.Reason ?? string.Empty), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> ShipOrderAsync(
        Guid orderId,
        ShipOrderRequest request,
        ICommandHandler<ShipOrderCommand, OrderResponse> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ShipOrderCommand(orderId, request.TrackingNumber ?? string.Empty), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
