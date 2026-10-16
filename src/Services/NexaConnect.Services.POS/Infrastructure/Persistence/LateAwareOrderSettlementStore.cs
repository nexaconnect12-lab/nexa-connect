using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.POS.Application.OrderSettlements;
using NexaConnect.Services.POS.Domain;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class LateAwareOrderSettlementStore(NpgsqlDataSource source) : IOrderSettlementProjectionStore
{
    public async Task<OrderSettlementProjectionStatus> ProjectAsync(OrderManualTenderSettledV1 value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        var custody=new PostgresLateFinancialWork(source);
        var disposition=await custody.DispositionAsync(value.OrganizationId,"order.manual-tender-settled.v1",value.EventId.ToString("D"),hash,ct);
        if(disposition=="committed")return OrderSettlementProjectionStatus.LateCaptured;
        if(disposition=="armed")throw new LateFinancialWorkHeldException();
        try { return await new PostgresOrderSettlementProjectionStore(source).ProjectAsync(value, ct); }
        catch (PostgresException e) when (e.SqlState == "PDS01")
        {
            // Resolve the actual drawer, including its close/review state; event time alone cannot assign a cash close.
            await using var q = source.CreateCommand("""
                SELECT c.id,c.status,c.concurrency_version,c.opened_at_utc,c.closed_at_utc,
                       COALESCE(c.variance_amount<>0,false),r.status,r.reviewed_session_version
                FROM terminals t JOIN stores s ON s.id=t.store_id JOIN shifts sh ON sh.store_id=s.id AND sh.terminal_id=t.id
                JOIN cash_sessions c ON c.shift_id=sh.id AND c.store_id=s.id
                LEFT JOIN cash_session_review_states r ON r.cash_session_id=c.id
                WHERE t.id=$1 AND s.restaurant_id=$2 AND s.branch_id=$3 AND sh.opened_at_utc<=$4
                  AND (sh.closed_at_utc IS NULL OR sh.closed_at_utc>=$4) AND c.opened_at_utc<=$4
                  AND (c.closed_at_utc IS NULL OR c.closed_at_utc>=$4) AND btrim(c.currency)=$5
                """);
            q.Parameters.AddWithValue(value.TerminalId); q.Parameters.AddWithValue(value.RestaurantId); q.Parameters.AddWithValue(value.BranchId);
            q.Parameters.AddWithValue(value.OccurredAtUtc); q.Parameters.AddWithValue(value.Currency);
            FinancialRecord? before = null;
            await using (var rows = await q.ExecuteReaderAsync(ct))
            {
                if (await rows.ReadAsync(ct)) before = new(rows.GetGuid(0), rows.GetString(1), rows.GetInt64(2), rows.GetFieldValue<DateTimeOffset>(3),
                    rows.IsDBNull(4) ? null : rows.GetFieldValue<DateTimeOffset>(4), rows.GetBoolean(5), rows.IsDBNull(6) ? null : rows.GetString(6), rows.IsDBNull(7) ? null : rows.GetInt64(7));
                if (await rows.ReadAsync(ct)) throw new OrderSettlementProjectionConflictException("Ambiguous settlement drawer.");
            }
            if (before is null) throw;
            var after = before with { Version = checked(before.Version + 1), HasVariance = true };
            var captured = await custody.CaptureAsync(value.OrganizationId, value.RestaurantId, value.BranchId,
                "order.manual-tender-settled.v1", value.EventId.ToString("D"), hash, json,
                (from, to) => !FinancialDayFence.AllowsMutation(from, to, before, after, false, "cash_movements"), ct);
            if (!captured) throw;
            return OrderSettlementProjectionStatus.LateCaptured;
        }
    }
}
