using Npgsql;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Contracts.IntegrationEvents;
using System.Text.Json;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class PostgresMenuCatalog(NpgsqlDataSource dataSource) : IMenuCatalog
{
    public async Task<bool> ProductExistsAsync(Guid organizationId, Guid productId, CancellationToken cancellationToken)
    {
        using var command = dataSource.CreateCommand("SELECT EXISTS(SELECT 1 FROM catalog_menu_items WHERE organization_id=@organization AND product_id=@product)");
        command.Parameters.AddWithValue("organization", organizationId);
        command.Parameters.AddWithValue("product", productId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
    public IReadOnlyCollection<MenuItem> GetForBranch(Guid branchId)
    {
        using var command = dataSource.CreateCommand("SELECT product_id,name,unit_price,currency,preparation_station,available FROM catalog_menu_items WHERE branch_id=@branch ORDER BY name");
        command.Parameters.AddWithValue("branch", branchId);
        using var reader = command.ExecuteReader(); var result = new List<MenuItem>();
        while (reader.Read()) result.Add(new MenuItem(reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)));
        return result;
    }

    public IReadOnlyCollection<MenuItem> GetForOrganizationBranch(Guid organizationId, Guid branchId)
    {
        using var command = dataSource.CreateCommand("SELECT product_id,name,unit_price,currency,preparation_station,available FROM catalog_menu_items WHERE organization_id=@organization AND branch_id=@branch ORDER BY name");
        command.Parameters.AddWithValue("organization", organizationId);
        command.Parameters.AddWithValue("branch", branchId);
        using var reader = command.ExecuteReader(); var result = new List<MenuItem>();
        while (reader.Read()) result.Add(new MenuItem(reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)));
        return result;
    }

    public MenuItem Add(Guid branchId, CreateMenuItem command, MenuMutationContext? context = null)
        => AddForOrganizationBranch(Guid.Empty, branchId, command, context);

    public MenuItem AddForOrganizationBranch(Guid organizationId, Guid branchId, CreateMenuItem command, MenuMutationContext? context = null)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || command.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || command.UnitPrice < 0)
            throw new ArgumentException("A valid organization, branch, product, name, and non-negative price are required.");
        var item = new MenuItem(command.ProductId, command.Name.Trim(), command.UnitPrice, command.Currency.Trim().ToUpperInvariant(), command.PreparationStation.Trim(), true);
        context ??= new MenuMutationContext("trusted-workload", Guid.NewGuid());
        if (string.IsNullOrWhiteSpace(context.ActorSubjectId) || context.CorrelationId == Guid.Empty)
            throw new ArgumentException("A valid mutation actor and correlation identifier are required.");
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        var changed = new CatalogMenuItemChangedV1(Guid.NewGuid(), context.CorrelationId, occurredAt, organizationId, branchId,
            item.ProductId, item.Name, item.UnitPrice, item.Currency, item.PreparationStation, item.Available);
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), context.CorrelationId, occurredAt, context.ActorSubjectId.Trim(), organizationId,
            "catalog.menu-item.changed", "catalog-menu-item", item.ProductId.ToString("D"), "succeeded");
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction();
        using var sql = new NpgsqlCommand("INSERT INTO catalog_menu_items (organization_id,branch_id,product_id,name,unit_price,currency,preparation_station,available) VALUES (@organization,@branch,@product,@name,@price,@currency,@station,true) ON CONFLICT (organization_id,branch_id,product_id) DO UPDATE SET name=EXCLUDED.name,unit_price=EXCLUDED.unit_price,currency=EXCLUDED.currency,preparation_station=EXCLUDED.preparation_station,available=true", connection, transaction);
        sql.Parameters.AddWithValue("organization", organizationId); sql.Parameters.AddWithValue("branch", branchId); sql.Parameters.AddWithValue("product", item.ProductId); sql.Parameters.AddWithValue("name", item.Name); sql.Parameters.AddWithValue("price", item.UnitPrice); sql.Parameters.AddWithValue("currency", item.Currency); sql.Parameters.AddWithValue("station", item.PreparationStation); sql.ExecuteNonQuery();
        using var auditSql = new NpgsqlCommand("INSERT INTO catalog_audit_records(id,organization_id,branch_id,product_id,action,actor_subject_id,occurred_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7)", connection, transaction);
        auditSql.Parameters.AddWithValue(audit.EventId); auditSql.Parameters.AddWithValue(organizationId); auditSql.Parameters.AddWithValue(branchId); auditSql.Parameters.AddWithValue(item.ProductId);
        auditSql.Parameters.AddWithValue(audit.Action); auditSql.Parameters.AddWithValue(audit.SubjectId); auditSql.Parameters.AddWithValue(occurredAt); auditSql.ExecuteNonQuery();
        Enqueue(connection, transaction, changed.EventId, "catalog.menu-item.changed.v1", item.ProductId, JsonSerializer.Serialize(changed), context.CorrelationId, occurredAt);
        Enqueue(connection, transaction, audit.EventId, "catalog.audit.v1", item.ProductId, JsonSerializer.Serialize(audit), context.CorrelationId, occurredAt);
        transaction.Commit();
        return item;
    }

    private static void Enqueue(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, string type, Guid aggregateId, string payload, Guid correlationId, DateTimeOffset occurredAt)
    {
        using var sql = new NpgsqlCommand("INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,$2,1,'catalog-menu-item',$3,$4::jsonb,$5,$6)", connection, transaction);
        sql.Parameters.AddWithValue(id); sql.Parameters.AddWithValue(type); sql.Parameters.AddWithValue(aggregateId); sql.Parameters.AddWithValue(payload);
        sql.Parameters.AddWithValue(correlationId.ToString("D")); sql.Parameters.AddWithValue(occurredAt); sql.ExecuteNonQuery();
    }
}
