using System.Diagnostics;
using NexaConnect.Services.POS.Application.CashReviews;

namespace NexaConnect.Services.POS.Infrastructure.Messaging;

public sealed class CashClosePublicationWorker(IServiceScopeFactory scopes, ILogger<CashClosePublicationWorker> logger) : BackgroundService
{
    private static readonly ActivitySource Activities = new("nexaconnect-pos");
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("nexaconnect-pos");
    private static readonly System.Diagnostics.Metrics.Counter<long> Outcomes = Meter.CreateCounter<long>("pos.cash_close.publication.outcomes");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Guid? after = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var publisher = scope.ServiceProvider.GetRequiredService<CashClosePublisher>();
                var candidates = await publisher.FindAsync(after, stoppingToken);
                foreach (var candidate in candidates)
                {
                    Guid correlation = Guid.NewGuid();
                    using var correlationScope = NexaConnect.Observability.CorrelationContext.Push(correlation.ToString("D"));
                    using var activity = Activities.StartActivity("cash-close.publish");
                    using var logScope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlation });
                    try
                    {
                        if (await publisher.PublishAsync(candidate, correlation, stoppingToken))
                        {
                            Outcomes.Add(1, new KeyValuePair<string, object?>("status", "queued"));
                            logger.LogInformation("POS cash-close snapshot queued");
                        }
                    }
                    catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    { Outcomes.Add(1, new KeyValuePair<string, object?>("status", "retry")); activity?.SetStatus(ActivityStatusCode.Error); logger.LogWarning("POS cash-close publication failed; retry on next scan"); }
                    after = candidate.SessionId;
                }
                if (candidates.Count < 100) after = null;
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            { Outcomes.Add(1, new KeyValuePair<string, object?>("status", "scan_failure")); logger.LogWarning("POS cash-close scan unavailable"); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
