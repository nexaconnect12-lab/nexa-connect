using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosCheckoutIntegrationTests
{
    [Fact]
    public async Task Cash_session_conflict_surfaces_safe_problem_guidance()
    {
        const string guidance = "This shift already has a closed cash session. Close the shift and open a new shift before opening another cash session.";
        using var api = new PosApiClient(Configuration(), posHandler: new Handler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new ProblemDetails { Title = guidance, Status = 409 })
            })));

        PosApiException exception = await Assert.ThrowsAsync<PosApiException>(() => api.OpenCashSessionAsync(
            Token(), Guid.NewGuid(), Guid.NewGuid(), "THB", 100m));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(guidance, exception.Message);
    }

    internal static PosClientConfiguration Configuration() => new("http://localhost:8080/realms/nexa-dev", "nexaconnect-pos",
        "nexaconnect-pos://oauth/callback", "openid", "http://localhost:5225/", "http://localhost:5230/",
        Guid.NewGuid(), Guid.NewGuid(), "THB", "cash_manual", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "http://localhost:5268/");
    private static PosTokenSet Token() => new("synthetic", null, DateTimeOffset.UtcNow.AddMinutes(5), "Bearer");

    [Fact]
    public async Task Menu_uses_catalog_origin_with_authenticated_tenant_and_correlation()
    {
        var config = Configuration();
        var catalog = new Handler(request =>
        {
            Assert.Equal(5268, request.RequestUri!.Port);
            Assert.Equal($"/api/catalog/v1/branches/{config.BranchId:D}/menu-items", request.RequestUri.AbsolutePath);
            Assert.Equal(config.OrganizationId.ToString("D"), request.Headers.GetValues("X-Nexa-Organization-Id").Single());
            Assert.Equal("nexa_connect", request.Headers.GetValues("X-Nexa-Application-Code").Single());
            Assert.NotNull(request.Headers.Authorization);
            Assert.True(Guid.TryParse(request.Headers.GetValues("X-Correlation-ID").Single(), out _));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<PosMenuItem>()) });
        });
        using var api = new PosApiClient(config, catalogHandler: catalog);
        Assert.Empty(await api.GetMenuAsync(Token(), config.BranchId));
    }

    [Fact]
    public async Task Lost_placement_response_replays_identical_command_and_order_identity()
    {
        var config = Configuration();
        var checkout = PendingCheckout.Create(config, [new(Guid.NewGuid(), 2)]);
        string? first = null;
        int count = 0;
        var handler = new Handler(async request =>
        {
            string body = await request.Content!.ReadAsStringAsync();
            if (count++ == 0) { first = body; throw new HttpRequestException("Lost response"); }
            Assert.Equal(first, body);
            using var json = JsonDocument.Parse(body);
            Assert.Equal(checkout.OrderId.ToString("N"), json.RootElement.GetProperty("idempotencyKey").GetString());
            Assert.Equal(checkout.OrderId, json.RootElement.GetProperty("orderId").GetGuid());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PosOrderResult(checkout.OrderId, PosOrderStatus.KitchenAccepted, 240m, "THB")) };
        });
        using var api = new PosApiClient(config, orderHandler: handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => api.PlaceOrderAsync(Token(), checkout));
        Assert.Equal(checkout.OrderId, (await api.PlaceOrderAsync(Token(), checkout)).OrderId);
    }

    [Fact]
    public async Task Changed_terminal_scope_is_rejected_before_network()
    {
        var config = Configuration();
        var checkout = PendingCheckout.Create(config, [new(Guid.NewGuid(), 1)]);
        using var api = new PosApiClient(config with { TerminalId = Guid.NewGuid() }, orderHandler: new Handler(_ => throw new Exception("Must not send")));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.PlaceOrderAsync(Token(), checkout));
    }

    [Fact]
    public async Task Confirmed_terminal_rejection_is_returned_for_recovery_cleanup()
    {
        var config = Configuration();
        var checkout = PendingCheckout.Create(config, [new(Guid.NewGuid(), 1)]);
        using var api = new PosApiClient(config, orderHandler: new Handler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new PosOrderResult(checkout.OrderId, PosOrderStatus.Rejected, 95m, "THB"))
            })));

        PosOrderResult result = await api.PlaceOrderAsync(Token(), checkout);

        Assert.Equal(PosOrderStatus.Rejected, result.Status);
        Assert.Equal(checkout.OrderId, result.OrderId);
    }

    [Fact]
    public void Invalid_setup_is_rejected()
    {
        var config = Configuration();
        Assert.Throws<InvalidDataException>(() => (config with { Currency = "SGD" }).ValidateCheckout());
        Assert.Throws<InvalidDataException>(() => (config with { CatalogApi = "http://untrusted.example/" }).ValidateCheckout());
        Assert.Throws<InvalidDataException>(() => (config with { BranchId = Guid.Empty }).ValidateCheckout());
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
