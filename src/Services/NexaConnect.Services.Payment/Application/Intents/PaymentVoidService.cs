using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Application.Intents;

public sealed class PaymentVoidService(IPaymentIntents intents, IPaymentProvider provider) : IPaymentVoidService
{
    public async Task<PaymentIntent?> VoidAsync(Guid organizationId, Guid id, PaymentMutationContext context,
        CancellationToken cancellationToken)
    {
        PaymentAuthorizationLease lease = intents.BeginVoid(organizationId, id, context);
        if (!lease.Acquired) return lease.Intent;
        ProviderVoidResult result;
        try
        {
            result = await provider.VoidAsync(lease.Intent, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new(ProviderVoidOutcome.Unknown, null, "provider_timeout");
        }
        catch (HttpRequestException)
        {
            result = new(ProviderVoidOutcome.Unknown, null, "provider_unavailable");
        }
        return intents.CompleteVoid(organizationId, id, lease.Intent.ConcurrencyVersion, result.Outcome,
            result.ProviderTransactionId, result.FailureReason, context);
    }
}
