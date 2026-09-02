extern alias PAYMENT;

using Microsoft.Extensions.Options;
using PaymentIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentIntent;
using HttpPaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.HttpPaymentProvider;
using PaymentProviderOptions = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions;
using ProviderCaptureOutcome = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderCaptureOutcome;
using ProviderVoidOutcome = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderVoidOutcome;

namespace NexaConnect.IntegrationTests;

public sealed class PaymentProviderLiveSandboxTests
{
    [PaymentProviderLiveSandboxFact]
    public async Task Capture_and_void_accept_configured_credential_and_are_idempotent_over_live_https()
    {
        string baseUrl = Required("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL");
        string apiKey = Required("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY");
        string captureAuthorization = Required("NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_AUTHORIZATION_ID");
        string voidAuthorization = Required("NEXACONNECT_PAYMENT_PROVIDER_VOID_AUTHORIZATION_ID");
        Assert.False(string.Equals(captureAuthorization, voidAuthorization, StringComparison.Ordinal));
        decimal amount = decimal.Parse(Required("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT"),
            System.Globalization.CultureInfo.InvariantCulture);
        string currency = Required("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY").Trim().ToUpperInvariant();
        var options = Options.Create(new PaymentProviderOptions
        {
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            CapturePath = Optional("NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_PATH", "v1/captures"),
            CaptureStatusPath = Optional("NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_STATUS_PATH", "v1/captures"),
            VoidPath = Optional("NEXACONNECT_PAYMENT_PROVIDER_VOID_PATH", "v1/voids"),
            VoidStatusPath = Optional("NEXACONNECT_PAYMENT_PROVIDER_VOID_STATUS_PATH", "v1/voids"),
            RequestTimeout = TimeSpan.FromSeconds(30)
        });
        using var client = new HttpClient { BaseAddress = new Uri(options.Value.BaseUrl), Timeout = options.Value.RequestTimeout };
        var provider = new HttpPaymentProvider(client, options);
        PaymentIntent captureIntent = Intent(amount, currency, captureAuthorization, "capturing");
        PaymentIntent voidIntent = Intent(amount, currency, voidAuthorization, "voiding");

        var firstCapture = await provider.CaptureAsync(captureIntent, CancellationToken.None);
        var replayedCapture = await provider.CaptureAsync(captureIntent, CancellationToken.None);
        var captureStatus = await provider.GetCaptureStatusAsync(captureIntent, CancellationToken.None);
        Assert.Equal(ProviderCaptureOutcome.Captured, firstCapture.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(firstCapture.ProviderTransactionId));
        Assert.True(SameOpaqueReference(firstCapture.ProviderTransactionId, replayedCapture.ProviderTransactionId));
        Assert.True(SameOpaqueReference(firstCapture.ProviderTransactionId, captureStatus.ProviderTransactionId));

        var firstVoid = await provider.VoidAsync(voidIntent, CancellationToken.None);
        var replayedVoid = await provider.VoidAsync(voidIntent, CancellationToken.None);
        var voidStatus = await provider.GetVoidStatusAsync(voidIntent, CancellationToken.None);
        Assert.Equal(ProviderVoidOutcome.Voided, firstVoid.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(firstVoid.ProviderTransactionId));
        Assert.True(SameOpaqueReference(firstVoid.ProviderTransactionId, replayedVoid.ProviderTransactionId));
        Assert.True(SameOpaqueReference(firstVoid.ProviderTransactionId, voidStatus.ProviderTransactionId));
    }

    private static PaymentIntent Intent(decimal amount, string currency, string authorizationId, string status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, currency, "card", status,
            DateTimeOffset.UtcNow, ProviderAuthorizationId: authorizationId);

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Missing live provider acceptance setting: {name}.");

    private static string Optional(string name, string fallback) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? fallback : Environment.GetEnvironmentVariable(name)!;

    private static bool SameOpaqueReference(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && string.Equals(left, right, StringComparison.Ordinal);
}

public sealed class PaymentProviderLiveSandboxFactAttribute : FactAttribute
{
    public PaymentProviderLiveSandboxFactAttribute()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        string? url = Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL");
        string[] required =
        [
            "NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY",
            "NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_AUTHORIZATION_ID",
            "NEXACONNECT_PAYMENT_PROVIDER_VOID_AUTHORIZATION_ID",
            "NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT",
            "NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY"
        ];
        bool safeUrl = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && !uri.Host.Contains("prod", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.Contains("production", StringComparison.OrdinalIgnoreCase);
        if (Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROVIDER_LIVE_ACCEPTANCE") != "1"
            || environment is not ("Development" or "Test" or "Testing")
            || !safeUrl
            || string.Equals(Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_AUTHORIZATION_ID"),
                Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROVIDER_VOID_AUTHORIZATION_ID"), StringComparison.Ordinal)
            || required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Live payment-provider acceptance requires explicit opt-in, safe environment, non-production HTTPS URL, credential, amount/currency, and disposable capture/void authorization references.";
    }
}
