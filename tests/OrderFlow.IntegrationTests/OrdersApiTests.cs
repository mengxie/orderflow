using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderFlow.Application.Orders;

namespace OrderFlow.IntegrationTests;

public sealed class OrdersApiTests(OrderFlowApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PlaceOrder_ReturnsCreatedWithLocation()
    {
        using var response = await Api.PlaceOrderAsync(_client, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(Ct);
        Assert.NotNull(order);
        Assert.Equal("Placed", order.Status);
        Assert.Equal(204.00m, order.Total);
        Assert.Equal($"/orders/{order.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PlaceOrder_RetriedWithTheSameIdempotencyKey_ReturnsTheSameOrder()
    {
        var key = Guid.NewGuid().ToString();

        using var first = await Api.PlaceOrderAsync(_client, key);
        using var retry = await Api.PlaceOrderAsync(_client, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<OrderResponse>(Ct))!.Id,
            (await retry.Content.ReadFromJsonAsync<OrderResponse>(Ct))!.Id);
    }

    [Fact]
    public async Task PlaceOrder_WithoutIdempotencyKey_ReturnsProblemDetails()
    {
        using var response = await Api.PlaceOrderAsync(_client, idempotencyKey: null);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "request.invalid_idempotency_key");
    }

    [Fact]
    public async Task PlaceOrder_WithInvalidSku_ReturnsProblemDetails()
    {
        var body = new { customerId = Guid.CreateVersion7(), currency = "NZD", lines = new[] { new { sku = "not a sku", quantity = 1, unitPrice = 1m } } };

        using var response = await Api.PlaceOrderAsync(_client, Guid.NewGuid().ToString(), body);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "sku.invalid");
    }

    [Fact]
    public async Task GetOrder_Unknown_Returns404()
    {
        using var response = await _client.GetAsync($"/orders/{Guid.CreateVersion7()}", Ct);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "order.not_found");
    }

    [Fact]
    public async Task ShipOrder_BeforePayment_Returns409()
    {
        var order = await Api.PlaceOrderAsync(_client);

        using var response = await _client.PostAsJsonAsync($"/orders/{order.Id}/ship", new { trackingNumber = "NZ123" }, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "order.invalid_transition");
    }

    [Fact]
    public async Task CancelOrder_ReturnsTheCancelledOrder()
    {
        var order = await Api.PlaceOrderAsync(_client);

        using var response = await _client.PostAsJsonAsync($"/orders/{order.Id}/cancel", new { reason = "duplicate" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", (await response.Content.ReadFromJsonAsync<OrderResponse>(Ct))!.Status);
    }

    [Fact]
    public async Task ReadinessProbe_ReportsDependenciesReachable()
    {
        using var response = await _client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }
}
