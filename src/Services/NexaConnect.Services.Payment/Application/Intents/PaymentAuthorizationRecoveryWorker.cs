using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Application.Intents;

public sealed class PaymentAuthorizationRecoveryWorker(
    IPaymentIntents intents,
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentProviderOptions> options,
    ILogger<PaymentAuthorizationRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = options.Value.RecoveryInterval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(30) : options.Value.RecoveryInterval;
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (PaymentIntent expired in intents.FindExpiredAuthorizations())
            {
                try
                {
                    PaymentAuthorizationLease claim = intents.ClaimExpiredAuthorization(expired.OrganizationId, expired.Id,
                        new PaymentMutationContext("payment-recovery-worker", Guid.NewGuid()));
                    if (!claim.Acquired) continue;
                    using IServiceScope scope = scopeFactory.CreateScope();
                    PaymentAuthorizationService authorization = scope.ServiceProvider.GetRequiredService<PaymentAuthorizationService>();
                    await authorization.ReconcileAsync(expired.OrganizationId, expired.Id,
                        new PaymentMutationContext("payment-recovery-worker", Guid.NewGuid()), stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Payment authorization recovery attempt failed for an opaque intent identifier");
                }
            }
        }
    }
}
