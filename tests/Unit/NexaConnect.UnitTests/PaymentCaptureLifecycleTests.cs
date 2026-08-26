using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure;
using NexaConnect.Services.Payment.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NexaConnect.UnitTests;

public sealed class PaymentCaptureLifecycleTests
{
    [Fact]
    public void Capture_recovery_is_enabled_by_default_and_can_be_paused_explicitly()
    {
        var options = new PaymentProviderOptions();

        Assert.True(options.CaptureRecoveryEnabled);
        options.CaptureRecoveryEnabled = false;
        Assert.False(options.CaptureRecoveryEnabled);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Capture_recovery_worker_registration_honors_the_operational_switch(string? configured, bool expected)
    {
        var values = configured is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["PaymentProvider:CaptureRecoveryEnabled"] = configured };
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();

        services.AddPaymentCaptureRecoveryWorker(configuration);

        Assert.Equal(expected, services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(PaymentCaptureRecoveryWorker)));
    }

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

    [Fact]
    public async Task Unknown_capture_is_reconciled_from_provider_status_without_repeating_capture()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PaymentProviderOptions
            { LeaseDuration = TimeSpan.FromMilliseconds(1), MaximumCaptureRecoveryAttempts = 3 });
        var store = new InMemoryPaymentIntents(options); Guid organization = Guid.NewGuid();
        PaymentIntent authorized = CreateAuthorized(store, organization);
        var provider = new RecoveryProvider();
        var capture = new PaymentCaptureService(store, provider);
        PaymentIntent uncertain = (await capture.CaptureAsync(organization, authorized.Id, Context(), default))!;
        Assert.Equal("capture_unknown", uncertain.Status);

        PaymentAuthorizationLease claim = store.ClaimExpiredCapture(organization, authorized.Id, Context());
        Assert.True(claim.Acquired);
        var recovery = new PaymentCaptureRecoveryService(store, provider);
        PaymentIntent reconciled = await recovery.ReconcileAsync(organization, authorized.Id, Context(), default);

        Assert.Equal("captured", reconciled.Status);
        Assert.Equal("capture-recovered", reconciled.ProviderCaptureId);
        Assert.Equal(1, provider.CaptureCalls);
        Assert.Equal(1, provider.StatusCalls);
        Assert.NotNull(reconciled.CaptureLastReconciledAtUtc);
    }

    [Fact]
    public async Task Repeated_unknown_status_is_bounded_and_requires_operator_action()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PaymentProviderOptions
            { LeaseDuration = TimeSpan.FromMilliseconds(1), MaximumCaptureRecoveryAttempts = 2 });
        var store = new InMemoryPaymentIntents(options); Guid organization = Guid.NewGuid();
        PaymentIntent authorized = CreateAuthorized(store, organization);
        var provider = new AlwaysUnknownRecoveryProvider();
        await new PaymentCaptureService(store, provider).CaptureAsync(organization, authorized.Id, Context(), default);
        var recovery = new PaymentCaptureRecoveryService(store, provider);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Assert.True(store.ClaimExpiredCapture(organization, authorized.Id, Context()).Acquired);
            await recovery.ReconcileAsync(organization, authorized.Id, Context(), default);
        }
        PaymentIntent result = store.Get(organization, authorized.Id)!;
        Assert.Equal("requires_action", result.Status);
        Assert.Equal("capture_attempts_exhausted", result.FailureCode);
        Assert.Empty(store.FindExpiredCaptures());
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
    private sealed class RecoveryProvider : IPaymentProvider
    {
        public int CaptureCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { CaptureCalls++; return Task.FromResult(new ProviderCaptureResult(ProviderCaptureOutcome.Unknown, null, "provider_timeout")); }
        public Task<ProviderCaptureResult> GetCaptureStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { StatusCalls++; return Task.FromResult(new ProviderCaptureResult(ProviderCaptureOutcome.Captured, "capture-recovered", null)); }
    }
    private sealed class AlwaysUnknownRecoveryProvider : IPaymentProvider
    {
        public Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderCaptureResult(ProviderCaptureOutcome.Unknown, null, "provider_timeout"));
        public Task<ProviderCaptureResult> GetCaptureStatusAsync(PaymentIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderCaptureResult(ProviderCaptureOutcome.Unknown, null, "provider_capture_status_unknown"));
    }
}
