using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.UnitTests;

public sealed class PaymentProviderRecoveryTests
{
    [Fact]
    public async Task Disabled_provider_fails_commands_without_creating_financial_uncertainty()
    {
        var provider = new DisabledPaymentProvider();
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = "authorization-ref-1" };

        ProviderAuthorizationResult authorization = await provider.AuthorizeAsync(intent, CancellationToken.None);
        ProviderCaptureResult capture = await provider.CaptureAsync(intent, CancellationToken.None);
        ProviderVoidResult voidResult = await provider.VoidAsync(intent, CancellationToken.None);

        Assert.Equal(ProviderAuthorizationOutcome.Failed, authorization.EffectiveOutcome);
        Assert.Equal(ProviderCaptureOutcome.Failed, capture.Outcome);
        Assert.Equal(ProviderVoidOutcome.Failed, voidResult.Outcome);
        Assert.All(new[] { authorization.FailureReason, capture.FailureReason, voidResult.FailureReason },
            reason => Assert.Equal("provider_not_configured", reason));
    }

    [Fact]
    public async Task Disabled_provider_status_lookups_never_invent_terminal_provider_state()
    {
        var provider = new DisabledPaymentProvider();
        PaymentIntent intent = Intent();

        Assert.Equal(ProviderAuthorizationOutcome.Unknown,
            (await provider.GetAuthorizationStatusAsync(intent, CancellationToken.None)).Outcome);
        Assert.Equal(ProviderCaptureOutcome.Unknown,
            (await provider.GetCaptureStatusAsync(intent, CancellationToken.None)).Outcome);
        Assert.Equal(ProviderVoidOutcome.Unknown,
            (await provider.GetVoidStatusAsync(intent, CancellationToken.None)).Outcome);
    }

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

    [Fact]
    public async Task Capture_status_without_provider_reference_remains_unknown()
    {
        var provider = CreateProvider(HttpStatusCode.OK, new { status = "captured" });

        ProviderCaptureResult result = await provider.GetCaptureStatusAsync(Intent(), CancellationToken.None);

        Assert.Equal(ProviderCaptureOutcome.Unknown, result.Outcome);
        Assert.Null(result.ProviderTransactionId);
        Assert.Equal("provider_capture_status_unknown", result.FailureReason);
    }

    [Fact]
    public async Task Void_uses_operation_specific_idempotency_key()
    {
        var handler = new StubHandler(HttpStatusCode.OK, new { succeeded = true, providerTransactionId = "void-ref-1" });
        var provider = new HttpPaymentProvider(new HttpClient(handler) { BaseAddress = new Uri("https://provider.test/") }, Options.Create(new PaymentProviderOptions()));
        PaymentIntent intent = Intent() with { Status = "voiding", ProviderAuthorizationId = "authorization-ref-1" };
        ProviderVoidResult result = await provider.VoidAsync(intent, CancellationToken.None);
        Assert.Equal(ProviderVoidOutcome.Voided, result.Outcome);
        Assert.Equal($"void:{intent.Id:D}", handler.IdempotencyKey);
    }

    [Fact]
    public async Task Void_status_without_provider_reference_remains_unknown()
    {
        var provider = CreateProvider(HttpStatusCode.OK, new { status = "voided" });
        ProviderVoidResult result = await provider.GetVoidStatusAsync(Intent(), CancellationToken.None);
        Assert.Equal(ProviderVoidOutcome.Unknown, result.Outcome);
        Assert.Equal("provider_void_status_unknown", result.FailureReason);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Capture_transient_http_failures_require_reconciliation(HttpStatusCode statusCode)
    {
        var provider = CreateProvider(statusCode, null);
        ProviderCaptureResult result = await provider.CaptureAsync(Intent() with
        {
            Status = "capturing", ProviderAuthorizationId = "authorization-ref-1"
        }, CancellationToken.None);

        Assert.Equal(ProviderCaptureOutcome.Unknown, result.Outcome);
        Assert.Equal($"provider_http_{(int)statusCode}", result.FailureReason);
    }

    [Fact]
    public async Task Malformed_success_response_requires_reconciliation()
    {
        var client = new HttpClient(new RawHandler(HttpStatusCode.OK, "not-json"))
            { BaseAddress = new Uri("https://provider.test/") };
        var provider = new HttpPaymentProvider(client, Options.Create(new PaymentProviderOptions()));

        ProviderCaptureResult result = await provider.CaptureAsync(Intent() with
        {
            Status = "capturing", ProviderAuthorizationId = "authorization-ref-1"
        }, CancellationToken.None);

        Assert.Equal(ProviderCaptureOutcome.Unknown, result.Outcome);
        Assert.Equal("provider_response_invalid", result.FailureReason);
    }

    [Fact]
    public async Task Provider_timeout_requires_reconciliation()
    {
        var client = new HttpClient(new TimeoutHandler()) { BaseAddress = new Uri("https://provider.test/") };
        var provider = new HttpPaymentProvider(client, Options.Create(new PaymentProviderOptions()));

        ProviderVoidResult result = await provider.VoidAsync(Intent() with
        {
            Status = "voiding", ProviderAuthorizationId = "authorization-ref-1"
        }, CancellationToken.None);

        Assert.Equal(ProviderVoidOutcome.Unknown, result.Outcome);
        Assert.Equal("provider_timeout", result.FailureReason);
    }

    [Fact]
    public async Task Provider_credential_is_injected_without_changing_idempotency_key()
    {
        var handler = new StubHandler(HttpStatusCode.OK, new { succeeded = true, providerTransactionId = "capture-ref-1" });
        var provider = new HttpPaymentProvider(new HttpClient(handler) { BaseAddress = new Uri("https://provider.test/") },
            Options.Create(new PaymentProviderOptions { ApiKey = "synthetic-secret" }));
        PaymentIntent intent = Intent() with { Status = "capturing", ProviderAuthorizationId = "authorization-ref-1" };

        ProviderCaptureResult result = await provider.CaptureAsync(intent, CancellationToken.None);

        Assert.Equal(ProviderCaptureOutcome.Captured, result.Outcome);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("synthetic-secret", handler.AuthorizationParameter);
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
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? values)
                ? values.Single()
                : null;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            var response = new HttpResponseMessage(statusCode);
            if (body is not null) response.Content = JsonContent.Create(body);
            await Task.Yield();
            return response;
        }
    }

    private sealed class RawHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("synthetic timeout"));
    }
}
