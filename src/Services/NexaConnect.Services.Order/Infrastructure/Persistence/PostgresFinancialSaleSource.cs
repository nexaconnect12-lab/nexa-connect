using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain;
using Npgsql;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed record FinancialSaleSource(DateTimeOffset ObservedAtUtc, int Candidates, int EvidenceGaps,
    int Unretained, int Requeued, IReadOnlyList<OrderSaleCompletedV1> Events);

/// <summary>Commercial source manifest using each report's original time basis; not a cross-service query.</summary>
public sealed class PostgresFinancialSaleSource(NpgsqlDataSource dataSource)
{
    public async Task<FinancialSaleSource> RunAsync(Guid organization, Guid branch, DateTimeOffset from, DateTimeOffset to,
        bool apply, string? actor, CancellationToken cancellationToken)
    {
        if (organization == Guid.Empty || branch == Guid.Empty || from == default || to <= from || to - from > TimeSpan.FromDays(31) || to > DateTimeOffset.UtcNow)
            throw new ArgumentException("Explicit tenant, branch and a maximum 31-day window are required.");
        if (apply && (string.IsNullOrWhiteSpace(actor) || actor.Length > 128 || actor.Any(char.IsControl)))
            throw new ArgumentException("An explicit bounded replay operator identity is required.");
        from = from.ToUniversalTime(); to = to.ToUniversalTime(); Guid runId = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var ids = new List<Guid>();
        await using (var query = new NpgsqlCommand("""
            SELECT id FROM orders WHERE organization_id=$1 AND branch_id=$2 AND status='completed'
              AND ((created_at_utc>=$3 AND created_at_utc<$4)
                OR (COALESCE((receipt_snapshot->>'PaidAtUtc')::timestamptz,updated_at_utc)>=$3
                  AND COALESCE((receipt_snapshot->>'PaidAtUtc')::timestamptz,updated_at_utc)<$4))
            ORDER BY id LIMIT 10001
            """,connection))
        {
            query.Parameters.AddWithValue(organization); query.Parameters.AddWithValue(branch);
            query.Parameters.AddWithValue(from); query.Parameters.AddWithValue(to);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        }
        if (ids.Count > 10000) throw new ArgumentException("Sale scope exceeds 10000 orders; narrow the window.");
        int gaps = 0, unretained = 0, requeued = 0; var events = new List<OrderSaleCompletedV1>();
        foreach (Guid id in ids)
        {
            await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,cancellationToken);
            OrderSaleCompletedV1 value; string? retained;
            try
            {
                await using var query = new NpgsqlCommand("""
                    SELECT o.receipt_snapshot::text,o.created_at_utc,o.payment_intent_id,s.id,o.channel,o.service_type,
                      o.workflow_correlation_id,p.payload::text
                    FROM orders o LEFT JOIN order_manual_tender_settlements s ON s.order_id=o.id
                    LEFT JOIN order_sale_publications p ON p.order_id=o.id
                    WHERE o.id=$1 AND o.organization_id=$2 AND o.branch_id=$3 AND o.status='completed'
                    """ + (apply ? " FOR UPDATE OF o" : ""),connection,transaction);
                query.Parameters.AddWithValue(id); query.Parameters.AddWithValue(organization); query.Parameters.AddWithValue(branch);
                await using var reader = await query.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) { gaps++; continue; }
                var receipt = JsonSerializer.Deserialize<PaidOrderReceipt>(reader.GetString(0)) ?? throw new JsonException("Sale receipt evidence is missing.");
                value = SaleCompletionEvents.Create(receipt,reader.GetFieldValue<DateTimeOffset>(1),reader.IsDBNull(2)?null:reader.GetGuid(2),
                    reader.IsDBNull(3)?null:reader.GetGuid(3),reader.GetString(4),reader.GetString(5),reader.IsDBNull(6)?null:reader.GetGuid(6));
                retained = reader.IsDBNull(7)?null:reader.GetString(7);
                if (retained is not null && JsonSerializer.Deserialize<OrderSaleCompletedV1>(retained) != value)
                    throw new InvalidOperationException("Retained sale differs from source receipt evidence.");
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
            { gaps++; continue; }
            if (apply)
            {
                await PostgresSalePublication.EnsureAsync(connection,transaction,id,cancellationToken);
                requeued += await PostgresSalePublication.RequeueAsync(connection,transaction,id,organization,branch,runId,actor!,cancellationToken);
            }
            else if (retained is null) unretained++;
            await transaction.CommitAsync(cancellationToken); events.Add(value);
        }
        return new(DateTimeOffset.UtcNow,ids.Count,gaps,unretained,requeued,events);
    }
}
