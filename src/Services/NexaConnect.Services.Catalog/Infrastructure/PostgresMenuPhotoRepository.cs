using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.MenuPhotos;
using NexaConnect.Services.Catalog.Domain.Photos;

namespace NexaConnect.Services.Catalog.Infrastructure;
public sealed class PostgresMenuPhotoRepository(NpgsqlDataSource source, TimeProvider clock) : IMenuPhotoRepository
{
    public async Task<MenuPhotoSelection?> ReadAsync(Guid org, Guid branch, Guid product, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Read(c, null, org, branch, product, ct);
    }
    private static async Task<MenuPhotoSelection?> Read(NpgsqlConnection c, NpgsqlTransaction? tx, Guid org, Guid branch, Guid product, CancellationToken ct)
    {
        await using var sql = Command(c, tx, "SELECT p.asset_id,COALESCE(p.version,0) FROM catalog_menu_items m LEFT JOIN catalog_menu_photos p USING(organization_id,branch_id,product_id) WHERE m.organization_id=@org AND m.branch_id=@branch AND m.product_id=@product", ("org", org), ("branch", branch), ("product", product));
        await using var r = await sql.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(org, branch, product, r.IsDBNull(0) ? null : r.GetGuid(0), r.GetInt64(1)) : null;
    }
    public async Task<MenuPhotoSelection> ChangeAsync(Guid org, Guid branch, Guid product, Func<MenuPhotoSelection, MenuPhotoChange> decide, MenuMutationContext actor, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        // Lock the owning menu row, including the first image assignment, in the same transaction.
        await using (var item = Command(c, tx, "SELECT product_id FROM catalog_menu_items WHERE organization_id=@org AND branch_id=@branch AND product_id=@product FOR UPDATE", ("org", org), ("branch", branch), ("product", product)))
            if (await item.ExecuteScalarAsync(ct) is null) throw new KeyNotFoundException();
        var previous = await Read(c, tx, org, branch, product, ct) ?? throw new KeyNotFoundException();
        var change = decide(previous);
        if (!change.Changed) { await tx.CommitAsync(ct); return previous; }
        var now = clock.GetUtcNow(); var id = Guid.NewGuid();
        await using (var save = Command(c, tx, "INSERT INTO catalog_menu_photos(organization_id,branch_id,product_id,asset_id,version) VALUES(@org,@branch,@product,@asset,@version) ON CONFLICT(organization_id,branch_id,product_id) DO UPDATE SET asset_id=EXCLUDED.asset_id,version=EXCLUDED.version", ("org", org), ("branch", branch), ("product", product), ("asset", change.AssetId), ("version", change.Version))) await save.ExecuteNonQueryAsync(ct);
        await using (var audit = Command(c, tx, "INSERT INTO catalog_menu_photo_audit(id,organization_id,branch_id,product_id,version,previous_asset_id,asset_id,actor_subject_id,correlation_id,occurred_at_utc) VALUES(@id,@org,@branch,@product,@version,@previous,@asset,@actor,@correlation,@now)", ("id", id), ("org", org), ("branch", branch), ("product", product), ("version", change.Version), ("previous", previous.AssetId), ("asset", change.AssetId), ("actor", actor.ActorSubjectId), ("correlation", actor.CorrelationId), ("now", now))) await audit.ExecuteNonQueryAsync(ct);
        var entry = new PlatformAuditEventV1(id, actor.CorrelationId, now, actor.ActorSubjectId, org, "catalog.menu-photo.changed", "catalog-menu-photo", product.ToString("D"), "succeeded");
        await using (var enqueue = Command(c, tx, "INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES(@id,'catalog.audit.v1',1,'catalog-menu-photo',@product,@payload::jsonb,@correlation,@now)", ("id", id), ("product", product), ("payload", JsonSerializer.Serialize(entry)), ("correlation", actor.CorrelationId.ToString("D")), ("now", now))) await enqueue.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct); return new(org, branch, product, change.AssetId, change.Version);
    }
    private static NpgsqlCommand Command(NpgsqlConnection c, NpgsqlTransaction? tx, string text, params (string, object?)[] parameters)
    {
        var sql = new NpgsqlCommand(text, c, tx);
        foreach (var (name, value) in parameters)
            if (value is null) sql.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid) { Value = DBNull.Value }); else sql.Parameters.AddWithValue(name, value);
        return sql;
    }
}
