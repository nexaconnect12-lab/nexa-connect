using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class PaymentOperationalMetricsOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class PaymentOperationalMetricsWorker : BackgroundService
{
    private static readonly Meter Meter = new("nexaconnect-payment");
    private static readonly Counter<long> CollectionFailures = Meter.CreateCounter<long>(
        "payment.operational_metrics.collection_failures");
    private long recoveryBacklog;
    private double recoveryOldestAgeSeconds;
    private long unpublishedOutbox;
    private double unpublishedOutboxOldestAgeSeconds;
    private readonly NpgsqlDataSource dataSource;
    private readonly IOptions<PaymentOperationalMetricsOptions> options;
    private readonly ILogger<PaymentOperationalMetricsWorker> logger;
    private readonly ObservableGauge<long> recoveryBacklogGauge;
    private readonly ObservableGauge<double> recoveryAgeGauge;
    private readonly ObservableGauge<long> outboxGauge;
    private readonly ObservableGauge<double> outboxAgeGauge;

    public PaymentOperationalMetricsWorker(
        NpgsqlDataSource dataSource,
        IOptions<PaymentOperationalMetricsOptions> options,
        ILogger<PaymentOperationalMetricsWorker> logger)
    {
        this.dataSource = dataSource;
        this.options = options;
        this.logger = logger;
        recoveryBacklogGauge = Meter.CreateObservableGauge(
            "payment.capture_recovery.backlog", () => Volatile.Read(ref recoveryBacklog));
        recoveryAgeGauge = Meter.CreateObservableGauge(
            "payment.capture_recovery.oldest_age_seconds", () => Volatile.Read(ref recoveryOldestAgeSeconds));
        outboxGauge = Meter.CreateObservableGauge(
            "payment.outbox.unpublished", () => Volatile.Read(ref unpublishedOutbox));
        outboxAgeGauge = Meter.CreateObservableGauge(
            "payment.outbox.oldest_age_seconds", () => Volatile.Read(ref unpublishedOutboxOldestAgeSeconds));
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
                   COALESCE(EXTRACT(EPOCH FROM now() - min(COALESCE(capture_lease_expires_at_utc, updated_at_utc))), 0)::double precision
            FROM payment_intents
            WHERE status = 'capture_unknown'
               OR (status = 'capturing' AND capture_lease_expires_at_utc <= now());

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
            Interlocked.Exchange(ref recoveryBacklog, reader.GetInt64(0));
            Volatile.Write(ref recoveryOldestAgeSeconds, Math.Max(0, reader.GetDouble(1)));
            await reader.NextResultAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            Interlocked.Exchange(ref unpublishedOutbox, reader.GetInt64(0));
            Volatile.Write(ref unpublishedOutboxOldestAgeSeconds, Math.Max(0, reader.GetDouble(1)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CollectionFailures.Add(1);
            logger.LogWarning(exception, "Failed to collect Payment operational backlog metrics");
        }
    }
}
