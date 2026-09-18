extern alias PAYMENT;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentProgram = PAYMENT::PaymentProgram;
using PaymentIntents = PAYMENT::NexaConnect.Services.Payment.Application.Intents.IPaymentIntents;
using PaymentMutationContext = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentMutationContext;
using IPaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.IPaymentProvider;
using OmisePaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.OmisePaymentProvider;
using PaymentProviderOptions = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions;

namespace NexaConnect.IntegrationTests;

public sealed class OmisePaymentAuthorizationHttpTests
{
    [Fact]
    public async Task Ephemeral_authorize_body_preserves_workload_tenant_and_replay_boundaries()
    {
        await using var factory = new OmiseFactory(); using HttpClient client = factory.CreateClient();
        Guid organization = Guid.NewGuid();
        PaymentIntents store = factory.Services.GetRequiredService<PaymentIntents>();
        var intent = store.Create(organization, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "omise-http", 50m, "THB", "card"),
            new PaymentMutationContext("nexaconnect-order-service", Guid.NewGuid()));
        string route = $"/api/payment/v1/intents/{intent.Id:D}/authorize";
        client.DefaultRequestHeaders.Add("X-Nexa-Organization-Id", organization.ToString("D"));
        using var missing = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("pending", store.Get(organization, intent.Id)!.Status);
        using var invalid = await client.PostAsJsonAsync(route, new { cardToken = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "customer-user");
        using var denied = await client.PostAsJsonAsync(route, new { cardToken = "tokn_test_abcdefghijklmnopqrstuvwxyz" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "integration-test-token");
        client.DefaultRequestHeaders.Remove("X-Nexa-Organization-Id");
        client.DefaultRequestHeaders.Add("X-Nexa-Organization-Id", Guid.NewGuid().ToString("D"));
        using var crossTenant = await client.PostAsJsonAsync(route, new { cardToken = "tokn_test_abcdefghijklmnopqrstuvwxyz" });
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
        Assert.Equal(0, factory.Handler.Commands);
        client.DefaultRequestHeaders.Remove("X-Nexa-Organization-Id");
        client.DefaultRequestHeaders.Add("X-Nexa-Organization-Id", organization.ToString("D"));
        using var authorized = await client.PostAsJsonAsync(route, new { cardToken = "tokn_test_abcdefghijklmnopqrstuvwxyz" });
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Assert.Equal("authorized", store.Get(organization, intent.Id)!.Status);
        Assert.DoesNotContain("tokn_test_", await authorized.Content.ReadAsStringAsync());
        using var replay = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(1, factory.Handler.Commands);
    }

    private sealed class OmiseFactory : WebApplicationFactory<PaymentProgram>
    {
        public ChargeHandler Handler { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestServiceConfiguration.Configure(builder, "payment", new Dictionary<string, string?>
            { ["PaymentProvider:Adapter"] = "Omise", ["PaymentProvider:OmiseSecretKey"] = "skey_test_abcdefghijklmnopqrstuvwxyz" });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPaymentProvider>();
                services.AddSingleton<IPaymentProvider>(new OmisePaymentProvider(new HttpClient(Handler)
                    { BaseAddress = new Uri("https://api.omise.co/") }, Options.Create(new PaymentProviderOptions
                    { OmiseSecretKey = "skey_test_abcdefghijklmnopqrstuvwxyz" }), NullLogger<OmisePaymentProvider>.Instance));
            });
        }
    }
    private sealed class ChargeHandler : HttpMessageHandler
    {
        public int Commands;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Commands);
            string form = await request.Content!.ReadAsStringAsync(cancellationToken);
            Dictionary<string, string> fields = form.Split('&').Select(part => part.Split('=', 2))
                .ToDictionary(parts => WebUtility.UrlDecode(parts[0]), parts => WebUtility.UrlDecode(parts[1]));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                @object = "charge", id = "chrg_test_abcdefghijklmnopqrstuvwxyz", livemode = false,
                amount = long.Parse(fields["amount"]), currency = "thb", status = "pending", authorized = true,
                paid = false, reversed = false, capture = false, capturable = true, reversible = true,
                refunded_amount = 0, captured_amount = 0,
                metadata = new Dictionary<string, string>
                {
                    ["nexa_intent_id"] = fields["metadata[nexa_intent_id]"],
                    ["nexa_organization_id"] = fields["metadata[nexa_organization_id]"],
                    ["nexa_order_id"] = fields["metadata[nexa_order_id]"]
                }
            }) };
        }
    }
}
