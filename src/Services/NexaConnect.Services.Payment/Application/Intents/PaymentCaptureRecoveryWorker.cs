using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Infrastructure.Providers;
using Npgsql;
using System.Diagnostics.Metrics;

namespace NexaConnect.Services.Payment.Application.Intents;

public sealed class PaymentCaptureRecoveryWorker(
    IPaymentIntents intents,
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentProviderOptions> options,
    ILogger<PaymentCaptureRecoveryWorker> logger) : BackgroundService
{
    private static readonly Meter Meter = new("nexaconnect-payment");
    private static readonly Counter<long> Claims = Meter.CreateCounter<long>("payment.capture_recovery.claims");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("payment.capture_recovery.outcomes");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("payment.capture_recovery.failures");

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
                    Claims.Add(1);
                    using IServiceScope scope = scopeFactory.CreateScope();
                    PaymentCaptureRecoveryService service = scope.ServiceProvider.GetRequiredService<PaymentCaptureRecoveryService>();
                    PaymentIntent reconciled = await service.ReconcileAsync(candidate.OrganizationId, candidate.Id, context, stoppingToken);
                    Outcomes.Add(1, new KeyValuePair<string, object?>("payment.capture.status", reconciled.Status));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Failures.Add(1, new KeyValuePair<string, object?>("error.type", ClassifyFailure(exception)));
                    logger.LogWarning(exception, "Payment capture recovery attempt failed for an opaque intent identifier");
                }
            }
        }
    }

    private static string ClassifyFailure(Exception exception) => exception switch
    {
        PaymentConcurrencyException => "concurrency",
        KeyNotFoundException => "not_found",
        TimeoutException => "timeout",
        HttpRequestException => "provider_transport",
        NpgsqlException => "persistence",
        ArgumentException => "invalid_data",
        InvalidOperationException => "invalid_state",
        _ => "unexpected"
    };
}
