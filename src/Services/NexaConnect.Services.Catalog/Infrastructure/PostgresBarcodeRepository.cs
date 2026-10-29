using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Catalog.Application.Barcodes;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Domain.Barcodes;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class PostgresBarcodeRepository(NpgsqlDataSource source, TimeProvider clock) : IBarcodeRepository
{
    public async Task<ProductBarcode?> ReadAsync(Guid org, Guid branch, Guid product, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var sql = Command(connection, null, "SELECT b.barcode,COALESCE(b.version,0) FROM catalog_menu_items m LEFT JOIN catalog_branch_barcodes b USING(organization_id,branch_id,product_id) WHERE m.organization_id=@org AND m.branch_id=@branch AND m.product_id=@product", ("org",org),("branch",branch),("product",product));
        await using var reader = await sql.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(org,branch,product,reader.IsDBNull(0)?null:reader.GetString(0),reader.GetInt64(1)) : null;
    }
    public async Task<MenuItem?> LookupAsync(Guid org, Guid branch, string barcode, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var sql = Command(connection,null,"SELECT m.product_id,m.name,m.unit_price,m.currency,m.preparation_station,m.available FROM catalog_branch_barcodes b JOIN catalog_menu_items m USING(organization_id,branch_id,product_id) WHERE b.organization_id=@org AND b.branch_id=@branch AND b.barcode=@barcode",("org",org),("branch",branch),("barcode",barcode));
        await using var reader = await sql.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0),reader.GetString(1),reader.GetDecimal(2),reader.GetString(3),reader.GetString(4),reader.GetBoolean(5)) : null;
    }
    public async Task<ProductBarcode> ChangeAsync(Guid org,Guid branch,Guid product,string? barcode,Func<BarcodeWriteState,BarcodeChange> decide,MenuMutationContext context,CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Tenant-local serialization covers new rows, cross-branch collisions and orphan release.
        await using(var gate=Command(connection,tx,"SELECT pg_advisory_xact_lock(hashtextextended(@org::text,7261))",("org",org))) await gate.ExecuteNonQueryAsync(ct);
        await using(var exists=Command(connection,tx,"SELECT product_id FROM catalog_menu_items WHERE organization_id=@org AND branch_id=@branch AND product_id=@product FOR KEY SHARE",("org",org),("branch",branch),("product",product)))
            if(await exists.ExecuteScalarAsync(ct) is null) throw new KeyNotFoundException();
        long version=0;string? previous=null;
        await using(var current=Command(connection,tx,"SELECT barcode,version FROM catalog_branch_barcodes WHERE organization_id=@org AND branch_id=@branch AND product_id=@product FOR UPDATE",("org",org),("branch",branch),("product",product)))
        await using(var reader=await current.ExecuteReaderAsync(ct))
            if(await reader.ReadAsync(ct)){previous=reader.IsDBNull(0)?null:reader.GetString(0);version=reader.GetInt64(1);}
        Guid? registered=null;
        if(barcode is not null)
        {
            await using var owner=Command(connection,tx,"SELECT product_id FROM catalog_barcode_registry WHERE organization_id=@org AND barcode=@barcode",("org",org),("barcode",barcode));
            if(await owner.ExecuteScalarAsync(ct) is Guid id)registered=id;
        }
        var change=decide(new(version,previous,registered));
        if(!change.Changed){await tx.CommitAsync(ct);return new(org,branch,product,previous,version);}
        if(change.Barcode is not null)
        {
            await using var register=Command(connection,tx,"INSERT INTO catalog_barcode_registry(organization_id,barcode,product_id) VALUES(@org,@barcode,@product) ON CONFLICT(organization_id,barcode) DO NOTHING",("org",org),("barcode",change.Barcode),("product",product));
            await register.ExecuteNonQueryAsync(ct);
        }
        await using(var save=Command(connection,tx,"INSERT INTO catalog_branch_barcodes(organization_id,branch_id,product_id,barcode,version) VALUES(@org,@branch,@product,@barcode,@version) ON CONFLICT(organization_id,branch_id,product_id) DO UPDATE SET barcode=EXCLUDED.barcode,version=EXCLUDED.version",("org",org),("branch",branch),("product",product),("barcode",change.Barcode),("version",change.Version)))await save.ExecuteNonQueryAsync(ct);
        await using(var cleanup=Command(connection,tx,"DELETE FROM catalog_barcode_registry r WHERE r.organization_id=@org AND NOT EXISTS(SELECT 1 FROM catalog_branch_barcodes b WHERE b.organization_id=r.organization_id AND b.barcode=r.barcode)",("org",org)))await cleanup.ExecuteNonQueryAsync(ct);
        var now=clock.GetUtcNow();var auditId=Guid.NewGuid();
        await using(var audit=Command(connection,tx,"INSERT INTO catalog_barcode_audit(id,organization_id,branch_id,product_id,version,previous_barcode,barcode,actor_subject_id,correlation_id,occurred_at_utc) VALUES(@id,@org,@branch,@product,@version,@previous,@barcode,@actor,@correlation,@now)",("id",auditId),("org",org),("branch",branch),("product",product),("version",change.Version),("previous",previous),("barcode",change.Barcode),("actor",context.ActorSubjectId),("correlation",context.CorrelationId),("now",now)))await audit.ExecuteNonQueryAsync(ct);
        var integration = new PlatformAuditEventV1(auditId,context.CorrelationId,now,context.ActorSubjectId,org,"catalog.barcode.changed","catalog-product-barcode",product.ToString("D"),"succeeded");
        await using(var enqueue=Command(connection,tx,"INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES(@id,'catalog.audit.v1',1,'catalog-product-barcode',@product,@payload::jsonb,@correlation,@now)",("id",auditId),("product",product),("payload",JsonSerializer.Serialize(integration)),("correlation",context.CorrelationId.ToString("D")),("now",now)))await enqueue.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);return new(org,branch,product,change.Barcode,change.Version);
    }
    private static NpgsqlCommand Command(NpgsqlConnection connection,NpgsqlTransaction? transaction,string sql,params (string Name,object? Value)[] parameters)
    {
        var command=new NpgsqlCommand(sql,connection,transaction);
        foreach(var (name,value) in parameters)
            if(value is null or string)command.Parameters.Add(new NpgsqlParameter(name,NpgsqlDbType.Text){Value=value??DBNull.Value});
            else command.Parameters.AddWithValue(name,value);
        return command;
    }
}
