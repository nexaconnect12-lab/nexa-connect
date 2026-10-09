using System.Diagnostics;
using System.Diagnostics.Metrics;
using NexaConnect.Services.POS.Application.DayClose;

namespace NexaConnect.Services.POS.Infrastructure.DayClose;

public sealed class SettlementRecoveryWorker(IServiceScopeFactory scopes, ILogger<SettlementRecoveryWorker> logger) : BackgroundService
{
    public const string TelemetryName = "NexaConnect.POS.SettlementRecovery";
    private static readonly ActivitySource Activities = new(TelemetryName);
    private static readonly Meter Meter = new(TelemetryName);
    private static readonly Counter<long> Attempts = Meter.CreateCounter<long>("settlement.recovery.attempts");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IDaySettlementStore>();
                var recovery = scope.ServiceProvider.GetRequiredService<DaySettlementRecovery>();
                foreach (var state in await store.PendingAsync(stoppingToken))
                {
                    using var activity = Activities.StartActivity("settlement.recover", ActivityKind.Internal);
                    using var correlation = NexaConnect.Observability.CorrelationContext.Push(state.TraceCorrelationId??state.CorrelationId.ToString("D"));
                    using var logging=logger.BeginScope(new Dictionary<string,object>{["CorrelationId"]=state.TraceCorrelationId??state.CorrelationId.ToString("D"),["TraceId"]=activity?.TraceId.ToString()??string.Empty});
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(25));
                    try
                    {
                        await recovery.RecoverAsync(state, deadline.Token);
                        Attempts.Add(1, new KeyValuePair<string, object?>("outcome", "completed"));
                    }
                    catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                    {
                        activity?.SetStatus(ActivityStatusCode.Error);
                        Attempts.Add(1, new KeyValuePair<string, object?>("outcome", "retry"));
                        logger.LogWarning("Settlement recovery deferred; category {Category}", e.GetType().Name);
                    }
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Settlement recovery inventory unavailable; category {Category}", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
