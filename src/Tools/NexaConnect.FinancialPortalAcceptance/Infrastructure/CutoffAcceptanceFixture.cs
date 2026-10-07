using System.Text.Json;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;
using Npgsql;

namespace NexaConnect.FinancialPortalAcceptance.Infrastructure;

// Only identity administration and bounded private proof. Financial transitions use owning HTTP commands.
internal static class CutoffAcceptanceFixture
{
    public static async Task<bool> ExecuteAsync(FixtureOptions options,FixtureState state,string action,NpgsqlDataSource auth,
        PlatformDirectoryManagementService platform,CancellationToken ct)
    {
        if(!options.Cutoff || state.Cashier is null)throw new ArgumentException();
        if(action=="membership-second")
        {
            if(!await platform.ChangeMembershipAsync(state.OrganizationId,options.SecondManager!,new(options.SecondManager!,"suspended"),"cutoff-acceptance",ct))throw new InvalidOperationException();
            return true;
        }
        if(action is not("revoke-day-close-read" or "revoke-day-close-prepare" or "revoke-manager-source" or "restore-manager-source"))return false;
        string subject=action=="revoke-day-close-read"?options.Accountant!:options.Resolver;
        string permission=action=="revoke-day-close-read"?"pos.day-close.read":action=="revoke-day-close-prepare"?"pos.day-close.prepare":"payment.refund.read";
        await using var update=auth.CreateCommand("UPDATE authorization_user_permission_overrides SET effect=$3 WHERE subject_id=$1 AND permission_code=$2 AND status='active' AND scope_id IN(SELECT id FROM authorization_resource_scopes WHERE organization_id=$4)");
        update.Parameters.AddWithValue(subject);update.Parameters.AddWithValue(permission);update.Parameters.AddWithValue(action=="restore-manager-source"?"allow":"deny");update.Parameters.AddWithValue(state.OrganizationId);
        if(await update.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException();return true;
    }
    public static async Task ProofAsync(FixtureOptions options,CancellationToken ct)
    {
        if(!options.Cutoff)throw new ArgumentException();
        var state=JsonSerializer.Deserialize<FixtureState>(await File.ReadAllTextAsync(options.StatePath,ct),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException();
        if(state.RunId!=options.RunId || state.Cashier is null)throw new InvalidOperationException();
        await using var pos=NpgsqlDataSource.Create(options.Connection("pos"));await using var auth=NpgsqlDataSource.Create(options.Connection("authorization"));
        await using var query=pos.CreateCommand("""
            SELECT c.version,c.state->>'status',c.state::text,
              (SELECT count(*) FROM branch_day_cutoff_audit a WHERE a.organization_id=c.organization_id AND a.branch_id=c.branch_id AND a.business_date=c.business_date),
              (SELECT count(*) FROM branch_day_cutoff_operations o WHERE o.organization_id=c.organization_id AND o.branch_id=c.branch_id AND o.business_date=c.business_date AND status='pending'),
              (SELECT count(*) FROM branch_day_cutoff_operations o WHERE o.organization_id=c.organization_id AND o.branch_id=c.branch_id AND o.business_date=c.business_date AND status='abandoned'),
              (SELECT array_agg(authorization_decision_id) FROM branch_day_cutoff_audit a WHERE a.organization_id=c.organization_id AND a.branch_id=c.branch_id AND a.business_date=c.business_date)
            FROM branch_day_cutoffs c WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4
            """);
        query.Parameters.AddWithValue(state.OrganizationId);query.Parameters.AddWithValue(state.RestaurantId);query.Parameters.AddWithValue(state.BranchId);query.Parameters.AddWithValue(DateOnly.Parse(state.BusinessDate!));
        long version,audit,pending,abandoned;string status;Guid[] decisions;DateTimeOffset? lease;
        await using(var reader=await query.ExecuteReaderAsync(ct))
        {
            if(!await reader.ReadAsync(ct))throw new InvalidOperationException();version=reader.GetInt64(0);status=reader.GetString(1);
            using var data=JsonDocument.Parse(reader.GetString(2));lease=data.RootElement.TryGetProperty("leaseUntilUtc",out var time)&&time.ValueKind==JsonValueKind.String?time.GetDateTimeOffset():null;
            audit=reader.GetInt64(3);pending=reader.GetInt64(4);abandoned=reader.GetInt64(5);decisions=reader.GetFieldValue<Guid[]>(6);
            if(status=="ready_for_review" && (!data.RootElement.TryGetProperty("snapshot",out var snapshot)||snapshot.ValueKind!=JsonValueKind.Object
                ||!snapshot.TryGetProperty("cutoff",out var cut)||cut.ValueKind!=JsonValueKind.Object))throw new InvalidOperationException();
        }
        if(audit!=version || decisions.Length!=version)throw new InvalidOperationException();
        await using var grants=auth.CreateCommand("SELECT count(*) FROM authorization_decisions WHERE id=ANY($1) AND granted AND organization_id=$2");
        grants.Parameters.AddWithValue(decisions.Distinct().ToArray());grants.Parameters.AddWithValue(state.OrganizationId);
        if(Convert.ToInt64(await grants.ExecuteScalarAsync(ct))!=decisions.Distinct().Count())throw new InvalidOperationException();
        var counts=new Dictionary<string,long>();
        foreach(string owner in new[]{"order","payment","pos"})
        {
            await using var db=NpgsqlDataSource.Create(options.Connection(owner));
            await using var count=db.CreateCommand("SELECT count(*) FROM source_day_cutoffs WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND from_utc=$4 AND to_utc=$5");
            count.Parameters.AddWithValue(state.OrganizationId);count.Parameters.AddWithValue(state.RestaurantId);count.Parameters.AddWithValue(state.BranchId);count.Parameters.AddWithValue(state.FromUtc);count.Parameters.AddWithValue(state.ToUtc);
            counts[owner]=Convert.ToInt64(await count.ExecuteScalarAsync(ct));
        }
        Console.WriteLine(JsonSerializer.Serialize(new{version,status,audit,pending,abandoned,sourceCounts=counts,authorizationVerified=true,
            leaseRemainingMs=lease is null?0:Math.Max(0,(int)(lease.Value-DateTimeOffset.UtcNow).TotalMilliseconds)}));
    }
}
