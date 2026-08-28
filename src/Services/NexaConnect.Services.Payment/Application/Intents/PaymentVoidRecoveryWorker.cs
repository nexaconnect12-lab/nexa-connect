using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Application.Intents;

public sealed class PaymentVoidRecoveryWorker(IPaymentIntents intents, IServiceScopeFactory scopes,
    IOptions<PaymentProviderOptions> options, ILogger<PaymentVoidRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = options.Value.RecoveryInterval > TimeSpan.Zero ? options.Value.RecoveryInterval : TimeSpan.FromSeconds(30);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (PaymentIntent candidate in intents.FindExpiredVoids())
            {
                try
                {
                    var context = new PaymentMutationContext("payment-void-recovery-worker", Guid.NewGuid());
                    PaymentAuthorizationLease claim = intents.ClaimExpiredVoid(candidate.OrganizationId, candidate.Id, context);
                    if (!claim.Acquired) continue;
                    using IServiceScope scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<PaymentVoidRecoveryService>()
                        .ReconcileAsync(candidate.OrganizationId, candidate.Id, context, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Payment void recovery attempt failed for an opaque intent identifier");
                }
            }
        }
    }
}
