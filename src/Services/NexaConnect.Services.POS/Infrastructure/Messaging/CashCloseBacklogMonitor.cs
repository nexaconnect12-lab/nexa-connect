using System.Diagnostics.Metrics;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Messaging;

public sealed class CashCloseBacklogMonitor(NpgsqlDataSource dataSource, ILogger<CashCloseBacklogMonitor> logger) : BackgroundService
{
    private readonly Meter meter = new("nexaconnect-pos");
    private long pending;
    private double oldestSeconds;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        meter.CreateObservableGauge("pos.cash_close.outbox.pending", () => Interlocked.Read(ref pending));
        meter.CreateObservableGauge("pos.cash_close.outbox.oldest_age", () => Volatile.Read(ref oldestSeconds), "s");
        var failures = meter.CreateCounter<long>("pos.cash_close.metrics.failures");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var command = dataSource.CreateCommand("""
                    SELECT count(*),COALESCE(EXTRACT(EPOCH FROM clock_timestamp()-min(occurred_at_utc)),0)::double precision
                    FROM outbox_messages WHERE event_type='pos.cash-close.snapshot.v1' AND published_at_utc IS NULL
                    """);
                await using var reader = await command.ExecuteReaderAsync(stoppingToken);
                await reader.ReadAsync(stoppingToken);
                Interlocked.Exchange(ref pending, reader.GetInt64(0));
                Volatile.Write(ref oldestSeconds, Math.Max(0, reader.GetDouble(1)));
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                failures.Add(1);
                logger.LogWarning("POS cash-close backlog metrics unavailable");
            }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
    public override void Dispose() { base.Dispose(); meter.Dispose(); }
}
