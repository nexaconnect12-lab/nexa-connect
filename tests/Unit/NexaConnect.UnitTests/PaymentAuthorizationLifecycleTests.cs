using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.UnitTests;

public sealed class PaymentAuthorizationLifecycleTests
{
    [Fact]
    public async Task Authorization_moves_pending_intent_to_authorized_and_replay_does_not_call_provider_twice()
    {
        var intents = new InMemoryPaymentIntents();
        var provider = new RecordingProvider(new(true, "provider-auth-1", null));
        var service = new PaymentAuthorizationService(intents, provider);
        Guid organizationId = Guid.NewGuid();
        PaymentIntent intent = intents.Create(organizationId,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "authorize-1", 25m, "USD", "card"), Context());

        PaymentIntent? first = await service.AuthorizeAsync(organizationId, intent.Id, Context(), default);
        PaymentIntent? replay = await service.AuthorizeAsync(organizationId, intent.Id, Context(), default);

        Assert.Equal("authorized", first!.Status);
        Assert.Equal("provider-auth-1", first.ProviderAuthorizationId);
        Assert.Equal(first, replay);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Decline_records_only_a_bounded_safe_failure_code()
    {
        var intents = new InMemoryPaymentIntents();
        var provider = new RecordingProvider(new(false, null, "Card declined: PAN 4111 1111 1111 1111"));
        var service = new PaymentAuthorizationService(intents, provider);
        Guid organizationId = Guid.NewGuid();
        PaymentIntent intent = intents.Create(organizationId,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "authorize-2", 25m, "USD", "card"), Context());

        PaymentIntent result = (await service.AuthorizeAsync(organizationId, intent.Id, Context(), default))!;

        Assert.Equal("failed", result.Status);
        Assert.Equal("provider_declined", result.FailureCode);
    }

    [Fact]
    public void Concurrent_authorization_cannot_acquire_two_leases()
    {
        var intents = new InMemoryPaymentIntents();
        Guid organizationId = Guid.NewGuid();
        PaymentIntent intent = intents.Create(organizationId,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "authorize-3", 25m, "USD", "card"), Context());

        PaymentAuthorizationLease first = intents.BeginAuthorization(organizationId, intent.Id, Context());
        PaymentAuthorizationLease second = intents.BeginAuthorization(organizationId, intent.Id, Context());

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
        Assert.Equal("authorizing", second.Intent.Status);
    }

    private static PaymentMutationContext Context() => new("order-service", Guid.NewGuid());

    private sealed class RecordingProvider(ProviderAuthorizationResult result) : IPaymentProvider
    {
        public int Calls { get; private set; }
        public Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
