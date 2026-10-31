using System.Text.Json;
using Npgsql;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Catalog.Application.Availability;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Domain.Availability;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class PostgresMenuAvailabilityRepository(NpgsqlDataSource source, TimeProvider clock) : IMenuAvailabilityRepository
{
    public async Task<IReadOnlyList<MenuAvailabilityItem>> ReadAsync(Guid organization, Guid branch, CancellationToken ct)
    {
        await using var sql = source.CreateCommand("SELECT product_id,name,available,availability_version FROM catalog_menu_items WHERE organization_id=@org AND branch_id=@branch ORDER BY name,product_id LIMIT 201");
        sql.Parameters.AddWithValue("org", organization); sql.Parameters.AddWithValue("branch", branch);
        await using var reader = await sql.ExecuteReaderAsync(ct);
        var items = new List<MenuAvailabilityItem>();
        while (await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2), reader.GetInt64(3)));
        return items;
    }
    public async Task<MenuAvailabilityItem?> ChangeAsync(Guid organization, Guid branch, Guid product,
        UpdateMenuAvailability input, MenuMutationContext actor, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        MenuAvailabilityItem previous;
        await using (var sql = Command(connection, tx, "SELECT name,available,availability_version FROM catalog_menu_items WHERE organization_id=@org AND branch_id=@branch AND product_id=@product FOR UPDATE", ("org", organization), ("branch", branch), ("product", product)))
        await using (var reader = await sql.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            previous = new(product, reader.GetString(0), reader.GetBoolean(1), reader.GetInt64(2));
        }
        var state = new MenuAvailabilityState(previous.Available, previous.Version);
        var next = state.Change(input.Available ?? throw new ArgumentException(), input.ExpectedVersion ?? throw new ArgumentException());
        if (ReferenceEquals(state, next)) { await tx.CommitAsync(ct); return previous; }
        var id = Guid.NewGuid(); var now = clock.GetUtcNow();
        await using (var sql = Command(connection, tx, "UPDATE catalog_menu_items SET available=@available,availability_version=@version WHERE organization_id=@org AND branch_id=@branch AND product_id=@product AND availability_version=@previous", ("available", next.Available), ("version", next.Version), ("org", organization), ("branch", branch), ("product", product), ("previous", state.Version)))
            if (await sql.ExecuteNonQueryAsync(ct) != 1) throw new MenuAvailabilityConflictException();
        await using (var sql = Command(connection, tx, "INSERT INTO catalog_menu_availability_audit(id,organization_id,branch_id,product_id,version,previous_available,available,actor_subject_id,correlation_id,occurred_at_utc) VALUES(@id,@org,@branch,@product,@version,@previous,@available,@actor,@correlation,@now)", ("id", id), ("org", organization), ("branch", branch), ("product", product), ("version", next.Version), ("previous", state.Available), ("available", next.Available), ("actor", actor.ActorSubjectId), ("correlation", actor.CorrelationId), ("now", now)))
            await sql.ExecuteNonQueryAsync(ct);
        var audit = new PlatformAuditEventV1(id, actor.CorrelationId, now, actor.ActorSubjectId, organization,
            "catalog.menu-availability.changed", "catalog-menu-item", product.ToString("D"), "succeeded");
        await using (var sql = Command(connection, tx, "INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES(@id,'catalog.audit.v1',1,'catalog-menu-availability',@product,@payload::jsonb,@correlation,@now)", ("id", id), ("product", product), ("payload", JsonSerializer.Serialize(audit)), ("correlation", actor.CorrelationId.ToString("D")), ("now", now)))
            await sql.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return previous with { Available = next.Available, Version = next.Version };
    }
    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction tx, string text, params (string Name, object Value)[] values)
    {
        var command = new NpgsqlCommand(text, connection, tx);
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
