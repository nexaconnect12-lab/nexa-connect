using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Payment.Application.Refunds;
using Npgsql;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed record RefundFinancialSource(DateTimeOffset ObservedAtUtc, int Candidates, int EvidenceGaps,
    int Unretained, int Requeued, IReadOnlyList<PaymentRefundedV1> Events);

public sealed class PostgresRefundFinancialReplay(NpgsqlDataSource dataSource)
{
    public async Task<RefundFinancialSource> RunAsync(Guid organization, Guid branch, DateTimeOffset from, DateTimeOffset to,
        bool apply, string? actor, CancellationToken cancellationToken)
    {
        if (organization == Guid.Empty || branch == Guid.Empty || from == default || to <= from || to - from > TimeSpan.FromDays(31) || to > DateTimeOffset.UtcNow)
            throw new ArgumentException("Explicit tenant, branch and a positive maximum 31-day window are required.");
        if (apply && (string.IsNullOrWhiteSpace(actor) || actor.Length > 128 || actor.Any(char.IsControl)))
            throw new ArgumentException("An explicit bounded replay operator identity is required.");
        from = from.ToUniversalTime(); to = to.ToUniversalTime();
        Guid runId = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var ids = new List<Guid>();
        await using (var query = new NpgsqlCommand("SELECT id FROM refunds WHERE organization_id=$1 AND branch_id=$2 AND status='completed' AND completed_at_utc>=$3 AND completed_at_utc<$4 ORDER BY id LIMIT 10001",connection))
        {
            query.Parameters.AddWithValue(organization); query.Parameters.AddWithValue(branch);
            query.Parameters.AddWithValue(from); query.Parameters.AddWithValue(to);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        }
        if (ids.Count > 10000) throw new ArgumentException("Refund scope exceeds 10000 rows; narrow the window.");
        var events = new List<PaymentRefundedV1>(); int gaps = 0, unretained = 0, requeued = 0;
        foreach (Guid id in ids)
        {
            await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,cancellationToken);
            PaymentRefund refund;
            await using (var query = new NpgsqlCommand(PostgresPaymentRefunds.Select + " WHERE id=$1 AND organization_id=$2 AND branch_id=$3" + (apply ? " FOR UPDATE" : ""),connection,transaction))
            {
                query.Parameters.AddWithValue(id); query.Parameters.AddWithValue(organization); query.Parameters.AddWithValue(branch);
                await using var reader = await query.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) { gaps++; continue; }
                refund = PostgresPaymentRefunds.Map(reader);
            }
            string? retainedJson;
            await using (var query = new NpgsqlCommand("SELECT payload::text FROM refund_financial_publications WHERE refund_id=$1",connection,transaction))
            { query.Parameters.AddWithValue(id); retainedJson = (string?)await query.ExecuteScalarAsync(cancellationToken); }
            var originals = new List<(string Payload,Guid Id,int Version,string Aggregate,string Correlation,DateTimeOffset At)>();
            await using (var query = new NpgsqlCommand("SELECT payload::text,id,contract_version,aggregate_type,correlation_id,occurred_at_utc FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.refunded.v1' LIMIT 2",connection,transaction))
            {
                query.Parameters.AddWithValue(id); await using var reader = await query.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) originals.Add((reader.GetString(0),reader.GetGuid(1),reader.GetInt32(2),reader.GetString(3),reader.GetString(4),reader.GetFieldValue<DateTimeOffset>(5)));
            }
            PaymentRefundedV1? value;
            try
            {
                if (originals.Count > 1 || (retainedJson is null && originals.Count == 0)) { gaps++; continue; }
                value = JsonSerializer.Deserialize<PaymentRefundedV1>(retainedJson ?? originals.Single().Payload) ?? throw new JsonException("Refund evidence is missing.");
                RefundFinancialEvidence.Validate(refund,value);
                if (originals.Count == 1)
                {
                    var original=originals[0];
                    if (JsonSerializer.Deserialize<PaymentRefundedV1>(original.Payload) != value || original.Id!=value.EventId
                        || original.Version!=1 || original.Aggregate!="payment-refund" || original.Correlation!=value.CorrelationId.ToString("D")
                        || original.At.UtcTicks/10!=value.OccurredAtUtc.UtcTicks/10)
                        throw new InvalidOperationException("Original refund outbox conflicts with retained evidence.");
                }
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
            {
                if (apply && retainedJson is not null) throw new InvalidOperationException("Retained refund evidence conflicts; investigate before replay.");
                gaps++; continue;
            }
            if (retainedJson is null)
            {
                if (apply) PostgresRefundFinancialPublication.Retain(connection,transaction,refund,value);
                else unretained++;
            }
            if (apply)
            {
                await using var replay = new NpgsqlCommand("""
                    INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc)
                    SELECT event_id,'payment.refunded.v1',1,'payment-refund',refund_id,payload,payload->>'CorrelationId',refunded_at_utc
                    FROM refund_financial_publications WHERE refund_id=$1
                    ON CONFLICT(id) DO UPDATE SET published_at_utc=NULL,retry_count=0,next_attempt_at_utc=NULL,last_error_category=NULL
                    WHERE outbox_messages.event_type='payment.refunded.v1' AND outbox_messages.aggregate_id=EXCLUDED.aggregate_id
                      AND outbox_messages.payload=EXCLUDED.payload AND outbox_messages.contract_version=EXCLUDED.contract_version
                      AND outbox_messages.aggregate_type=EXCLUDED.aggregate_type AND outbox_messages.correlation_id=EXCLUDED.correlation_id
                      AND outbox_messages.occurred_at_utc=EXCLUDED.occurred_at_utc
                    """,connection,transaction);
                replay.Parameters.AddWithValue(id);
                if (await replay.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Refund outbox identity conflict.");
                await using var audit = new NpgsqlCommand("INSERT INTO refund_financial_replay_audit(run_id,refund_id,organization_id,branch_id,actor) VALUES($1,$2,$3,$4,$5)",connection,transaction);
                audit.Parameters.AddWithValue(runId); audit.Parameters.AddWithValue(id); audit.Parameters.AddWithValue(organization);
                audit.Parameters.AddWithValue(branch); audit.Parameters.AddWithValue(actor!);
                await audit.ExecuteNonQueryAsync(cancellationToken); requeued++;
            }
            await transaction.CommitAsync(cancellationToken); events.Add(value);
        }
        return new(DateTimeOffset.UtcNow,ids.Count,gaps,unretained,requeued,events);
    }
}
