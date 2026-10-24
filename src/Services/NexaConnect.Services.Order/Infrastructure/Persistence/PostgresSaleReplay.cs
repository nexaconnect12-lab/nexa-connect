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
                requeued += await PostgresSalePublication.RequeueAsync(connection, transaction, id, organization, branch, runId, actor!, cancellationToken);
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
