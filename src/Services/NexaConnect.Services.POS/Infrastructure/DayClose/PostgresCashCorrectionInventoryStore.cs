using System.Data;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.POS.Application.DayClose;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.DayClose;

public sealed class PostgresCashCorrectionInventoryStore(NpgsqlDataSource source) : ICashCorrectionInventoryStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<CashCorrectionManifest> ReadAsync(Guid organization, Guid restaurant, Guid branch, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        (await ReadRetainedAsync(organization, restaurant, branch, from, to, ct)).Manifest;

    public async Task<(CashCorrectionManifest Manifest, IReadOnlyDictionary<Guid,string> Payloads)> ReadRetainedAsync(Guid organization, Guid restaurant, Guid branch, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        CashCorrectionInventory.Validate(organization, branch, from, to);
        if (restaurant == Guid.Empty) throw new ArgumentException();
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var q = new NpgsqlCommand("""
            SELECT l.receipt::text,l.adjustment,l.posted_at_utc,l.posting_date,l.posting_from_utc,l.posting_to_utc,
                   o.id,o.payload::text,o.correlation_id,o.occurred_at_utc,o.contract_version,o.aggregate_id
            FROM late_cash_corrections l LEFT JOIN outbox_messages o
              ON o.id=(l.receipt->>'eventId')::uuid AND o.event_type='pos.late-cash-correction-posted.v1'
            WHERE l.organization_id=$1 AND l.restaurant_id=$2 AND l.branch_id=$3 AND l.posted_at_utc >= $4 AND l.posted_at_utc < $5
            ORDER BY l.posted_at_utc,l.id LIMIT 1001
            """, c, tx);
        foreach (var value in new object[] { organization, restaurant, branch, from, to }) q.Parameters.AddWithValue(value);
        var events = new List<PosLateCashCorrectionPostedV1>();
        var payloads = new Dictionary<Guid,string>();
        await using (var r = await q.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                if (events.Count == 1000) throw new ArgumentException("Correction inventory exceeds 1000; narrow the range.");
                if (r.IsDBNull(6) || r.IsDBNull(7) || r.IsDBNull(8)) throw new InvalidOperationException("Correction publication evidence is missing.");
                var receipt = JsonSerializer.Deserialize<LateCashCorrectionReceipt>(r.GetString(0), Json) ?? throw new JsonException();
                var e = JsonSerializer.Deserialize<PosLateCashCorrectionPostedV1>(r.GetString(7), Json) ?? throw new JsonException();
                if (!Guid.TryParse(r.GetString(8), out var correlation) || correlation == Guid.Empty) throw new InvalidOperationException();
                var expected = new PosLateCashCorrectionPostedV1(receipt.EventId, correlation, receipt.PostedAtUtc,
                    organization, restaurant, branch, receipt.CorrectionId, receipt.WorkId, receipt.Scope.SettlementId,
                    receipt.OrderId, receipt.TenderId, receipt.DrawerId, receipt.ReviewVersion, receipt.PostingDate,
                    r.GetFieldValue<DateTimeOffset>(4), r.GetFieldValue<DateTimeOffset>(5), receipt.Currency, receipt.Adjustment);
                // SQL timestamps are microsecond precision; retained JSON and the original event retain exact precision.
                bool Same(DateTimeOffset a, DateTimeOffset b) => a.UtcTicks / 10 == b.UtcTicks / 10;
                if (e != expected || e.EventId != r.GetGuid(6) || e.CorrectionId != r.GetGuid(11) || r.GetInt32(10) != 1
                    || e.CashVarianceAdjustment != r.GetDecimal(1) || e.PostingDate != r.GetFieldValue<DateOnly>(3)
                    || !Same(e.OccurredAtUtc, r.GetFieldValue<DateTimeOffset>(2)) || !Same(e.OccurredAtUtc, r.GetFieldValue<DateTimeOffset>(9))
                    || receipt.Scope.Window.OrganizationId != organization || receipt.Scope.Window.RestaurantId != restaurant
                    || receipt.Scope.Window.BranchId != branch || e.Currency != "THB" || e.CashVarianceAdjustment >= 0
                    || e.OccurredAtUtc < e.PostingFromUtc || e.OccurredAtUtc >= e.PostingToUtc)
                    throw new InvalidOperationException("Correction publication evidence conflicts with its ledger.");
                events.Add(e);
                payloads.Add(e.EventId, r.GetString(7));
            }
        }
        await tx.CommitAsync(ct);
        return (new(organization, restaurant, branch, from, to, DateTimeOffset.UtcNow, events), payloads);
    }
}
