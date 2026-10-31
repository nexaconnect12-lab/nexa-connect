using System.Text.Json;
using Npgsql;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Catalog.Application.Details;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Domain.Details;
namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class PostgresMenuDetailsRepository(NpgsqlDataSource source,TimeProvider clock):IMenuDetailsRepository
{
    public async Task<IReadOnlyList<EditableMenuItem>> ReadAsync(Guid org,Guid branch,CancellationToken ct)
    {
        await using var sql=source.CreateCommand("SELECT product_id,name,unit_price,currency,preparation_station,available,menu_version FROM catalog_menu_items WHERE organization_id=@org AND branch_id=@branch ORDER BY name,product_id LIMIT 201");
        sql.Parameters.AddWithValue("org",org);sql.Parameters.AddWithValue("branch",branch);
        await using var r=await sql.ExecuteReaderAsync(ct);var items=new List<EditableMenuItem>();
        while(await r.ReadAsync(ct))items.Add(new(r.GetGuid(0),r.GetString(1),r.GetDecimal(2),r.GetString(3),r.GetString(4),r.GetBoolean(5),r.GetInt64(6)));return items;
    }
    public async Task<EditableMenuItem?> ChangeAsync(Guid org,Guid branch,Guid product,UpdateMenuDetails input,MenuMutationContext actor,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);EditableMenuItem previous;
        await using(var sql=Command(c,tx,"SELECT name,unit_price,currency,preparation_station,available,menu_version FROM catalog_menu_items WHERE organization_id=@org AND branch_id=@branch AND product_id=@product FOR UPDATE",("org",org),("branch",branch),("product",product)))
        await using(var r=await sql.ExecuteReaderAsync(ct))
        {if(!await r.ReadAsync(ct))return null;previous=new(product,r.GetString(0),r.GetDecimal(1),r.GetString(2),r.GetString(3),r.GetBoolean(4),r.GetInt64(5));}
        var state=new MenuDetailsState(previous.Name,previous.UnitPrice,previous.Currency,previous.PreparationStation,previous.Version);
        var next=state.Change(input.Name,input.UnitPrice,input.PreparationStation,input.ExpectedVersion);
        if(ReferenceEquals(state,next)){await tx.CommitAsync(ct);return previous;}
        await using(var sql=Command(c,tx,"UPDATE catalog_menu_items SET name=@name,unit_price=@price,preparation_station=@station,menu_version=@version WHERE organization_id=@org AND branch_id=@branch AND product_id=@product AND menu_version=@previous",("name",next.Name),("price",next.UnitPrice),("station",next.PreparationStation),("version",next.Version),("org",org),("branch",branch),("product",product),("previous",state.Version)))
            if(await sql.ExecuteNonQueryAsync(ct)!=1)throw new MenuDetailsConflictException();
        var result=previous with{Name=next.Name,UnitPrice=next.UnitPrice,PreparationStation=next.PreparationStation,Version=next.Version};
        Guid id=Guid.NewGuid();var now=clock.GetUtcNow();
        // Audit only the fields owned by this editor, not a potentially stale availability snapshot.
        await using(var sql=Command(c,tx,"INSERT INTO catalog_menu_details_audit(id,organization_id,branch_id,product_id,version,previous_details,details,actor_subject_id,correlation_id,occurred_at_utc) VALUES(@id,@org,@branch,@product,@version,@previous::jsonb,@details::jsonb,@actor,@correlation,@now)",("id",id),("org",org),("branch",branch),("product",product),("version",next.Version),("previous",JsonSerializer.Serialize(state)),("details",JsonSerializer.Serialize(next)),("actor",actor.ActorSubjectId),("correlation",actor.CorrelationId),("now",now)))await sql.ExecuteNonQueryAsync(ct);
        var audit=new PlatformAuditEventV1(id,actor.CorrelationId,now,actor.ActorSubjectId,org,"catalog.menu-details.changed","catalog-menu-item",product.ToString("D"),"succeeded");
        var changed=new CatalogMenuItemChangedV1(Guid.NewGuid(),actor.CorrelationId,now,org,branch,product,result.Name,result.UnitPrice,result.Currency,result.PreparationStation,result.Available);
        await Enqueue(c,tx,id,"catalog.audit.v1",product,JsonSerializer.Serialize(audit),actor.CorrelationId,now,ct);
        await Enqueue(c,tx,changed.EventId,"catalog.menu-item.changed.v1",product,JsonSerializer.Serialize(changed),actor.CorrelationId,now,ct);
        await tx.CommitAsync(ct);return result;
    }
    private static async Task Enqueue(NpgsqlConnection c,NpgsqlTransaction tx,Guid id,string type,Guid product,string payload,Guid correlation,DateTimeOffset now,CancellationToken ct)
    {await using var sql=Command(c,tx,"INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES(@id,@type,1,@aggregate,@product,@payload::jsonb,@correlation,@now)",("id",id),("type",type),("aggregate",type=="catalog.menu-item.changed.v1"?"catalog-menu-item":"catalog-menu-details"),("product",product),("payload",payload),("correlation",correlation.ToString("D")),("now",now));await sql.ExecuteNonQueryAsync(ct);}
    private static NpgsqlCommand Command(NpgsqlConnection c,NpgsqlTransaction tx,string text,params(string Name,object Value)[] values)
    {var sql=new NpgsqlCommand(text,c,tx);foreach(var (name,value)in values)sql.Parameters.AddWithValue(name,value);return sql;}
}
