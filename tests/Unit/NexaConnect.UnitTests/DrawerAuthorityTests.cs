using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.Drawers;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.Drawers;
namespace NexaConnect.UnitTests;

public sealed class DrawerAuthorityTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.UtcNow;
    private static DrawerCommand Command(string cause="manual")=>new(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()),Guid.NewGuid(),cause,cause=="cash_sale"?Guid.NewGuid():null,cause=="manual"?"cash_check":null);
    private static DrawerCustody Custody()=>new("active","active","open","open","THB","cashier",Now.AddMinutes(-1),false,Now.AddMinutes(-1),false,true,Now.AddSeconds(-1));
    [Theory][InlineData("store")][InlineData("terminal")][InlineData("shift")][InlineData("cash")][InlineData("currency")][InlineData("actor")][InlineData("closed")][InlineData("future")]
    public void Reservation_requires_exact_open_custody(string changed)
    {
        var c=Custody();c=changed switch{"store"=>c with{StoreStatus="suspended"},"terminal"=>c with{TerminalStatus="revoked"},"shift"=>c with{ShiftStatus="closing"},"cash"=>c with{CashStatus="counting"},"currency"=>c with{Currency="SGD"},"actor"=>c with{Cashier="other"},"closed"=>c with{CashClosed=true},_=>c with{ShiftOpened=Now.AddMinutes(1)}};
        Assert.Throws<DrawerConflictException>(()=>DrawerIntent.Reserve(Command(),"cashier",Guid.NewGuid(),c,Now));
    }
    [Theory][InlineData(-61,true)][InlineData(1,true)][InlineData(-1,false)]
    public void Sale_requires_fresh_authoritative_cash_projection(int seconds,bool confirmed)
        =>Assert.Throws<DrawerConflictException>(()=>DrawerIntent.Reserve(Command("cash_sale"),"cashier",Guid.NewGuid(),Custody() with{SettledAtUtc=Now.AddSeconds(seconds),ConfirmedCashSale=confirmed},Now));
    [Fact]public void Intent_is_consumed_and_outcome_is_one_way_without_renewal()
    {
        var command=Command("cash_sale");var intent=DrawerIntent.Reserve(command,"cashier",Guid.NewGuid(),Custody(),Now);
        Assert.Equal(Now.AddSeconds(60),intent.ExpiresAtUtc);intent.RequireSame(command with{OperationId=Guid.NewGuid()},"cashier");
        Assert.Throws<DrawerConflictException>(()=>intent.RequireSame(command with{Scope=command.Scope with{TerminalId=Guid.NewGuid()}},"cashier"));
        var reported=intent.Report("unknown",Now.AddMinutes(2));Assert.Equal(intent.ExpiresAtUtc,reported.ExpiresAtUtc);
        Assert.Equal(reported,reported.Report("unknown",Now.AddMinutes(3)));Assert.Throws<DrawerConflictException>(()=>reported.Report("submitted",Now));
        Assert.Throws<ArgumentException>(()=>intent.Report("opened",Now));
    }
    [Theory][InlineData("manual",null,"free text")][InlineData("cash_sale",null,null)][InlineData("promptpay",null,null)]
    public void Invalid_causes_and_reasons_are_rejected(string cause,string? order,string? reason)
        =>Assert.Throws<ArgumentException>(()=>DrawerIntent.Validate(Command() with{Cause=cause,OrderId=order is null?null:Guid.Parse(order),Reason=reason},"cashier"));
    [Fact]public async Task Live_scope_and_permission_are_required_before_reserving_and_rechecked_on_retry()
    {
        var p=new Ports();var service=new DrawerIntents(p,p,p);var user=new PosUserContext("cashier","fixture-token");
        p.Granted=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ReserveAsync(p.Input,user,default));Assert.Equal(0,p.Reservations);
        p.Granted=true;var first=await service.ReserveAsync(p.Input,user,default);Assert.True(first.MaySubmit);Assert.Equal(DrawerIntents.ManualPermission,p.LastPermission);
        p.Granted=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ReserveAsync(p.Input,user,default));Assert.Equal(1,p.Reservations);
        p.Granted=true;p.WrongScope=true;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ReserveAsync(p.Input,user,default));Assert.Equal(1,p.Reservations);
    }
    [Fact]public async Task Reading_and_reporting_never_grant_submission_and_cannot_change_actor()
    {
        var p=new Ports();var service=new DrawerIntents(p,p,p);var user=new PosUserContext("cashier","fixture-token");
        await service.ReserveAsync(p.Input,user,default);var read=await service.ReadAsync(p.Input.OrganizationId,p.Input.OperationId,user,default);
        Assert.False(read!.MaySubmit);Assert.Equal(DrawerIntents.ReadPermission,p.LastPermission);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ReportAsync(p.Input.OperationId,new(p.Input.OrganizationId,"submitted"),user with{Subject="other"},default));
        var result=await service.ReportAsync(p.Input.OperationId,new(p.Input.OrganizationId,"unknown"),user,default);Assert.False(result!.MaySubmit);Assert.Equal("unknown",result.Status);
        p.Granted=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ReportAsync(p.Input.OperationId,new(p.Input.OrganizationId,"unknown"),user,default));
    }
    private sealed class Ports:IDrawerIntentRepository,IRestaurantScopeReader,IAuthorizationDecisionClient
    {
        private readonly DrawerCommand command=Command();public bool Granted=true,WrongScope;public int Reservations;public string? LastPermission;private DrawerIntent? saved;
        public DrawerIntentRequestV1 Input=>new(command.Scope.OrganizationId,command.Scope.RestaurantId,command.Scope.BranchId,command.Scope.StoreId,command.Scope.TerminalId,command.Scope.ShiftId,command.Scope.CashSessionId,command.OperationId,command.Cause,command.OrderId,command.Reason);
        public Task<(DrawerIntent,bool)> ReserveAsync(DrawerCommand c,string actor,Guid decision,Func<DrawerCustody?,DateTimeOffset,DrawerIntent> create,CancellationToken ct){Reservations++;bool created=saved is null;saved??=create(Custody(),Now);return Task.FromResult((saved,created));}
        public Task<DrawerIntent?> FindAsync(Guid org,Guid op,CancellationToken ct)=>Task.FromResult(saved);
        public Task<DrawerIntent> ReportAsync(DrawerIntent original,string outcome,Guid decision,CancellationToken ct){saved=original.Report(outcome,Now);return Task.FromResult(saved);}
        public Task<RestaurantAuthorizationScope> GetAsync(Guid branch,CancellationToken ct)=>Task.FromResult(new RestaurantAuthorizationScope(WrongScope?Guid.NewGuid():command.Scope.OrganizationId,command.Scope.RestaurantId,command.Scope.BranchId));
        public Task<AuthorizationDecision> DecideAsync(PosUserContext user,RestaurantAuthorizationScope scope,string permission,CancellationToken ct){LastPermission=permission;return Task.FromResult(new AuthorizationDecision(Guid.NewGuid(),Granted,null));}
    }
}
