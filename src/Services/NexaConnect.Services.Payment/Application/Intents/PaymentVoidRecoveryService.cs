using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Application.Intents;

public sealed class PaymentVoidRecoveryService(IPaymentIntents intents, IPaymentProvider provider)
{
    public async Task<PaymentIntent> ReconcileAsync(Guid organizationId, Guid id, PaymentMutationContext context, CancellationToken token)
    {
        PaymentIntent intent = intents.Get(organizationId, id) ?? throw new KeyNotFoundException("Payment intent was not found.");
        ProviderVoidResult status;
        try { status = await provider.GetVoidStatusAsync(intent, token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { status = new(ProviderVoidOutcome.Unknown, null, "provider_timeout"); }
        catch (HttpRequestException) { status = new(ProviderVoidOutcome.Unknown, null, "provider_unavailable"); }
        return intents.ReconcileVoid(organizationId, id, intent.ConcurrencyVersion, status.Outcome,
            status.ProviderTransactionId, status.FailureReason, context);
    }
}
