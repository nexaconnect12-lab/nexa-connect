using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Application.Intents;

public sealed class PaymentCaptureRecoveryWorker(
    IPaymentIntents intents,
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentProviderOptions> options,
    ILogger<PaymentCaptureRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = options.Value.RecoveryInterval > TimeSpan.Zero
            ? options.Value.RecoveryInterval : TimeSpan.FromSeconds(30);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (PaymentIntent candidate in intents.FindExpiredCaptures())
            {
                try
                {
                    var context = new PaymentMutationContext("payment-capture-recovery-worker", Guid.NewGuid());
                    PaymentAuthorizationLease claim = intents.ClaimExpiredCapture(candidate.OrganizationId, candidate.Id, context);
                    if (!claim.Acquired) continue;
                    using IServiceScope scope = scopeFactory.CreateScope();
                    PaymentCaptureRecoveryService service = scope.ServiceProvider.GetRequiredService<PaymentCaptureRecoveryService>();
                    await service.ReconcileAsync(candidate.OrganizationId, candidate.Id, context, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Payment capture recovery attempt failed for an opaque intent identifier");
                }
            }
        }
    }
}
