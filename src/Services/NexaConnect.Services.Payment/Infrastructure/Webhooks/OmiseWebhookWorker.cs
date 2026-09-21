using Microsoft.Extensions.Options;
using NexaConnect.Observability;
using NexaConnect.Services.Payment.Application.Webhooks;

namespace NexaConnect.Services.Payment.Infrastructure.Webhooks;

public sealed class OmiseWebhookWorker(IServiceScopeFactory scopes, IOptions<OmiseWebhookOptions> options,
    ILogger<OmiseWebhookWorker> logger) : BackgroundService
{
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("nexaconnect-payment");
    private static readonly System.Diagnostics.ActivitySource Activities = new("nexaconnect-payment");
    private static readonly System.Diagnostics.Metrics.Counter<long> Outcomes = Meter.CreateCounter<long>("payment.omise_webhook.outcomes");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var inbox = scope.ServiceProvider.GetRequiredService<IOmiseWebhookInbox>();
                var claim = await inbox.ClaimAsync(options.Value.LeaseDuration, stoppingToken);
                if (claim is null) { await Task.Delay(options.Value.PollInterval, stoppingToken); continue; }
                using var correlation = CorrelationContext.Push(claim.TraceCorrelationId ?? claim.CorrelationId.ToString("D"));
                using var activity = Activities.StartActivity("payment.omise_webhook.process",System.Diagnostics.ActivityKind.Consumer);
                using var logging = logger.BeginScope(new Dictionary<string,object>
                { ["CorrelationId"] = claim.TraceCorrelationId ?? claim.CorrelationId.ToString("D"),
                  ["TraceId"] = activity?.TraceId.ToString() ?? string.Empty });
                string outcome;
                // Automatic outbound URL spans would expose event/charge references. Export the bounded consumer span instead.
                try
                {
                    using var suppression = OpenTelemetry.SuppressInstrumentationScope.Begin();
                    outcome = await scope.ServiceProvider.GetRequiredService<OmiseWebhookProcessor>().ProcessAsync(claim, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                { outcome = "retry"; logger.LogWarning("Omise webhook processing failed; durable delivery remains retryable"); }
                await inbox.FinishAsync(claim, outcome, options.Value.RetryDelay, options.Value.MaximumAttempts, stoppingToken);
                Outcomes.Add(1, new KeyValuePair<string,object?>("outcome",outcome));
                activity?.SetTag("outcome",outcome);
                logger.LogInformation("Omise webhook processing finished with {Outcome}", outcome);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Omise webhook inbox unavailable; retrying after bounded delay");
                await Task.Delay(options.Value.PollInterval, stoppingToken);
            }
        }
    }
}
