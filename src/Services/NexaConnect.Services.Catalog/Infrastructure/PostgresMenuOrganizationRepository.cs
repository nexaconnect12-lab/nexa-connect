using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Organization;
using NexaConnect.Services.Catalog.Domain.Organization;
namespace NexaConnect.Services.Catalog.Infrastructure;
public sealed class PostgresMenuOrganizationRepository(NpgsqlDataSource source,TimeProvider clock):IMenuOrganizationRepository
{
    private static readonly JsonSerializerOptions Json=JsonSerializerOptions.Web;
    public async Task<BranchMenuOrganizationState> ReadAsync(Guid org,Guid branch,CancellationToken ct)
    {await using var c=await source.OpenConnectionAsync(ct);return await Read(c,null,org,branch,ct);}
    private static async Task<BranchMenuOrganizationState> Read(NpgsqlConnection c,NpgsqlTransaction? tx,Guid org,Guid branch,CancellationToken ct)
    {
        await using var sql=Command(c,tx,"SELECT version,categories::text,items::text FROM catalog_menu_organizations WHERE organization_id=@org AND branch_id=@branch",("org",org),("branch",branch));
        await using var r=await sql.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return BranchMenuOrganizationState.Empty();
        return BranchMenuOrganizationState.Restore(r.GetInt64(0),JsonSerializer.Deserialize<Category[]>(r.GetString(1),Json),JsonSerializer.Deserialize<Placement[]>(r.GetString(2),Json));
    }
    public async Task<BranchMenuOrganizationState> ChangeAsync(Guid org,Guid branch,Func<BranchMenuOrganizationState,IReadOnlySet<Guid>,BranchMenuOrganizationState> change,MenuMutationContext actor,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        // Serialize absent first writes too; only parameter values enter the lock's stable tenant/branch key.
        await using(var gate=Command(c,tx,"SELECT pg_advisory_xact_lock(hashtextextended(@org::text || ':' || @branch::text,7262))",("org",org),("branch",branch)))await gate.ExecuteNonQueryAsync(ct);
        var products=new HashSet<Guid>();
        await using(var sql=Command(c,tx,"SELECT product_id FROM catalog_menu_items WHERE organization_id=@org AND branch_id=@branch ORDER BY product_id LIMIT 201 FOR SHARE",("org",org),("branch",branch)))
        await using(var r=await sql.ExecuteReaderAsync(ct)){while(await r.ReadAsync(ct))products.Add(r.GetGuid(0));}
        if(products.Count>200)throw new ArgumentException("Menu exceeds organization bounds.");
        var previous=await Read(c,tx,org,branch,ct);var next=change(previous,products);
        if(ReferenceEquals(next,previous)){await tx.CommitAsync(ct);return previous;}
        var categories=JsonSerializer.Serialize(next.Categories,Json);var items=JsonSerializer.Serialize(next.Items,Json);var id=Guid.NewGuid();var now=clock.GetUtcNow();
        await using(var sql=Command(c,tx,"INSERT INTO catalog_menu_organizations(organization_id,branch_id,version,categories,items) VALUES(@org,@branch,@version,@categories::jsonb,@items::jsonb) ON CONFLICT(organization_id,branch_id) DO UPDATE SET version=EXCLUDED.version,categories=EXCLUDED.categories,items=EXCLUDED.items WHERE catalog_menu_organizations.version=@previous",("org",org),("branch",branch),("version",next.Version),("categories",categories),("items",items),("previous",previous.Version)))
            if(await sql.ExecuteNonQueryAsync(ct)!=1)throw new MenuOrganizationConflictException();
        await using(var audit=Command(c,tx,"INSERT INTO catalog_menu_organization_audit(id,organization_id,branch_id,version,previous_layout,layout,actor_subject_id,correlation_id,occurred_at_utc) VALUES(@id,@org,@branch,@version,@previous::jsonb,@layout::jsonb,@actor,@correlation,@now)",("id",id),("org",org),("branch",branch),("version",next.Version),("previous",JsonSerializer.Serialize(previous,Json)),("layout",JsonSerializer.Serialize(next,Json)),("actor",actor.ActorSubjectId),("correlation",actor.CorrelationId),("now",now)))await audit.ExecuteNonQueryAsync(ct);
        var entry=new PlatformAuditEventV1(id,actor.CorrelationId,now,actor.ActorSubjectId,org,"catalog.menu-organization.changed","catalog-menu-organization",branch.ToString("D"),"succeeded");
        await using(var enqueue=Command(c,tx,"INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES(@id,'catalog.audit.v1',1,'catalog-menu-organization',@branch,@payload::jsonb,@correlation,@now)",("id",id),("branch",branch),("payload",JsonSerializer.Serialize(entry)),("correlation",actor.CorrelationId.ToString("D")),("now",now)))await enqueue.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);return next;
    }
    private static NpgsqlCommand Command(NpgsqlConnection c,NpgsqlTransaction? tx,string text,params(string Name,object Value)[] values)
    {var command=new NpgsqlCommand(text,c,tx);foreach(var (name,value)in values)command.Parameters.AddWithValue(name,value);return command;}
}
