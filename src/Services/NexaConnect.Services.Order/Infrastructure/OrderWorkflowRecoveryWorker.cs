using Microsoft.Extensions.Options;
using NexaConnect.Services.Order.Application.Workflow;
using System.Diagnostics.Metrics;

namespace NexaConnect.Services.Order.Infrastructure;

public sealed class OrderWorkflowRecoveryOptions
{
    public bool Enabled { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(15);
}

public sealed class OrderWorkflowRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OrderWorkflowRecoveryOptions> options,
    ILogger<OrderWorkflowRecoveryWorker> logger) : BackgroundService
{
    private static readonly Meter Meter = new("nexaconnect-order");
    private static readonly Counter<long> RecoveredSteps = Meter.CreateCounter<long>("order.workflow_recovery.steps");
    private static readonly Counter<long> FailedAttempts = Meter.CreateCounter<long>("order.workflow_recovery.failures");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OrderWorkflowRecoveryOptions settings = options.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<OrderWorkflowRecoveryService>();
                bool recovered = await service.RecoverNextAsync(settings.LeaseDuration, settings.RetryDelay, stoppingToken);
                if (recovered) { RecoveredSteps.Add(1); continue; }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                FailedAttempts.Add(1);
                logger.LogWarning(exception, "Order workflow recovery attempt failed; the durable item remains retryable.");
            }
            await Task.Delay(settings.PollInterval, stoppingToken);
        }
    }
}
