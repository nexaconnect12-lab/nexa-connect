using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

internal static class PostgresManualRefundDayEvidence
{
    private const string Scope = """
        FROM manual_refund_sales s JOIN manual_refund_approvals a USING (order_id)
        JOIN manual_refund_payouts p USING (operation_id)
        LEFT JOIN manual_refund_completions c USING (operation_id)
        LEFT JOIN cash_movements m ON m.id=c.cash_movement_id
        WHERE s.organization_id=$1 AND s.restaurant_id=$2 AND s.branch_id=$3
        """;
    private const string CompletedInWindow = "c.recorded_at_utc >= $4 AND c.recorded_at_utc < $5";
    private const string EvidenceRow = """
        jsonb_build_array(a.operation_id,s.order_id,s.settlement_id,s.tender,a.amount,btrim(s.currency),
          (extract(epoch from c.recorded_at_utc)*1000000)::bigint,c.cash_movement_id)::text
        """;

    internal static async Task<ManualRefundDaySummary?> ReadAsync(EndOfDayWindow window, NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var available = new NpgsqlCommand("SELECT to_regclass('manual_refund_completions') IS NOT NULL", connection, tx);
        if (await available.ExecuteScalarAsync(ct) is not true) return null;
        string sql = $"""
            SELECT COALESCE(sum(a.amount) FILTER(WHERE {CompletedInWindow} AND s.tender='cash'),0),
                COALESCE(sum(a.amount) FILTER(WHERE {CompletedInWindow} AND s.tender='promptpay_manual'),0),
                count(*) FILTER(WHERE {CompletedInWindow})::int,
                count(*) FILTER(WHERE p.started_at_utc<$5 AND c.operation_id IS NULL)::int,
                count(*) FILTER(WHERE {CompletedInWindow} AND (
                    (s.tender='cash' AND (m.id IS NULL OR m.cash_session_id IS DISTINCT FROM p.cash_session_id
                        OR m.movement_type<>'refund' OR m.amount<>a.amount OR m.order_id IS DISTINCT FROM s.order_id
                        OR m.occurred_at_utc<>c.recorded_at_utc OR m.recorded_by<>a.cashier_subject))
                    OR (s.tender='promptpay_manual' AND (c.cash_movement_id IS NOT NULL OR p.cash_session_id IS NOT NULL))
                ))::int
            {Scope}
            """;
        ManualRefundDaySummary result;
        await using (var query = Command(sql, window, connection, tx))
        await using (var rows = await query.ExecuteReaderAsync(ct))
        {
            await rows.ReadAsync(ct);
            result = new(rows.GetDecimal(0), rows.GetDecimal(1), rows.GetInt32(2), rows.GetInt32(3), rows.GetInt32(4), null);
        }
        if (result.CompletedCount == 0) return result;
        await using var evidence = Command($"SELECT {EvidenceRow} {Scope} AND {CompletedInWindow} ORDER BY a.operation_id LIMIT 10001",
            window, connection, tx);
        return result with { EvidenceVersion = await BoundedEvidenceHash.ReadAsync(evidence, "manual-refund-day-v1", ct) };
    }

    private static NpgsqlCommand Command(string sql, EndOfDayWindow window, NpgsqlConnection connection, NpgsqlTransaction tx)
    {
        var command = new NpgsqlCommand(sql, connection, tx);
        foreach (var value in new object[] { window.OrganizationId, window.RestaurantId, window.BranchId,
            window.FromUtc.ToUniversalTime(), window.ToUtc.ToUniversalTime() })
            command.Parameters.AddWithValue(value);
        return command;
    }
}
