using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using Npgsql;

namespace NexaConnect.Infrastructure.Persistence;

/// <summary>Low-level immutable snapshot retention; selection, authorization and readiness belong to each service.</summary>
public sealed class PostgresSnapshotRetention<T>(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<SourceCutoff<T>> CaptureAsync(SourceCutoffCommand command, string actor,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<SourceCutoff<T>>> capture, CancellationToken ct)
    {
        if (command.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(actor) || actor.Length > 128 || actor.Any(char.IsControl))
            throw new ArgumentException("Invalid snapshot operation.");
        // Session locks precede the repeatable-read snapshot, avoiding stale snapshots after lock contention.
        await using var connection = await source.OpenConnectionAsync(ct);
        await Lock(connection, command.Window.OrganizationId + ":" + command.OperationId, ct);
        try
        {
            await Lock(connection, Scope(command.Window), ct);
            try
            {
                await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                string fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { command, actor }, Json)));
                await using (var existing = new NpgsqlCommand("SELECT fingerprint,payload::text FROM source_day_cutoffs WHERE organization_id=$1 AND operation_id=$2", connection, tx))
                {
                    existing.Parameters.AddWithValue(command.Window.OrganizationId); existing.Parameters.AddWithValue(command.OperationId);
                    await using var rows = await existing.ExecuteReaderAsync(ct);
                    if (await rows.ReadAsync(ct))
                    {
                        if (rows.GetString(0) != fingerprint) throw new SnapshotOperationConflictException();
                        return JsonSerializer.Deserialize<SourceCutoff<T>>(rows.GetString(1), Json)!;
                    }
                }
                var value = await capture(connection, tx, ct);
                await using var generation = new NpgsqlCommand("SELECT COALESCE(max(generation),0)+1 FROM source_day_cutoffs WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND from_utc=$4 AND to_utc=$5", connection, tx);
                AddWindow(generation, command.Window);
                value = value with { ManifestId = Guid.NewGuid(), OperationId = command.OperationId,
                    Generation = (long)(await generation.ExecuteScalarAsync(ct))!, Window = command.Window };
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, Json);
                if (payload.Length > 16 * 1024 * 1024) throw new InvalidOperationException("Snapshot exceeds retention bound.");
                await using var insert = new NpgsqlCommand("INSERT INTO source_day_cutoffs(organization_id,restaurant_id,branch_id,from_utc,to_utc,id,operation_id,generation,fingerprint,actor,payload) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11::jsonb)", connection, tx);
                AddWindow(insert, command.Window);
                insert.Parameters.AddWithValue(value.ManifestId); insert.Parameters.AddWithValue(command.OperationId);
                insert.Parameters.AddWithValue(value.Generation); insert.Parameters.AddWithValue(fingerprint);
                insert.Parameters.AddWithValue(actor); insert.Parameters.AddWithValue(System.Text.Encoding.UTF8.GetString(payload));
                await insert.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct); return value;
            }
            finally { await Unlock(connection, Scope(command.Window)); }
        }
        finally { await Unlock(connection, command.Window.OrganizationId + ":" + command.OperationId); }
    }
    public async Task<SourceCutoff<T>?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct)
    {
        await using var query = source.CreateCommand("SELECT payload::text FROM source_day_cutoffs WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND from_utc=$4 AND to_utc=$5 AND id=$6");
        AddWindow(query, window); query.Parameters.AddWithValue(id);
        return await query.ExecuteScalarAsync(ct) is string payload ? JsonSerializer.Deserialize<SourceCutoff<T>>(payload, Json) : null;
    }
    private static string Scope(EndOfDayWindow w) => $"cutoff:{w.OrganizationId:D}:{w.RestaurantId:D}:{w.BranchId:D}:{w.FromUtc.UtcTicks}:{w.ToUtc.UtcTicks}";
    private static async Task Lock(NpgsqlConnection c, string key, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended($1,0))", c);
        query.Parameters.AddWithValue(key); await query.ExecuteNonQueryAsync(ct);
    }
    private static async Task Unlock(NpgsqlConnection c, string key)
    {
        await using var query = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended($1,0))", c) { CommandTimeout = 5 };
        query.Parameters.AddWithValue(key); await query.ExecuteNonQueryAsync(CancellationToken.None);
    }
    private static void AddWindow(NpgsqlCommand query, EndOfDayWindow w)
    {
        query.Parameters.AddWithValue(w.OrganizationId); query.Parameters.AddWithValue(w.RestaurantId);
        query.Parameters.AddWithValue(w.BranchId); query.Parameters.AddWithValue(w.FromUtc.ToUniversalTime()); query.Parameters.AddWithValue(w.ToUtc.ToUniversalTime());
    }
}
public sealed class SnapshotOperationConflictException : Exception;
