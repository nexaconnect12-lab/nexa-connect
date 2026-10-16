using System.Text;
using Npgsql;

namespace NexaConnect.Infrastructure.Persistence;

public sealed class LateFinancialWorkHeldException : Exception;

/// <summary>Append-only custody mechanics; the source owns payload normalization and window attribution.</summary>
public sealed class PostgresLateFinancialWork(NpgsqlDataSource source)
{
    public async Task<string> DispositionAsync(Guid organization,string eventType,string eventId,string fingerprint,CancellationToken ct)
    {
        // Earlier deployed financial schemas have no custody protocol and retain their original delivery behavior.
        await using(var available=source.CreateCommand("SELECT to_regclass('source_late_work')::text"))
            if(await available.ExecuteScalarAsync(ct) is null or DBNull)return "none";
        await using var q=source.CreateCommand("""
            SELECT fingerprint,
              EXISTS(SELECT 1 FROM source_late_work_links l JOIN source_day_barriers b ON b.organization_id=l.organization_id AND b.id=l.barrier_id
                WHERE l.organization_id=w.organization_id AND l.event_type=w.event_type AND l.event_id=w.event_id AND b.phase='committed'),
              EXISTS(SELECT 1 FROM source_late_work_links l JOIN source_day_barriers b ON b.organization_id=l.organization_id AND b.id=l.barrier_id
                WHERE l.organization_id=w.organization_id AND l.event_type=w.event_type AND l.event_id=w.event_id AND b.phase='armed')
            FROM source_late_work w WHERE organization_id=$1 AND event_type=$2 AND event_id=$3
            """);
        Add(q,organization,eventType,eventId);await using var rows=await q.ExecuteReaderAsync(ct);
        if(!await rows.ReadAsync(ct))return "none";
        if(rows.GetString(0)!=fingerprint)throw new SnapshotOperationConflictException();
        return rows.GetBoolean(1)?"committed":rows.GetBoolean(2)?"armed":"released";
    }
    public async Task<bool> CaptureAsync(Guid organization, Guid restaurant, Guid branch, string eventType, string eventId,
        string fingerprint, string normalizedPayload, Func<DateTimeOffset, DateTimeOffset, bool> affectsWindow, CancellationToken ct)
    {
        if (organization == Guid.Empty || restaurant == Guid.Empty || branch == Guid.Empty || eventId.Length is < 1 or > 128
            || eventType.Length is < 1 or > 128 || fingerprint.Length != 64 || Encoding.UTF8.GetByteCount(normalizedPayload) > 16 * 1024)
            throw new ArgumentException();
        await using var c = await source.OpenConnectionAsync(ct);
        var key = $"financial-revision:{restaurant:D}:{branch:D}";
        await Run(c, null, "SELECT pg_advisory_lock(hashtextextended($1,0))", ct, key);
        try
        {
            await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            var barriers = new List<(Guid Id, string Phase)>();
            await using (var q = new NpgsqlCommand("SELECT id,phase,from_utc,to_utc FROM source_day_barriers WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND phase IN('armed','committed') ORDER BY id FOR UPDATE", c, tx))
            {
                Add(q, organization, restaurant, branch);
                await using var rows = await q.ExecuteReaderAsync(ct);
                while (await rows.ReadAsync(ct))
                    if (affectsWindow(rows.GetFieldValue<DateTimeOffset>(2), rows.GetFieldValue<DateTimeOffset>(3))) barriers.Add((rows.GetGuid(0), rows.GetString(1)));
            }
            if (barriers.Count == 0) { await tx.CommitAsync(ct); return false; }
            await Run(c, tx, "SELECT pg_advisory_xact_lock(hashtextextended($1,0))", ct, "late-work:" + organization + ":" + eventType + ":" + eventId);
            await using (var q = new NpgsqlCommand("SELECT fingerprint FROM source_late_work WHERE organization_id=$1 AND event_type=$2 AND event_id=$3", c, tx))
            {
                Add(q, organization, eventType, eventId);
                if (await q.ExecuteScalarAsync(ct) is string existing && existing != fingerprint) throw new SnapshotOperationConflictException();
            }
            await Run(c, tx, "INSERT INTO source_late_work(organization_id,event_type,event_id,fingerprint,payload) VALUES($1,$2,$3,$4,$5::jsonb) ON CONFLICT DO NOTHING", ct,
                organization, eventType, eventId, fingerprint, normalizedPayload);
            foreach (var barrier in barriers)
                await Run(c, tx, "INSERT INTO source_late_work_links(organization_id,event_type,event_id,barrier_id) VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING", ct,
                    organization, eventType, eventId, barrier.Id);
            await tx.CommitAsync(ct);
            // Armed-only work remains in its delivery channel. Abort must allow normal processing to resume.
            return barriers.Any(b => b.Phase == "committed");
        }
        finally { await Run(c, null, "SELECT pg_advisory_unlock(hashtextextended($1,0))", CancellationToken.None, key); }
    }

    private static void Add(NpgsqlCommand q, params object[] values) { foreach (var value in values) q.Parameters.AddWithValue(value); }
    private static async Task Run(NpgsqlConnection c, NpgsqlTransaction? tx, string sql, CancellationToken ct, params object[] values)
    { await using var q = new NpgsqlCommand(sql, c, tx) { CommandTimeout = 10 }; Add(q, values); await q.ExecuteNonQueryAsync(ct); }
}
