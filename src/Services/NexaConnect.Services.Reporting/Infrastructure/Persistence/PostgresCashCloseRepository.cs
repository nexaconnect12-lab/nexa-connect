using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;
using Npgsql;
using NpgsqlTypes;

namespace NexaConnect.Services.Reporting.Infrastructure.Persistence;

public sealed class PostgresCashCloseRepository(NpgsqlDataSource dataSource) : ICashCloseRepository
{
    public async Task<bool> ProjectAsync(Guid eventId, CashCloseSnapshot snapshot, CancellationToken ct)
    {
        snapshot.Validate();
        string json = JsonSerializer.Serialize(snapshot);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        // Receipt and fact commit together; retries after a lost acknowledgement cannot reapply.
        await using (var q = new NpgsqlCommand("INSERT INTO cash_close_event_receipts(event_id,payload_hash) VALUES($1,$2) ON CONFLICT DO NOTHING", c, t))
        {
            q.Parameters.AddWithValue(eventId); q.Parameters.AddWithValue(hash);
            if (await q.ExecuteNonQueryAsync(ct) == 0)
            {
                await using var existing = new NpgsqlCommand("SELECT payload_hash FROM cash_close_event_receipts WHERE event_id=$1", c, t);
                existing.Parameters.AddWithValue(eventId);
                if ((string?)await existing.ExecuteScalarAsync(ct) != hash) throw new ArgumentException("Cash-close event identity conflict.");
                await t.CommitAsync(ct); return false;
            }
        }
        bool inserted;
        await using (var q = new NpgsqlCommand("""
            INSERT INTO cash_close_facts(session_id,organization_id,branch_id,store_id,closed_at_utc,snapshot,projected_at_utc)
            VALUES($1,$2,$3,$4,$5,$6::jsonb,clock_timestamp()) ON CONFLICT(session_id) DO NOTHING
            """, c, t))
        {
            q.Parameters.AddWithValue(snapshot.SessionId); q.Parameters.AddWithValue(snapshot.OrganizationId); q.Parameters.AddWithValue(snapshot.BranchId);
            q.Parameters.AddWithValue(snapshot.StoreId); q.Parameters.AddWithValue(snapshot.ClosedAtUtc); q.Parameters.AddWithValue(json);
            inserted = await q.ExecuteNonQueryAsync(ct) == 1;
        }
        bool replaced = false;
        if (!inserted)
        {
            await using var read = new NpgsqlCommand("SELECT snapshot::text FROM cash_close_facts WHERE session_id=$1 FOR UPDATE", c, t);
            read.Parameters.AddWithValue(snapshot.SessionId);
            var current = JsonSerializer.Deserialize<CashCloseSnapshot>((string)(await read.ExecuteScalarAsync(ct))!)!;
            if (snapshot.ShouldReplace(current))
            {
                await using var update = new NpgsqlCommand("UPDATE cash_close_facts SET snapshot=$1::jsonb,projected_at_utc=clock_timestamp() WHERE session_id=$2", c, t);
                update.Parameters.AddWithValue(json); update.Parameters.AddWithValue(snapshot.SessionId); await update.ExecuteNonQueryAsync(ct);
                replaced = true;
            }
        }
        await t.CommitAsync(ct);
        return inserted || replaced;
    }

    public async Task<IReadOnlyList<CashCloseRow>> ReadAsync(CashCloseQuery query, CancellationToken ct)
    {
        const string sql = """
            SELECT snapshot::text,projected_at_utc FROM cash_close_facts
            WHERE organization_id=$1 AND branch_id=$2 AND store_id=$3 AND closed_at_utc >= $4 AND closed_at_utc < $5
              AND ($6::timestamptz IS NULL OR (closed_at_utc,session_id)<($6,$7))
            ORDER BY closed_at_utc DESC,session_id DESC LIMIT $8
            """;
        await using var c = await dataSource.OpenConnectionAsync(ct); await using var q = new NpgsqlCommand(sql, c);
        q.Parameters.AddWithValue(query.OrganizationId); q.Parameters.AddWithValue(query.BranchId); q.Parameters.AddWithValue(query.StoreId);
        q.Parameters.AddWithValue(query.FromUtc); q.Parameters.AddWithValue(query.ToUtc);
        q.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)query.BeforeUtc ?? DBNull.Value });
        q.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)query.BeforeId ?? DBNull.Value });
        q.Parameters.AddWithValue(query.Limit);
        await using var r = await q.ExecuteReaderAsync(ct); var result = new List<CashCloseRow>();
        while (await r.ReadAsync(ct)) result.Add(new(JsonSerializer.Deserialize<CashCloseSnapshot>(r.GetString(0))!, r.GetFieldValue<DateTimeOffset>(1)));
        return result;
    }
}
