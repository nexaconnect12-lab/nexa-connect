using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Restaurant.Application.Qr;
using NexaConnect.Services.Restaurant.Domain.Qr;
using Npgsql;

namespace NexaConnect.Services.Restaurant.Infrastructure.Persistence;

public sealed class PostgresQrEntryRepository(NpgsqlDataSource source) : IQrEntryRepository
{
    private const string ScopeSql = "SELECT r.organization_id,r.id,b.id,r.name,b.name,b.currency FROM branches b JOIN restaurants r ON r.id=b.restaurant_id WHERE r.organization_id=$1 AND b.id=$2 AND r.status='active' AND b.status='active'";
    public async Task<QrScope?> ScopeAsync(Guid org, Guid branch, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand(ScopeSql); cmd.Parameters.AddWithValue(org); cmd.Parameters.AddWithValue(branch);
        await using var reader = await cmd.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? Scope(reader) : null;
    }
    public async Task<IReadOnlyList<QrEntry>> ListAsync(QrScope scope, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("SELECT id,table_code,secret_hash,version,active,expires_at_utc FROM restaurant_qr_entries WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 ORDER BY table_code LIMIT 200;");
        Owner(cmd, scope); var entries = new List<QrEntry>(); await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) entries.Add(Entry(r, scope)); return entries;
    }
    public async Task<QrEntry?> GetAsync(QrScope scope, Guid id, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("SELECT id,table_code,secret_hash,version,active,expires_at_utc FROM restaurant_qr_entries WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND id=$4;");
        Owner(cmd, scope); cmd.Parameters.AddWithValue(id); await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Entry(r, scope) : null;
    }
    public async Task SaveAsync(QrEntry entry, long expectedVersion, string actor, DateTimeOffset now, CancellationToken ct)
    {
        await using var cn = await source.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        // One branch lock orders QR writes and admission budgets; live hierarchy is checked again at commit.
        await using (var branch = new NpgsqlCommand(ScopeSql + " FOR UPDATE OF b;", cn, tx))
        {
            branch.Parameters.AddWithValue(entry.Scope.OrganizationId); branch.Parameters.AddWithValue(entry.Scope.BranchId);
            await using var r = await branch.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct) || r.GetGuid(1) != entry.Scope.RestaurantId) throw new QrConflictException();
        }
        if (expectedVersion == 0)
        {
            await using var budget = new NpgsqlCommand("SELECT count(*) FROM restaurant_qr_entries WHERE organization_id=$1 AND branch_id=$2;", cn, tx);
            budget.Parameters.AddWithValue(entry.Scope.OrganizationId); budget.Parameters.AddWithValue(entry.Scope.BranchId);
            QrGuestAdmission.RequireEntryCapacity((long)(await budget.ExecuteScalarAsync(ct))!);
        }
        string sql = expectedVersion == 0
            ? "INSERT INTO restaurant_qr_entries(id,organization_id,restaurant_id,branch_id,table_code,secret_hash,version,active,expires_at_utc) VALUES($4,$1,$2,$3,$5,$6,$7,$8,$9) ON CONFLICT DO NOTHING;"
            : "UPDATE restaurant_qr_entries SET secret_hash=$6,version=$7,active=$8,expires_at_utc=$9 WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND id=$4 AND table_code=$5 AND version=$10;";
        await using var cmd = new NpgsqlCommand(sql, cn, tx); Owner(cmd, entry.Scope);
        cmd.Parameters.AddWithValue(entry.Id); cmd.Parameters.AddWithValue(entry.TableCode); cmd.Parameters.AddWithValue(entry.SecretHash);
        cmd.Parameters.AddWithValue(entry.Version); cmd.Parameters.AddWithValue(entry.Active); cmd.Parameters.AddWithValue(entry.ExpiresAtUtc);
        if (expectedVersion != 0) cmd.Parameters.AddWithValue(expectedVersion);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new QrConflictException();
        string action = expectedVersion == 0 ? "qr.created" : entry.Active ? "qr.rotated" : "qr.revoked";
        Guid auditId = Guid.NewGuid();
        await using var audit = new NpgsqlCommand("INSERT INTO restaurant_qr_audit(id,organization_id,branch_id,entry_id,entry_version,action,actor_subject_id,occurred_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7,$8);", cn, tx);
        audit.Parameters.AddWithValue(auditId); audit.Parameters.AddWithValue(entry.Scope.OrganizationId); audit.Parameters.AddWithValue(entry.Scope.BranchId);
        audit.Parameters.AddWithValue(entry.Id); audit.Parameters.AddWithValue(entry.Version); audit.Parameters.AddWithValue(action);
        audit.Parameters.AddWithValue(actor); audit.Parameters.AddWithValue(now); await audit.ExecuteNonQueryAsync(ct);
        await TransactionalAuditOutbox.EnqueueAuditAsync(cn, tx, new(auditId, Guid.NewGuid(), now, actor,
            entry.Scope.OrganizationId, action, "table-qr-entry", entry.Id.ToString("D"), "succeeded"), "restaurant.audit.v1", ct);
        await tx.CommitAsync(ct);
    }
    private const string GuestJoin = " FROM restaurant_qr_entries e JOIN branches b ON b.id=e.branch_id AND b.restaurant_id=e.restaurant_id JOIN restaurants r ON r.id=e.restaurant_id AND r.organization_id=e.organization_id ";
    private const string Active = " e.active AND e.expires_at_utc>$2 AND b.status='active' AND r.status='active' ";
    public async Task<QrGuestContext?> AdmitAsync(string entryHash, string sessionHash, Guid visitor, DateTimeOffset now, CancellationToken ct)
    {
        await using var cn = await source.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        QrScope scope; Guid entryId; long version; string table; DateTimeOffset expiry;
        await using (var lookup = new NpgsqlCommand("SELECT r.organization_id,r.id,b.id,r.name,b.name,b.currency,e.id,e.version,e.table_code,e.expires_at_utc" + GuestJoin + "WHERE e.secret_hash=$1 AND" + Active + "FOR UPDATE OF b,e;", cn, tx))
        {
            lookup.Parameters.AddWithValue(entryHash); lookup.Parameters.AddWithValue(now); await using var r = await lookup.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            scope = Scope(r); entryId = r.GetGuid(6); version = r.GetInt64(7); table = r.GetString(8);
            expiry = QrGuestAdmission.Expiry(now, r.GetFieldValue<DateTimeOffset>(9));
        }
        await using (var prune = new NpgsqlCommand("DELETE FROM restaurant_qr_sessions s USING restaurant_qr_entries e WHERE s.entry_id=e.id AND e.organization_id=$1 AND e.branch_id=$2 AND (s.expires_at_utc<=$3 OR s.entry_version<>e.version OR (s.ended AND s.created_at_utc<=$4));", cn, tx))
        {
            prune.Parameters.AddWithValue(scope.OrganizationId); prune.Parameters.AddWithValue(scope.BranchId); prune.Parameters.AddWithValue(now); prune.Parameters.AddWithValue(now.AddMinutes(-1)); await prune.ExecuteNonQueryAsync(ct);
        }
        await using (var budget = new NpgsqlCommand("SELECT count(*) FILTER(WHERE s.entry_id=$3 AND NOT s.ended),count(*) FILTER(WHERE NOT s.ended),count(*) FILTER(WHERE s.entry_id=$3 AND s.created_at_utc>$4) FROM restaurant_qr_sessions s JOIN restaurant_qr_entries e ON e.id=s.entry_id WHERE e.organization_id=$1 AND e.branch_id=$2;", cn, tx))
        {
            budget.Parameters.AddWithValue(scope.OrganizationId); budget.Parameters.AddWithValue(scope.BranchId); budget.Parameters.AddWithValue(entryId); budget.Parameters.AddWithValue(now.AddMinutes(-1));
            await using var r = await budget.ExecuteReaderAsync(ct); await r.ReadAsync(ct);
            QrGuestAdmission.RequireCapacity(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2));
        }
        await using var insert = new NpgsqlCommand("INSERT INTO restaurant_qr_sessions(visitor_id,entry_id,entry_version,secret_hash,created_at_utc,expires_at_utc) VALUES($1,$2,$3,$4,$5,$6);", cn, tx);
        insert.Parameters.AddWithValue(visitor); insert.Parameters.AddWithValue(entryId); insert.Parameters.AddWithValue(version);
        insert.Parameters.AddWithValue(sessionHash); insert.Parameters.AddWithValue(now); insert.Parameters.AddWithValue(expiry);
        await insert.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        return new(visitor, entryId, scope.OrganizationId, scope.RestaurantId, scope.BranchId, scope.RestaurantName, scope.BranchName, table, scope.Currency, expiry);
    }
    public async Task<QrGuestContext?> ContextAsync(string sessionHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("SELECT r.organization_id,r.id,b.id,r.name,b.name,b.currency,e.id,e.table_code,s.visitor_id,s.expires_at_utc" + GuestJoin + "JOIN restaurant_qr_sessions s ON s.entry_id=e.id WHERE s.secret_hash=$1 AND NOT s.ended AND s.entry_version=e.version AND s.expires_at_utc>$2 AND" + Active);
        cmd.Parameters.AddWithValue(sessionHash); cmd.Parameters.AddWithValue(now); await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(r.GetGuid(8), r.GetGuid(6), r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3), r.GetString(4), r.GetString(7), r.GetString(5), r.GetFieldValue<DateTimeOffset>(9)) : null;
    }
    public async Task EndAsync(string sessionHash, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("UPDATE restaurant_qr_sessions SET ended=true WHERE secret_hash=$1;"); cmd.Parameters.AddWithValue(sessionHash); await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<QrGuestContext?> VisitorAsync(Guid organization,Guid branch,Guid visitor,Guid entry,DateTimeOffset now,CancellationToken ct)
    {
        await using var cmd=source.CreateCommand("SELECT r.organization_id,r.id,b.id,r.name,b.name,b.currency,e.id,e.table_code,s.visitor_id,s.expires_at_utc"+GuestJoin+"JOIN restaurant_qr_sessions s ON s.entry_id=e.id WHERE r.organization_id=$1 AND e.branch_id=$3 AND s.visitor_id=$4 AND e.id=$5 AND NOT s.ended AND s.entry_version=e.version AND s.expires_at_utc>$2 AND"+Active);
        cmd.Parameters.AddWithValue(organization);cmd.Parameters.AddWithValue(now);cmd.Parameters.AddWithValue(branch);cmd.Parameters.AddWithValue(visitor);cmd.Parameters.AddWithValue(entry);
        await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(r.GetGuid(8),r.GetGuid(6),r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),r.GetString(7),r.GetString(5),r.GetFieldValue<DateTimeOffset>(9)):null;
    }
    private static void Owner(NpgsqlCommand cmd, QrScope s)
    { cmd.Parameters.AddWithValue(s.OrganizationId); cmd.Parameters.AddWithValue(s.RestaurantId); cmd.Parameters.AddWithValue(s.BranchId); }
    private static QrScope Scope(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3), r.GetString(4), r.GetString(5));
    private static QrEntry Entry(NpgsqlDataReader r, QrScope scope) => new(r.GetGuid(0), scope, r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetBoolean(4), r.GetFieldValue<DateTimeOffset>(5));
}
