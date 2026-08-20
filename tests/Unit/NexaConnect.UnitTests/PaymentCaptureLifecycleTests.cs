using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.UnitTests;

public sealed class PaymentCaptureLifecycleTests
{
    [Fact]
    public async Task Authorized_intent_is_captured_once_and_replay_does_not_call_provider_twice()
    {
        var store = new InMemoryPaymentIntents(); Guid organization = Guid.NewGuid(); PaymentIntent authorized = CreateAuthorized(store, organization);
        var provider = new CaptureProvider(new(ProviderCaptureOutcome.Captured, "capture-1", null)); var service = new PaymentCaptureService(store, provider);
        PaymentIntent first = (await service.CaptureAsync(organization, authorized.Id, Context(), default))!;
        PaymentIntent replay = (await service.CaptureAsync(organization, authorized.Id, Context(), default))!;
        Assert.Equal("captured", first.Status); Assert.Equal("capture-1", first.ProviderCaptureId); Assert.Equal(first, replay); Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Transport_uncertainty_is_not_recorded_as_a_definitive_failure()
    {
        var store = new InMemoryPaymentIntents(); Guid organization = Guid.NewGuid(); PaymentIntent authorized = CreateAuthorized(store, organization);
        var service = new PaymentCaptureService(store, new CaptureProvider(new(ProviderCaptureOutcome.Unknown, null, "provider_timeout")));
        PaymentIntent result = (await service.CaptureAsync(organization, authorized.Id, Context(), default))!;
        Assert.Equal("capture_unknown", result.Status); Assert.Equal("provider_timeout", result.FailureCode);
    }

    [Fact]
    public async Task Pending_intent_cannot_be_captured()
    {
        var store = new InMemoryPaymentIntents(); Guid organization = Guid.NewGuid();
        PaymentIntent intent = store.Create(organization, new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"pending",10m,"USD","card"), Context());
        var service = new PaymentCaptureService(store, new CaptureProvider(new(ProviderCaptureOutcome.Captured,"capture",null)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(organization, intent.Id, Context(), default));
    }

    private static PaymentIntent CreateAuthorized(InMemoryPaymentIntents store, Guid organization)
    {
        PaymentIntent intent=store.Create(organization,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid().ToString("N"),10m,"USD","card"),Context());
        PaymentAuthorizationLease lease=store.BeginAuthorization(organization,intent.Id,Context());
        return store.CompleteAuthorization(organization,intent.Id,lease.Intent.ConcurrencyVersion,ProviderAuthorizationOutcome.Authorized,"auth-1",null,Context());
    }
    private static PaymentMutationContext Context()=>new("order-service",Guid.NewGuid());
    private sealed class CaptureProvider(ProviderCaptureResult result):IPaymentProvider
    {
        public int Calls{get;private set;} public Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent,CancellationToken cancellationToken){Calls++;return Task.FromResult(result);}
    }
}
