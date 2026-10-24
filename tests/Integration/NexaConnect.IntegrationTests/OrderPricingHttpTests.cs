extern alias ORDER;
extern alias CATALOG;
using System.Net;
using System.Net.Http.Json;
using NexaConnect.Contracts.Platform;
using ORDER::NexaConnect.Services.Order.Application.Orders;
using CATALOG::NexaConnect.Services.Catalog.Application.Menu;

namespace NexaConnect.IntegrationTests;

public sealed class OrderPricingHttpTests : IClassFixture<RestaurantWorkflowServiceFixture>
{
    private readonly RestaurantWorkflowServiceFixture fixture;
    public OrderPricingHttpTests(RestaurantWorkflowServiceFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task Caller_amounts_cannot_override_preview_and_confirmation_is_required()
    {
        using var catalog = fixture.Catalog.CreateClient();
        using var order = fixture.Order.CreateClient();
        Guid product = Guid.NewGuid();
        using var seeded = await catalog.PostAsJsonAsync($"/api/catalog/v1/branches/{RestaurantWorkflowServiceFixture.BranchId}/menu-items",
            new CreateMenuItem(product, "Pricing", 100, "THB", "grill"));
        Assert.True(seeded.IsSuccessStatusCode);
        var command = new
        {
            RestaurantId = RestaurantWorkflowServiceFixture.RestaurantId,
            OrganizationId = RestaurantWorkflowServiceFixture.OrganizationId,
            BranchId = RestaurantWorkflowServiceFixture.BranchId, Currency = "THB", PaymentMethod = "cash_manual",
            IdempotencyKey = Guid.NewGuid().ToString("N"), OrderId = Guid.NewGuid(),
            Lines = new[] { new { ProductId = product, Quantity = 1, UnitPrice = 1m } }, TotalAmount = 1m
        };
        using var quoted = await order.PostAsJsonAsync("/api/order/v1/workflows/quote", command);
        var quote = (await quoted.Content.ReadFromJsonAsync<OrderQuote>())!;
        Assert.Equal(117.70m, quote.Pricing.TotalAmount);
        using var rejected = await order.PostAsJsonAsync("/api/order/v1/workflows/place", command);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var missing = await order.GetAsync($"/api/order/v1/orders/{command.OrderId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("Bearer unauthenticated", HttpStatusCode.Unauthorized)]
    [InlineData("Bearer customer-token", HttpStatusCode.Forbidden)]
    public async Task Quote_enforces_authentication_and_tenant_permission(string authorization, HttpStatusCode expected)
    {
        using var client = fixture.Order.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/order/v1/workflows/quote");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId, Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        request.Content = JsonContent.Create(new { RestaurantId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), BranchId = Guid.NewGuid(), Currency = "THB", PaymentMethod = "cash_manual", IdempotencyKey = "quote", Lines = Array.Empty<object>() });
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Client_priced_create_route_is_retired()
    {
        using var client = fixture.Order.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/order/v1/orders", new
        {
            OrganizationId = Guid.NewGuid(), BranchId = Guid.NewGuid(), Currency = "THB",
            Lines = new[] { new { ProductId = Guid.NewGuid(), Name = "Item", UnitPrice = 1, Quantity = 1, PreparationStation = "grill" } }
        });
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }
}
