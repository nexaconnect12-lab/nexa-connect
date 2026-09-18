extern alias PAYMENT;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using PaymentIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentIntent;
using PaymentMutationContext = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentMutationContext;
using InMemoryPaymentIntents = PAYMENT::NexaConnect.Services.Payment.Infrastructure.InMemoryPaymentIntents;
using PaymentAuthorizationService = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentAuthorizationService;
using OmisePaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.OmisePaymentProvider;
using PaymentProviderOptions = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions;
using ProviderAuthorizationOutcome = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderAuthorizationOutcome;
using ProviderCaptureOutcome = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderCaptureOutcome;
using ProviderVoidOutcome = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderVoidOutcome;

namespace NexaConnect.IntegrationTests;

public sealed class OmisePaymentProviderLiveSandboxTests
{
    [OmiseLiveFact]
    public async Task Test_account_authorization_capture_reverse_and_status_preserve_charge_identity()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri("https://api.omise.co/"), Timeout = TimeSpan.FromSeconds(15) };
        var provider = new OmisePaymentProvider(client, Options.Create(new PaymentProviderOptions
        { OmiseSecretKey = Required("NEXACONNECT_OMISE_TEST_SECRET_KEY") }), NullLogger<OmisePaymentProvider>.Instance);
        var store = new InMemoryPaymentIntents(); var service = new PaymentAuthorizationService(store, provider);
        Guid organization = Guid.NewGuid();
        var context = new PaymentMutationContext("nexaconnect-order-service", Guid.NewGuid());
        decimal amount = decimal.Parse(Required("NEXACONNECT_OMISE_SANDBOX_AMOUNT"), System.Globalization.CultureInfo.InvariantCulture);
        var capture = store.Create(organization, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"), amount, "THB", "card"), context);
        var reversal = store.Create(organization, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"), amount, "THB", "card"), context);
        capture = (await service.AuthorizeAsync(organization, capture.Id, context, Required("NEXACONNECT_OMISE_CAPTURE_TEST_TOKEN"), default))!;
        Assert.True(capture.Status == "authorized", $"Capture fixture authorization was not confirmed. State={capture.Status}; category={capture.FailureCode ?? "none"}.");
        reversal = (await service.AuthorizeAsync(organization, reversal.Id, context, Required("NEXACONNECT_OMISE_VOID_TEST_TOKEN"), default))!;
        Assert.True(reversal.Status == "authorized", $"Void fixture authorization was not confirmed. State={reversal.Status}; category={reversal.FailureCode ?? "none"}.");
        PaymentIntent replay = (await service.AuthorizeAsync(organization, capture.Id, context, default))!;
        Assert.True(replay.ProviderAuthorizationId == capture.ProviderAuthorizationId, "Authorization replay changed identity.");
        var authorizationContext = await provider.InspectTestChargeAsync(capture.ProviderAuthorizationId!, amount, default);
        Assert.True(authorizationContext.ReadSucceeded && authorizationContext.TrustedFullCaptureContextVerified,
            "Omise did not retain verifiable full-capture creation context. No capture attempted.");

        // Deliberately remove the local reference to exercise lost-response lookup.
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        bool recovered = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await provider.GetAuthorizationStatusAsync(reversal with { ProviderAuthorizationId = null }, default);
            if (status.Outcome == ProviderAuthorizationOutcome.Authorized)
            { recovered = status.ProviderTransactionId == reversal.ProviderAuthorizationId; break; }
            await Task.Delay(2000);
        }
        Assert.True(recovered, "Metadata search did not recover the original authorization.");
        var captured = await provider.CaptureAsync(capture, default);
        var captureReplay = await provider.CaptureAsync(capture, default);
        var captureStatus = await provider.GetCaptureStatusAsync(capture, default);
        Assert.True(captured.Outcome == ProviderCaptureOutcome.Captured && captureReplay.Outcome == ProviderCaptureOutcome.Captured
            && captureStatus.Outcome == ProviderCaptureOutcome.Captured,
            $"Capture/replay/status was not confirmed. Capture={captured.Outcome}/{captured.FailureReason ?? "none"}; replay={captureReplay.Outcome}/{captureReplay.FailureReason ?? "none"}; status={captureStatus.Outcome}/{captureStatus.FailureReason ?? "none"}.");
        Assert.True(captured.ProviderTransactionId == capture.ProviderAuthorizationId
            && captured.ProviderTransactionId == captureReplay.ProviderTransactionId
            && captured.ProviderTransactionId == captureStatus.ProviderTransactionId, "Capture identity changed.");
        var voided = await provider.VoidAsync(reversal, default);
        var voidReplay = await provider.VoidAsync(reversal, default);
        var voidStatus = await provider.GetVoidStatusAsync(reversal, default);
        Assert.True(voided.Outcome == ProviderVoidOutcome.Voided && voidReplay.Outcome == ProviderVoidOutcome.Voided
            && voidStatus.Outcome == ProviderVoidOutcome.Voided, "Reverse/replay/status was not confirmed.");
        Assert.True(voided.ProviderTransactionId == reversal.ProviderAuthorizationId
            && voided.ProviderTransactionId == voidReplay.ProviderTransactionId
            && voided.ProviderTransactionId == voidStatus.ProviderTransactionId, "Reverse identity changed.");
        Assert.True((await provider.VoidAsync(capture, default)).Outcome == ProviderVoidOutcome.Failed, "Captured charge must require a refund.");
        await File.WriteAllTextAsync(Required("NEXACONNECT_OMISE_SANDBOX_EVIDENCE"), JsonSerializer.Serialize(new
        {
            completedAtUtc = DateTimeOffset.UtcNow, provider = "Omise", testMode = true, passed = true,
            authorizationReplayVerified = true, lostAuthorizationReferenceLookupVerified = true,
            captureReplayAndStatusVerified = true, reversalReplayAndStatusVerified = true,
            capturedReversalRejected = true, hostedProcessInterruptionVerified = false,
            trustedFullCaptureCreationContextVerified = true,
            credentialsOrTokensRetained = false, providerReferencesRetained = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing setting: " + name);
}

public sealed class OmiseLiveFactAttribute : FactAttribute
{
    public OmiseLiveFactAttribute()
    {
        string[] required = ["NEXACONNECT_OMISE_TEST_SECRET_KEY", "NEXACONNECT_OMISE_CAPTURE_TEST_TOKEN",
            "NEXACONNECT_OMISE_VOID_TEST_TOKEN", "NEXACONNECT_OMISE_SANDBOX_AMOUNT", "NEXACONNECT_OMISE_SANDBOX_EVIDENCE"];
        if (Environment.GetEnvironmentVariable("NEXACONNECT_OMISE_SANDBOX_ACCEPTANCE") != "1"
            || Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") != "Testing"
            || required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Requires the guarded Omise test-account runner and two fresh disposable test tokens.";
    }
}
