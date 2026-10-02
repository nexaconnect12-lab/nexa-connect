using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using Npgsql;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed record SaleReplayResult(int Candidates, int MissingReceipts, int Published, int Requeued,
    IReadOnlyList<OrderSaleCompletedV1> Publications);

/// <summary>Operator-only bounded recovery. This service owns all operational reads and writes.</summary>
public sealed class PostgresSaleReplay(NpgsqlDataSource dataSource)
{
    public async Task<SaleReplayResult> RunAsync(Guid organization, Guid branch, DateTimeOffset from,
        DateTimeOffset to, bool apply, CancellationToken cancellationToken, string? actor = null)
    {
        if (organization == Guid.Empty || branch == Guid.Empty || from == default || to <= from || to - from > TimeSpan.FromDays(31))
            throw new ArgumentException("Explicit tenant, branch and a maximum 31-day UTC window are required.");
        if (apply && (string.IsNullOrWhiteSpace(actor) || actor.Length > 128 || actor.Any(char.IsControl)))
            throw new ArgumentException("An explicit bounded operator identity is required for replay attribution.");
        from = from.ToUniversalTime(); to = to.ToUniversalTime();
        Guid runId = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var ids = new List<Guid>(); int missing = 0, published = 0, requeued = 0;
        await using (var read = new NpgsqlCommand("""
            SELECT id,receipt_snapshot IS NULL FROM orders WHERE organization_id=$1 AND branch_id=$2
              AND status='completed' AND COALESCE((receipt_snapshot->>'PaidAtUtc')::timestamptz,updated_at_utc)>=$3
              AND COALESCE((receipt_snapshot->>'PaidAtUtc')::timestamptz,updated_at_utc)<$4 ORDER BY id LIMIT 10001
            """, connection))
        {
            read.Parameters.AddWithValue(organization); read.Parameters.AddWithValue(branch);
            read.Parameters.AddWithValue(from); read.Parameters.AddWithValue(to);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) { ids.Add(reader.GetGuid(0)); if (reader.GetBoolean(1)) missing++; }
        }
        if (ids.Count > 10000) throw new ArgumentException("Window exceeds 10000 orders; choose a smaller window.");
        if (apply)
        {
            foreach (Guid id in ids)
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                if (await PostgresSalePublication.EnsureAsync(connection, transaction, id, cancellationToken)) published++;
                await using var replay = new NpgsqlCommand("""
                    INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc)
                    SELECT event_id,'order.sale-completed.v1',1,'Order',order_id,payload,payload->>'CorrelationId',paid_at_utc
                    FROM order_sale_publications WHERE order_id=$1 AND organization_id=$2 AND branch_id=$3
                    ON CONFLICT(id) DO UPDATE SET published_at_utc=NULL,retry_count=0,next_attempt_at_utc=NULL,last_error_category=NULL
                    WHERE outbox_messages.event_type='order.sale-completed.v1' AND outbox_messages.payload=EXCLUDED.payload
                    """, connection, transaction);
                replay.Parameters.AddWithValue(id); replay.Parameters.AddWithValue(organization); replay.Parameters.AddWithValue(branch);
                int affected = await replay.ExecuteNonQueryAsync(cancellationToken);
                if (affected == 0)
                {
                    await using var retained = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM order_sale_publications WHERE order_id=$1)", connection, transaction);
                    retained.Parameters.AddWithValue(id);
                    if ((bool)(await retained.ExecuteScalarAsync(cancellationToken))!)
                        throw new InvalidOperationException("Retained sale publication conflicts with outbox evidence.");
                }
                requeued += affected;
                if (affected > 0)
                {
                    await using var audit = new NpgsqlCommand("INSERT INTO order_sale_replay_audit(run_id,order_id,organization_id,branch_id,actor) VALUES($1,$2,$3,$4,$5)", connection, transaction);
                    audit.Parameters.AddWithValue(runId); audit.Parameters.AddWithValue(id);
                    audit.Parameters.AddWithValue(organization); audit.Parameters.AddWithValue(branch); audit.Parameters.AddWithValue(actor!);
                    await audit.ExecuteNonQueryAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
        }
        var publications = new List<OrderSaleCompletedV1>();
        await using (var read = new NpgsqlCommand("""
            SELECT payload::text FROM order_sale_publications WHERE organization_id=$1 AND branch_id=$2
              AND paid_at_utc>=$3 AND paid_at_utc<$4 ORDER BY order_id
            """, connection))
        {
            read.Parameters.AddWithValue(organization); read.Parameters.AddWithValue(branch);
            read.Parameters.AddWithValue(from); read.Parameters.AddWithValue(to);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) publications.Add(JsonSerializer.Deserialize<OrderSaleCompletedV1>(reader.GetString(0))!);
        }
        return new(ids.Count, missing, published, requeued, publications);
    }
}
