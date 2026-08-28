using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.UnitTests;

public sealed class PaymentVoidLifecycleTests
{
    [Fact]
    public async Task Authorized_void_is_idempotent_and_captured_payment_is_rejected()
    {
        Guid organization = Guid.NewGuid(); var store = new InMemoryPaymentIntents();
        PaymentIntent intent = store.Create(organization, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid().ToString("N"), 12.50m, "USD", "card"), Context());
        PaymentAuthorizationLease authorization = store.BeginAuthorization(organization, intent.Id, Context());
        PaymentIntent authorized = store.CompleteAuthorization(organization, intent.Id, authorization.Intent.ConcurrencyVersion,
            ProviderAuthorizationOutcome.Authorized, "auth-123", null, Context());
        var provider = new VoidProvider(new(ProviderVoidOutcome.Voided, "void-123", null));
        var service = new PaymentVoidService(store, provider);

        PaymentIntent first = (await service.VoidAsync(organization, authorized.Id, Context(), default))!;
        PaymentIntent replay = (await service.VoidAsync(organization, authorized.Id, Context(), default))!;

        Assert.Equal("voided", first.Status); Assert.Equal("void-123", first.ProviderVoidId);
        Assert.Equal(first, replay); Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(ProviderVoidOutcome.Failed, "void_failed")]
    [InlineData(ProviderVoidOutcome.Unknown, "void_unknown")]
    public async Task Non_success_outcome_is_not_reported_as_voided(ProviderVoidOutcome outcome, string expected)
    {
        Guid organization = Guid.NewGuid(); var store = new InMemoryPaymentIntents();
        PaymentIntent intent = store.Create(organization, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid().ToString("N"), 10m, "USD", "card"), Context());
        PaymentAuthorizationLease authorization = store.BeginAuthorization(organization, intent.Id, Context());
        PaymentIntent authorized = store.CompleteAuthorization(organization, intent.Id, authorization.Intent.ConcurrencyVersion,
            ProviderAuthorizationOutcome.Authorized, "auth-123", null, Context());
        PaymentIntent result = (await new PaymentVoidService(store, new VoidProvider(new(outcome, null, "provider_result")))
            .VoidAsync(organization, authorized.Id, Context(), default))!;
        Assert.Equal(expected, result.Status); Assert.Null(result.ProviderVoidId);
    }

    [Fact]
    public async Task Captured_payment_is_refund_only()
    {
        Guid organization = Guid.NewGuid(); var store = new InMemoryPaymentIntents();
        PaymentIntent intent = store.Create(organization, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"), 10m, "USD", "card"), Context());
        PaymentAuthorizationLease authorization = store.BeginAuthorization(organization, intent.Id, Context());
        PaymentIntent authorized = store.CompleteAuthorization(organization, intent.Id, authorization.Intent.ConcurrencyVersion, ProviderAuthorizationOutcome.Authorized, "auth-123", null, Context());
        PaymentAuthorizationLease capture = store.BeginCapture(organization, authorized.Id, Context());
        store.CompleteCapture(organization, authorized.Id, capture.Intent.ConcurrencyVersion, ProviderCaptureOutcome.Captured, "capture-123", null, Context());
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PaymentVoidService(store,
            new VoidProvider(new(ProviderVoidOutcome.Voided, "void-123", null))).VoidAsync(organization, authorized.Id, Context(), default));
        Assert.Contains("refund", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PaymentMutationContext Context() => new("test-actor", Guid.NewGuid());
    private sealed class VoidProvider(ProviderVoidResult result) : IPaymentProvider
    {
        public int Calls { get; private set; }
        public Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProviderVoidResult> VoidAsync(PaymentIntent intent, CancellationToken cancellationToken) { Calls++; return Task.FromResult(result); }
    }
}
