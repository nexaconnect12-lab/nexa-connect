using System.Text.Json;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;
using Npgsql;

namespace NexaConnect.Services.Reporting.Infrastructure.Persistence;

public sealed class PostgresFinancialCompletenessRepository(NpgsqlDataSource dataSource):IFinancialCompletenessRepository
{
    public async Task<FinancialCompletenessObservation> CheckAsync(FinancialCompletenessSource source,CancellationToken cancellationToken)
    {
        source.Validate();
        await using var connection=await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction=await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,cancellationToken);
        var sales=new List<ProjectedSale>(); var payments=new List<ProjectedPayment>(); var refunds=new List<RefundFinancialFact>();
        await using(var query=Query("""
            SELECT order_id,source_event_id,organization_id,restaurant_id,branch_id,channel,service_type,btrim(currency),
              subtotal_amount,discount_amount,service_charge_amount,tax_amount,total_amount,order_status,ordered_at_utc,completed_at_utc,source_event_version
            FROM sales_facts WHERE (organization_id=$1 AND branch_id=$2 AND ordered_at_utc>=$3 AND ordered_at_utc<$4)
              OR order_id=ANY($5) LIMIT 20001
            """,source,source.Sales.Select(s=>s.OrderId).ToArray(),connection,transaction))
        {
            await using var r=await query.ExecuteReaderAsync(cancellationToken);
            while(await r.ReadAsync(cancellationToken)) sales.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetGuid(4),r.GetString(5),r.GetString(6),r.GetString(7),
                r.GetDecimal(8),r.GetDecimal(9),r.GetDecimal(10),r.GetDecimal(11),r.GetDecimal(12),r.GetString(13),r.GetFieldValue<DateTimeOffset>(14),r.IsDBNull(15)?null:r.GetFieldValue<DateTimeOffset>(15),r.GetInt64(16)));
        }
        await using(var query=Query("""
            SELECT payment_intent_id,source_event_id,organization_id,restaurant_id,branch_id,order_id,payment_method,payment_origin,
              provider_code,btrim(currency),paid_amount,refunded_amount,payment_status,paid_at_utc,source_event_version
            FROM payment_facts WHERE (organization_id=$1 AND branch_id=$2 AND paid_at_utc>=$3 AND paid_at_utc<$4)
              OR payment_intent_id=ANY($5) LIMIT 20001
            """,source,source.Sales.Select(s=>s.PaymentId).ToArray(),connection,transaction))
        {
            await using var r=await query.ExecuteReaderAsync(cancellationToken);
            while(await r.ReadAsync(cancellationToken)) payments.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetGuid(4),r.GetGuid(5),r.GetString(6),r.GetString(7),
                r.IsDBNull(8)?null:r.GetString(8),r.GetString(9),r.GetDecimal(10),r.GetDecimal(11),r.GetString(12),r.IsDBNull(13)?null:r.GetFieldValue<DateTimeOffset>(13),r.GetInt64(14)));
        }
        await using(var query=Query("""
            SELECT source_event_id,organization_id,restaurant_id,branch_id,order_id,payment_intent_id,refund_id,amount,btrim(currency),
              reason_code,refunded_at_utc,cumulative_refunded_amount,captured_amount,receipt_number
            FROM refund_facts WHERE (organization_id=$1 AND branch_id=$2 AND refunded_at_utc>=$3 AND refunded_at_utc<$4)
              OR refund_id=ANY($5) LIMIT 20001
            """,source,source.Refunds.Select(r=>r.RefundId).ToArray(),connection,transaction))
        {
            await using var r=await query.ExecuteReaderAsync(cancellationToken);
            while(await r.ReadAsync(cancellationToken)) refunds.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetGuid(4),r.GetGuid(5),r.GetGuid(6),r.GetDecimal(7),r.GetString(8),r.GetString(9),r.GetFieldValue<DateTimeOffset>(10),r.GetDecimal(11),r.GetDecimal(12),r.GetString(13)));
        }
        if(sales.Count>20000 || payments.Count>20000 || refunds.Count>20000) throw new ArgumentException("Reporting scope exceeds the bounded inventory; narrow the window.");
        var saleHashes=await Hashes("sale_fact_event_receipts",source.Sales.Select(s=>s.SourceEventId).ToArray(),connection,transaction,cancellationToken);
        var refundHashes=await Hashes("refund_fact_event_receipts",source.Refunds.Select(r=>r.SourceEventId).ToArray(),connection,transaction,cancellationToken);
        var observation=FinancialCompletenessEvaluator.Evaluate(source,new(sales,payments,refunds,saleHashes,refundHashes));
        await transaction.CommitAsync(cancellationToken); return observation;
    }
    public async Task RecordAsync(FinancialCompletenessObservation observation,string actor,CancellationToken cancellationToken)
    {
        await using var query=dataSource.CreateCommand("INSERT INTO financial_completeness_checks(id,organization_id,branch_id,from_utc,to_utc,checked_at_utc,actor,result) VALUES($1,$2,$3,$4,$5,$6,$7,$8::jsonb)");
        query.Parameters.AddWithValue(observation.CheckId); query.Parameters.AddWithValue(observation.Range.OrganizationId);
        query.Parameters.AddWithValue(observation.Range.BranchId); query.Parameters.AddWithValue(observation.Range.FromUtc);
        query.Parameters.AddWithValue(observation.Range.ToUtc); query.Parameters.AddWithValue(observation.CheckedAtUtc);
        query.Parameters.AddWithValue(actor); query.Parameters.AddWithValue(JsonSerializer.Serialize(observation));
        await query.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<FinancialCompletenessObservation?> LatestAsync(ReportingRange range,CancellationToken cancellationToken)
    {
        await using var query=dataSource.CreateCommand("SELECT result::text FROM financial_completeness_checks WHERE organization_id=$1 AND branch_id=$2 AND from_utc=$3 AND to_utc=$4 AND result->'Range'=$5::jsonb ORDER BY checked_at_utc DESC,id DESC LIMIT 1");
        query.Parameters.AddWithValue(range.OrganizationId); query.Parameters.AddWithValue(range.BranchId);
        query.Parameters.AddWithValue(range.FromUtc.ToUniversalTime()); query.Parameters.AddWithValue(range.ToUtc.ToUniversalTime());
        // PostgreSQL columns truncate to microseconds. Retained JSON distinguishes exact sub-microsecond request windows.
        query.Parameters.AddWithValue(JsonSerializer.Serialize(range with { FromUtc=range.FromUtc.ToUniversalTime(),ToUtc=range.ToUtc.ToUniversalTime() }));
        return await query.ExecuteScalarAsync(cancellationToken) is string json?JsonSerializer.Deserialize<FinancialCompletenessObservation>(json):null;
    }
    private static NpgsqlCommand Query(string sql,FinancialCompletenessSource source,Guid[] ids,NpgsqlConnection c,NpgsqlTransaction t)
    {
        var query=new NpgsqlCommand(sql,c,t); query.Parameters.AddWithValue(source.Range.OrganizationId);
        query.Parameters.AddWithValue(source.Range.BranchId); query.Parameters.AddWithValue(source.Range.FromUtc.ToUniversalTime());
        query.Parameters.AddWithValue(source.Range.ToUtc.ToUniversalTime()); query.Parameters.AddWithValue(ids); return query;
    }
    private static async Task<Dictionary<Guid,string>> Hashes(string table,Guid[] ids,NpgsqlConnection c,NpgsqlTransaction t,CancellationToken ct)
    {
        if(table is not ("sale_fact_event_receipts" or "refund_fact_event_receipts")) throw new ArgumentException("Unsupported receipt inventory.");
        string quoted=new NpgsqlCommandBuilder().QuoteIdentifier(table);
        await using var query=new NpgsqlCommand($"SELECT event_id,payload_hash FROM {quoted} WHERE event_id=ANY($1)",c,t);
        query.Parameters.AddWithValue(ids); var hashes=new Dictionary<Guid,string>(); await using var reader=await query.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct)) hashes.Add(reader.GetGuid(0),reader.GetString(1)); return hashes;
    }
}
