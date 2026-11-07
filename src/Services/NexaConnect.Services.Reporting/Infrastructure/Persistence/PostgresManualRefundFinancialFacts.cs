using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Reporting.Application;
using Npgsql;
using NpgsqlTypes;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;

namespace NexaConnect.Services.Reporting.Infrastructure.Persistence;

public sealed class PostgresManualRefundFinancialFacts(NpgsqlDataSource source) : IManualRefundFinancialFacts
{
    public async Task<ManualRefundDaySummary> ReadDayAsync(EndOfDayWindow w, CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        const string scope="FROM manual_refund_facts WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND refunded_at_utc >= $4 AND refunded_at_utc < $5";
        NpgsqlCommand Query(string sql)
        {
            var q=new NpgsqlCommand(sql,c,tx);
            foreach(var v in new object[]{w.OrganizationId,w.RestaurantId,w.BranchId,w.FromUtc.ToUniversalTime(),w.ToUtc.ToUniversalTime()})q.Parameters.AddWithValue(v);
            return q;
        }
        decimal cash,prompt;int count;
        await using(var q=Query("SELECT COALESCE(sum(amount) FILTER(WHERE method='cash'),0),COALESCE(sum(amount) FILTER(WHERE method='promptpay_manual'),0),count(*)::int "+scope))
        await using(var rows=await q.ExecuteReaderAsync(ct))
        { await rows.ReadAsync(ct);cash=rows.GetDecimal(0);prompt=rows.GetDecimal(1);count=rows.GetInt32(2); }
        string? hash=null;
        if(count>0)
        {
            await using var q=Query("SELECT jsonb_build_array(operation_id,order_id,settlement_id,method,amount,btrim(currency),(extract(epoch from refunded_at_utc)*1000000)::bigint,cash_movement_id)::text "+scope+" ORDER BY operation_id LIMIT 10001");
            hash=await BoundedEvidenceHash.ReadAsync(q,"manual-refund-day-v1",ct);
        }
        await tx.CommitAsync(ct);
        return new(cash,prompt,count,0,0,hash);
    }
    public async Task<bool> ProjectAsync(PosManualRefundCompletedV1 value, CancellationToken ct)
    {
        ManualRefundFinancialReporting.Validate(value);
        string hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            value.EventId, value.OrganizationId, value.RestaurantId, value.BranchId, value.OrderId,
            value.SettlementId, value.OperationId, value.Method, Amount = value.Amount.ToString("0.0000", CultureInfo.InvariantCulture),
            value.Currency, value.CashMovementId, UtcTicks = value.OccurredAtUtc.UtcTicks
        })));
        try
        {
            await using var connection = await source.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using var insert = new NpgsqlCommand("""
                INSERT INTO manual_refund_facts(operation_id,source_event_id,organization_id,restaurant_id,branch_id,
                    order_id,settlement_id,method,amount,currency,refunded_at_utc,payload_hash,cash_movement_id)
                VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13) ON CONFLICT(source_event_id) DO NOTHING
                """, connection, transaction);
            foreach (var parameter in new object[] { value.OperationId, value.EventId, value.OrganizationId, value.RestaurantId,
                value.BranchId, value.OrderId, value.SettlementId, value.Method, value.Amount, value.Currency, value.OccurredAtUtc.ToUniversalTime(), hash })
                insert.Parameters.AddWithValue(parameter);
            insert.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)value.CashMovementId ?? DBNull.Value });
            if (await insert.ExecuteNonQueryAsync(ct) == 0)
            {
                await using var existing = new NpgsqlCommand("SELECT payload_hash FROM manual_refund_facts WHERE source_event_id=$1",
                    connection, transaction);
                existing.Parameters.AddWithValue(value.EventId);
                if (!string.Equals(await existing.ExecuteScalarAsync(ct) as string, hash, StringComparison.Ordinal))
                    throw new ArgumentException("Manual refund event identity conflict.");
                await transaction.CommitAsync(ct);
                return false;
            }
            await using var checkpoint = new NpgsqlCommand("""
                INSERT INTO projection_checkpoints(projector_name,source_stream,position,last_event_id,last_event_occurred_at_utc,updated_at_utc)
                VALUES('manual-refund-financial','pos.manual-refund-completed.v1',1,$1,$2,clock_timestamp())
                ON CONFLICT(projector_name,source_stream) DO UPDATE SET position=projection_checkpoints.position+1,
                    last_event_id=EXCLUDED.last_event_id,last_event_occurred_at_utc=EXCLUDED.last_event_occurred_at_utc,
                    updated_at_utc=EXCLUDED.updated_at_utc
                """, connection, transaction);
            checkpoint.Parameters.AddWithValue(value.EventId);
            checkpoint.Parameters.AddWithValue(value.OccurredAtUtc.ToUniversalTime());
            await checkpoint.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ArgumentException("Manual refund operation or drawer movement already has financial evidence.", e);
        }
    }
}
