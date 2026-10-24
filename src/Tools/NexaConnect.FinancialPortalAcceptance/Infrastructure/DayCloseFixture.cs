extern alias POS;
extern alias ORDER;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;
using Npgsql;
using PosDb=POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using PosDomain=POS::NexaConnect.Services.POS.Domain.CashReviews;
using PosApp=POS::NexaConnect.Services.POS.Application.CashReviews;
namespace NexaConnect.FinancialPortalAcceptance.Infrastructure;

internal static class DayCloseFixture
{
    public static async Task<bool> ExecuteAsync(FixtureOptions options,FixtureState state,string action,NpgsqlDataSource authorization,
        PlatformDirectoryManagementService platform,CancellationToken ct)
    {
        if(state.Pos is null)throw new InvalidOperationException();
        if(action is "stop-pos" or "start-pos"){await Control(options,action,ct);return true;}
        if(action=="membership-second"){
            if(!await platform.ChangeMembershipAsync(state.OrganizationId,options.SecondManager!,new(options.SecondManager!,"suspended"),"day-close-fixture",ct))throw new InvalidOperationException();return true;}
        if(action is "revoke-day-close-read" or "revoke-day-close-prepare" or "revoke-manager-source" or "restore-manager-source"){
            string subject=action=="revoke-day-close-read"?options.Reader:options.Resolver;
            string permission=action=="revoke-day-close-read"?"pos.day-close.read":action=="revoke-day-close-prepare"?"pos.day-close.prepare":"payment.refund.read";
            await using var update=authorization.CreateCommand("UPDATE authorization_user_permission_overrides SET effect=$3 WHERE subject_id=$1 AND permission_code=$2 AND status='active'");
            update.Parameters.AddWithValue(subject);update.Parameters.AddWithValue(permission);update.Parameters.AddWithValue(action=="restore-manager-source"?"allow":"deny");
            if(await update.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException();return true;
        }
        if(action is not("resolve-day-close" or "same-total-evidence" or "late-cash" or "approve-cash" or "preparation-proof"))return false;
        await using var pos=NpgsqlDataSource.Create(options.Connection("pos"));var ids=state.Pos;
        var reviews=new PosDb.PostgresCashReviewStore(pos);var scope=new PosApp.CashReviewScope(state.OrganizationId,state.RestaurantId,state.BranchId,ids.StoreId);
        async Task Decide(string decision){var detail=await reviews.GetAsync(scope,ids.ClosedSessionId,ct)??throw new InvalidOperationException();
            await reviews.ResolveAsync(scope,ids.ClosedSessionId,PosDomain.CashReviewDecision.Create(decision,"Synthetic acceptance evidence"),options.Resolver,Guid.NewGuid(),detail.Session.SessionVersion,detail.Session.ReviewVersion,Guid.NewGuid(),new string('a',64),DateTimeOffset.UtcNow,ct);}
        if(action=="resolve-day-close"){
            var cash=new PosDb.PostgresCashSessionStore(pos);var summary=await cash.GetSummaryAsync(ids.OpenSessionId,options.Resolver,ids.OlderTerminalId,ct)??throw new InvalidOperationException();
            await cash.CloseAsync(ids.OpenSessionId,summary.ExpectedClosingAmount,summary.ConcurrencyVersion,options.Resolver,ids.OlderTerminalId,ct);
            var shifts=new PosDb.PostgresShiftStore(pos);var value=await shifts.FindOpenAsync(ids.OpenShiftId,ct)??throw new InvalidOperationException();
            var shift=POS::NexaConnect.Services.POS.Domain.Shifts.Shift.Rehydrate(value.Id,value.StoreId,value.TerminalId,value.EmployeeSubject,value.ShiftNumber,value.Status,value.OpenedAtUtc,value.ClosedAtUtc,value.OpenedBy,value.ClosedBy,value.AuthorizationDecisionId,value.CloseAuthorizationDecisionId,value.ConcurrencyVersion);
            shift.Close(options.Resolver,Guid.NewGuid(),DateTimeOffset.UtcNow);if(!await shifts.TryCloseAsync(shift,ct))throw new InvalidOperationException();await Decide("approve");
        }else if(action=="same-total-evidence"){
            // Supported completed-aggregate save advances owner metadata/version without rewriting its receipt/publication.
            await using var order=NpgsqlDataSource.Create(options.Connection("order"));
            await using var select=order.CreateCommand("SELECT id FROM orders WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND status='completed' AND created_at_utc>=$4 AND created_at_utc<$5");
            foreach(var parameter in new object[]{state.OrganizationId,state.RestaurantId,state.BranchId,state.FromUtc,state.ToUtc})select.Parameters.AddWithValue(parameter);
            Guid id;await using(var reader=await select.ExecuteReaderAsync(ct)){if(!await reader.ReadAsync(ct))throw new InvalidOperationException();id=reader.GetGuid(0);if(await reader.ReadAsync(ct))throw new InvalidOperationException();}
            var repository=new ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderRepository(order);
            var aggregate=await repository.GetAsync(id,ct)??throw new InvalidOperationException();await repository.SaveAsync(aggregate,ct);
        }
        else if(action=="approve-cash")await Decide("approve");
        else if(action=="late-cash"){
            var eventValue=new OrderManualTenderSettledV1(Guid.NewGuid(),Guid.NewGuid(),state.FromUtc.AddHours(10),state.OrganizationId,state.RestaurantId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),ids.TerminalId,"cash",1,"THB");
            await new PosDb.PostgresOrderSettlementProjectionStore(pos).ProjectAsync(eventValue,ct);
        }else await Proof(pos,authorization,state,ct);
        return true;
    }
    private static async Task Control(FixtureOptions options,string action,CancellationToken ct)
    {
        string directory=Path.Combine(Path.GetDirectoryName(options.StatePath)!,"pos-control");
        if(!Directory.Exists(directory))throw new InvalidOperationException();
        string id=Guid.NewGuid().ToString("N"),temp=Path.Combine(directory,id+".tmp");
        await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(new{runId=options.RunId,requestId=id,action}),ct);
        File.Move(temp,Path.Combine(directory,"request.json"),true);
        while(true){string ack=Path.Combine(directory,"ack.json");if(File.Exists(ack)){
            using var result=JsonDocument.Parse(await File.ReadAllTextAsync(ack,ct));
            if(result.RootElement.GetProperty("requestId").GetString()==id){if(result.RootElement.GetProperty("status").GetString()!="completed")throw new InvalidOperationException();return;}}
            await Task.Delay(100,ct);}
    }
    private static async Task Proof(NpgsqlDataSource pos,NpgsqlDataSource auth,FixtureState state,CancellationToken ct)
    {
        await using var query=pos.CreateCommand("""
            SELECT c.version,c.state->>'status',c.state,
                (SELECT count(*) FROM branch_day_close_audit a WHERE a.organization_id=c.organization_id AND a.branch_id=c.branch_id AND a.business_date=c.business_date),
                (SELECT count(*) FROM branch_day_close_operations o WHERE o.organization_id=c.organization_id AND o.branch_id=c.branch_id AND o.business_date=c.business_date AND status='completed'),
                (SELECT count(*) FROM branch_day_close_operations o WHERE o.organization_id=c.organization_id AND o.branch_id=c.branch_id AND o.business_date=c.business_date AND status='abandoned'),
                (SELECT array_agg(a.authorization_decision_id ORDER BY a.version) FROM branch_day_close_audit a WHERE a.organization_id=c.organization_id AND a.branch_id=c.branch_id AND a.business_date=c.business_date),
                (SELECT count(*) FROM branch_day_close_operations o WHERE o.organization_id=c.organization_id AND o.branch_id=c.branch_id AND o.business_date=c.business_date AND status='pending')
            FROM branch_day_closes c WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4
            """);
        query.Parameters.AddWithValue(state.OrganizationId);query.Parameters.AddWithValue(state.RestaurantId);query.Parameters.AddWithValue(state.BranchId);query.Parameters.AddWithValue(DateOnly.Parse(state.BusinessDate!));
        long version,audit,completed,abandoned,pending;string status;Guid[] decisions;DateTimeOffset? lease;
        await using(var reader=await query.ExecuteReaderAsync(ct)){
            if(!await reader.ReadAsync(ct))throw new InvalidOperationException();version=reader.GetInt64(0);status=reader.GetString(1);using var json=JsonDocument.Parse(reader.GetString(2));
            lease=json.RootElement.TryGetProperty("leaseUntilUtc",out var time)&&time.ValueKind==JsonValueKind.String?time.GetDateTimeOffset():null;
            audit=reader.GetInt64(3);completed=reader.GetInt64(4);abandoned=reader.GetInt64(5);decisions=reader.GetFieldValue<Guid[]>(6);pending=reader.GetInt64(7);
        }
        if(audit!=version || decisions.Length!=version)throw new InvalidOperationException();
        await using var authority=auth.CreateCommand("SELECT count(*) FROM authorization_decisions WHERE id=ANY($1) AND granted AND organization_id=$2");
        authority.Parameters.AddWithValue(decisions.Distinct().ToArray());authority.Parameters.AddWithValue(state.OrganizationId);
        if(Convert.ToInt64(await authority.ExecuteScalarAsync(ct))!=decisions.Distinct().Count())throw new InvalidOperationException();
        Console.WriteLine(JsonSerializer.Serialize(new{version,status,audit,completed,abandoned,pending,leaseRemainingMs=lease is null?0:Math.Max(0,(int)(lease.Value-DateTimeOffset.UtcNow).TotalMilliseconds)}));
    }
}
