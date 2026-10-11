using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Application.Shifts;
namespace NexaConnect.UnitTests;

public sealed class SettlementInventoryTests
{
    [Theory]
    [InlineData("scope")][InlineData("denied")][InlineData("decision")][InlineData("subject")]
    public async Task Live_authority_and_exact_owner_scope_precede_inventory_reads(string kind)
    {
        var p=new Ports();var range=new SettlementReportRange(p.Scope.OrganizationId,p.Scope.BranchId,new(2026,9,1),new(2026,9,2));
        if(kind=="scope")p.Scope=p.Scope with{OrganizationId=Guid.NewGuid()};
        if(kind=="denied")p.Decision=p.Decision with{Granted=false};if(kind=="decision")p.Decision=p.Decision with{DecisionId=Guid.Empty};
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new SettlementInventory(p,p,p).ReadAsync(range,new(kind=="subject"?"":"operator","token"),default));Assert.Equal(0,p.Reads);
    }
    private sealed class Ports:ISettlementInventoryStore,IRestaurantScopeReader,IAuthorizationDecisionClient
    {
        public RestaurantAuthorizationScope Scope=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());public AuthorizationDecision Decision=new(Guid.NewGuid(),true,null);public int Reads;
        public Task<RestaurantAuthorizationScope> GetAsync(Guid branch,CancellationToken ct)=>Task.FromResult(Scope);
        public Task<AuthorizationDecision> DecideAsync(PosUserContext user,RestaurantAuthorizationScope scope,string permission,CancellationToken ct)
        {Assert.Equal(DayClosePreparation.ReadPermission,permission);return Task.FromResult(Decision);}
        public Task<SettlementManifest> ReadAsync(SettlementReportRange range,Guid restaurant,CancellationToken ct){Reads++;throw new NotImplementedException();}
    }
}
