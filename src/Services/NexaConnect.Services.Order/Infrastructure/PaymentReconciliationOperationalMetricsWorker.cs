using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace NexaConnect.Services.Order.Infrastructure;

public sealed class OrderOperationalMetricsOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class PaymentReconciliationOperationalMetricsWorker : BackgroundService
{
    private static readonly Meter Meter = new("nexaconnect-order");
    private static readonly Counter<long> CollectionFailures = Meter.CreateCounter<long>(
        "order.operational_metrics.collection_failures");
    private long pendingInbox;
    private double oldestExpiredLeaseAgeSeconds;
    private long unpublishedOutbox;
    private double unpublishedOutboxOldestAgeSeconds;
    private readonly NpgsqlDataSource dataSource;
    private readonly IOptions<OrderOperationalMetricsOptions> options;
    private readonly ILogger<PaymentReconciliationOperationalMetricsWorker> logger;
    private readonly ObservableGauge<long> pendingInboxGauge;
    private readonly ObservableGauge<double> expiredLeaseAgeGauge;
    private readonly ObservableGauge<long> outboxGauge;
    private readonly ObservableGauge<double> outboxAgeGauge;

    public PaymentReconciliationOperationalMetricsWorker(
        NpgsqlDataSource dataSource,
        IOptions<OrderOperationalMetricsOptions> options,
        ILogger<PaymentReconciliationOperationalMetricsWorker> logger)
    {
        this.dataSource = dataSource;
        this.options = options;
        this.logger = logger;
        pendingInboxGauge = Meter.CreateObservableGauge(
            "order.payment_reconciliation.inbox_pending", () => Volatile.Read(ref pendingInbox));
        expiredLeaseAgeGauge = Meter.CreateObservableGauge(
            "order.payment_reconciliation.oldest_expired_lease_age_seconds",
            () => Volatile.Read(ref oldestExpiredLeaseAgeSeconds));
        outboxGauge = Meter.CreateObservableGauge(
            "order.outbox.unpublished", () => Volatile.Read(ref unpublishedOutbox));
        outboxAgeGauge = Meter.CreateObservableGauge(
            "order.outbox.oldest_age_seconds", () => Volatile.Read(ref unpublishedOutboxOldestAgeSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = options.Value.PollInterval > TimeSpan.Zero
            ? options.Value.PollInterval
            : TimeSpan.FromSeconds(30);
        using var timer = new PeriodicTimer(interval);
        do
        {
            await CollectAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT count(*)::bigint,
                   COALESCE(EXTRACT(EPOCH FROM now() -
                       (min(locked_until_utc) FILTER
                           (WHERE status = 'processing' AND locked_until_utc <= now()))), 0)::double precision
            FROM inbox_messages
            WHERE consumer_name = 'order.payment-reconciled.v1'
              AND (status = 'queued' OR (status = 'processing' AND locked_until_utc <= now()));

            SELECT count(*)::bigint,
                   COALESCE(EXTRACT(EPOCH FROM now() - min(occurred_at_utc)), 0)::double precision
            FROM outbox_messages
            WHERE published_at_utc IS NULL;
            """;
        try
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            Interlocked.Exchange(ref pendingInbox, reader.GetInt64(0));
            Volatile.Write(ref oldestExpiredLeaseAgeSeconds, Math.Max(0, reader.GetDouble(1)));
            await reader.NextResultAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            Interlocked.Exchange(ref unpublishedOutbox, reader.GetInt64(0));
            Volatile.Write(ref unpublishedOutboxOldestAgeSeconds, Math.Max(0, reader.GetDouble(1)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CollectionFailures.Add(1);
            logger.LogWarning(exception, "Failed to collect Order payment-reconciliation operational metrics");
        }
    }
}
