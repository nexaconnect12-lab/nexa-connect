using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.UnitTests;

public sealed class PaymentProviderRecoveryTests
{
    [Fact]
    public async Task Server_error_is_unknown_not_declined()
    {
        var provider = CreateProvider(HttpStatusCode.GatewayTimeout, null);
        ProviderAuthorizationResult result = await provider.AuthorizeAsync(Intent(), CancellationToken.None);

        Assert.Equal(ProviderAuthorizationOutcome.Unknown, result.EffectiveOutcome);
        Assert.Equal("provider_http_504", result.FailureReason);
    }

    [Theory]
    [InlineData("authorized", ProviderAuthorizationOutcome.Authorized)]
    [InlineData("declined", ProviderAuthorizationOutcome.Declined)]
    [InlineData("failed", ProviderAuthorizationOutcome.Failed)]
    [InlineData("pending", ProviderAuthorizationOutcome.Unknown)]
    public async Task Status_lookup_maps_provider_outcomes_without_persisting_payload(string status, ProviderAuthorizationOutcome expected)
    {
        var provider = CreateProvider(HttpStatusCode.OK, new { status, providerTransactionId = "provider-ref-1" });

        ProviderAuthorizationStatus result = await provider.GetAuthorizationStatusAsync(Intent(), CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal("provider-ref-1", result.ProviderTransactionId);
    }

    [Fact]
    public async Task Missing_provider_authorization_is_unknown()
    {
        var provider = CreateProvider(HttpStatusCode.NotFound, null);

        ProviderAuthorizationStatus result = await provider.GetAuthorizationStatusAsync(Intent(), CancellationToken.None);

        Assert.Equal(ProviderAuthorizationOutcome.Unknown, result.Outcome);
        Assert.Equal("provider_status_missing", result.FailureReason);
    }

    [Fact]
    public async Task Capture_uses_payment_intent_as_provider_idempotency_key()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            new { succeeded = true, providerTransactionId = "capture-ref-1" });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://provider.test/") };
        var provider = new HttpPaymentProvider(client, Options.Create(new PaymentProviderOptions()));
        PaymentIntent intent = Intent() with { Status = "capturing", ProviderAuthorizationId = "authorization-ref-1" };

        ProviderCaptureResult result = await provider.CaptureAsync(intent, CancellationToken.None);

        Assert.Equal(ProviderCaptureOutcome.Captured, result.Outcome);
        Assert.Equal(intent.Id.ToString("D"), handler.IdempotencyKey);
    }

    private static HttpPaymentProvider CreateProvider(HttpStatusCode statusCode, object? body)
    {
        var handler = new StubHandler(statusCode, body);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://provider.test/") };
        return new HttpPaymentProvider(client, Options.Create(new PaymentProviderOptions()));
    }

    private static PaymentIntent Intent() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        10m, "USD", "card", "authorizing", DateTimeOffset.UtcNow);

    private sealed class StubHandler(HttpStatusCode statusCode, object? body) : HttpMessageHandler
    {
        public string? IdempotencyKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? values)
                ? values.Single()
                : null;
            var response = new HttpResponseMessage(statusCode);
            if (body is not null) response.Content = JsonContent.Create(body);
            await Task.Yield();
            return response;
        }
    }
}
