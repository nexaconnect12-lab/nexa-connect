using System.Data;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;
using Npgsql;

namespace NexaConnect.Services.Reporting.Infrastructure.Persistence;

public sealed class PostgresCashCorrectionFactRepository(NpgsqlDataSource source) : ICashCorrectionFactRepository
{
    public async Task<bool> ProjectAsync(CashCorrectionFact fact, CancellationToken ct)
    {
        fact.Validate();
        string hash = FinancialCompleteness.Hash(fact);
        try
        {
            await using var c = await source.OpenConnectionAsync(ct);
            await using var tx = await c.BeginTransactionAsync(ct);
            await using var receipt = new NpgsqlCommand("INSERT INTO cash_correction_event_receipts(event_id,payload_hash) VALUES($1,$2) ON CONFLICT DO NOTHING", c, tx);
            Add(receipt, fact.EventId, hash);
            if (await receipt.ExecuteNonQueryAsync(ct) == 0)
            {
                await using var existing = new NpgsqlCommand("SELECT payload_hash FROM cash_correction_event_receipts WHERE event_id=$1", c, tx);
                Add(existing, fact.EventId);
                if ((string?)await existing.ExecuteScalarAsync(ct) != hash) throw new ArgumentException("Correction event identity conflict.");
                await tx.CommitAsync(ct); return false;
            }
            await using var insert = new NpgsqlCommand("""
                INSERT INTO cash_correction_facts(event_id,organization_id,restaurant_id,branch_id,correction_id,work_id,tender_id,order_id,posted_at_utc,adjustment,payload)
                VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11::jsonb)
                """, c, tx);
            Add(insert, fact.EventId, fact.OrganizationId, fact.RestaurantId, fact.BranchId, fact.CorrectionId,
                fact.WorkId, fact.TenderId, fact.OrderId, fact.PostedAtUtc, fact.Adjustment, JsonSerializer.Serialize(fact));
            await insert.ExecuteNonQueryAsync(ct);
            await using var checkpoint = new NpgsqlCommand("""
                INSERT INTO projection_checkpoints(projector_name,source_stream,position,last_event_id,last_event_occurred_at_utc,updated_at_utc)
                VALUES('pos-cash-correction','pos.late-cash-correction-posted.v1',1,$1,$2,clock_timestamp())
                ON CONFLICT(projector_name,source_stream) DO UPDATE SET position=projection_checkpoints.position+1,
                last_event_id=EXCLUDED.last_event_id,last_event_occurred_at_utc=EXCLUDED.last_event_occurred_at_utc,updated_at_utc=clock_timestamp()
                """, c, tx);
            Add(checkpoint, fact.EventId, fact.PostedAtUtc); await checkpoint.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct); return true;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new ArgumentException("Correction financial identity conflict.", e); }
    }

    public async Task<CashCorrectionReport> CompareAsync(CashCorrectionManifest manifest, CancellationToken ct)
    {
        var range = new ReportingRange(manifest.OrganizationId, manifest.BranchId, manifest.FromUtc, manifest.ToUtc);
        CashCorrectionReporting.ValidateManifest(manifest, range);
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var facts = new Dictionary<Guid, (CashCorrectionFact Fact, string? Hash)>();
        await using (var q = new NpgsqlCommand("""
            SELECT f.payload::text,r.payload_hash FROM cash_correction_facts f
            LEFT JOIN cash_correction_event_receipts r ON r.event_id=f.event_id
            WHERE (f.organization_id=$1 AND f.branch_id=$2 AND f.posted_at_utc >= $3 AND f.posted_at_utc < $4)
               OR f.event_id=ANY($5) ORDER BY f.event_id LIMIT 2001
            """, c, tx))
        {
            Add(q, range.OrganizationId, range.BranchId, range.FromUtc, range.ToUtc, manifest.Events.Select(x => x.EventId).ToArray());
            await using var r = await q.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var fact = JsonSerializer.Deserialize<CashCorrectionFact>(r.GetString(0)) ?? throw new InvalidOperationException();
                fact.Validate(); facts.Add(fact.EventId, (fact, r.IsDBNull(1) ? null : r.GetString(1)));
            }
        }
        if (facts.Count > 2000) throw new ArgumentException("Correction projection window exceeds the bounded inventory.");
        var items = new List<CashCorrectionReportItem>(); int matched = 0, missing = 0, conflicting = 0;
        foreach (var e in manifest.Events.OrderBy(x => x.EventId))
        {
            var expected = CashCorrectionReporting.Translate(e);
            string status;
            if (!facts.TryGetValue(e.EventId, out var actual)) { status = "missing"; missing++; }
            else if (actual.Fact != expected || actual.Hash != FinancialCompleteness.Hash(expected)) { status = "conflicting"; conflicting++; }
            else { status = "matched"; matched++; }
            items.Add(Item(expected, status));
        }
        var identities = manifest.Events.Select(x => x.EventId).ToHashSet();
        var unexpected = facts.Values.Where(x => !identities.Contains(x.Fact.EventId)).ToArray();
        if (manifest.Events.Count + unexpected.Length > 2000) throw new ArgumentException("Correction report window exceeds the bounded inventory.");
        items.AddRange(unexpected.Select(x => Item(x.Fact, "unexpected")));
        // A conflicting fact from another tenant is counted, but never returned or included in this tenant's totals.
        decimal projected = facts.Values.Where(x => x.Fact.OrganizationId == range.OrganizationId && x.Fact.BranchId == range.BranchId
            && x.Fact.PostedAtUtc >= range.FromUtc && x.Fact.PostedAtUtc < range.ToUtc).Sum(x => x.Fact.Adjustment);
        await tx.CommitAsync(ct);
        return new(range.OrganizationId, range.BranchId, range.FromUtc, range.ToUtc, manifest.ObservedAtUtc,
            DateTimeOffset.UtcNow, FinancialCompleteness.Hash(manifest.Events.OrderBy(x => x.EventId).Select(CashCorrectionReporting.Translate).ToArray()),
            missing + conflicting + unexpected.Length == 0 ? "matched" : "gaps", manifest.Events.Count, matched, missing, conflicting,
            unexpected.Length, manifest.Events.Sum(x => x.CashVarianceAdjustment), projected, items);
    }
    private static CashCorrectionReportItem Item(CashCorrectionFact f, string status) => new(f.EventId, f.CorrectionId,
        f.OriginalSettlementId, f.WorkId, f.OrderId, f.TenderId, f.DrawerId, f.PostingDate, f.PostedAtUtc, f.Currency, f.Adjustment, status);
    private static void Add(NpgsqlCommand q, params object[] values) { foreach (var value in values) q.Parameters.AddWithValue(value); }
}
