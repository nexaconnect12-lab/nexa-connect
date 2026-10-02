using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Application.Refunds;

public sealed class PaymentRefundRecoveryService(IPaymentRefunds refunds, IPaymentProvider provider)
{
    public async Task<PaymentRefund> RecoverAsync(Guid organizationId, Guid refundId,
        PaymentMutationContext context, CancellationToken cancellationToken)
    {
        PaymentRefundLease lease = refunds.ClaimExpired(organizationId, refundId, context);
        if (!lease.Acquired) return lease.Refund;
        ProviderRefundResult result = await provider.GetRefundStatusAsync(lease.Intent, lease.Refund, cancellationToken);
        return refunds.Reconcile(organizationId, refundId, lease.Refund.ConcurrencyVersion, result.Outcome,
            result.ProviderTransactionId, result.FailureReason, context);
    }
}

public sealed class PaymentRefundRecoveryWorker(IServiceScopeFactory scopes, ILogger<PaymentRefundRecoveryWorker> logger,
    Microsoft.Extensions.Options.IOptions<PaymentProviderOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RecoveryInterval);
        do
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IPaymentRefunds>();
                var service = scope.ServiceProvider.GetRequiredService<PaymentRefundRecoveryService>();
                foreach (PaymentRefund refund in store.FindRecoverable())
                {
                    try { await service.RecoverAsync(refund.OrganizationId, refund.Id,
                        new PaymentMutationContext("payment-refund-recovery", Guid.NewGuid()), stoppingToken); }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    { logger.LogWarning(exception, "Refund recovery failed for refund {RefundId}.", refund.Id); }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { logger.LogError(exception, "Refund recovery scan failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
