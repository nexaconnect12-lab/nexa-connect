using NexaConnect.Services.Restaurant.Application.Branches;
using NexaConnect.Services.Authorization.Domain;

namespace NexaConnect.UnitTests;

public sealed class OperationalBranchDiscoveryTests
{
    [Fact]
    public async Task DiscoveryChecksExactLiveBranchScopeAndHidesDeniedOrInactiveBranches()
    {
        Guid org=Guid.NewGuid(),restaurant=Guid.NewGuid(),allowed=Guid.NewGuid(),denied=Guid.NewGuid();
        var rows=new[] { Row(org,restaurant,allowed,"active"),Row(org,restaurant,denied,"active"),Row(org,restaurant,Guid.NewGuid(),"closed") };
        var authorization=new Authorizer(allowed);
        var discovery=new OperationalBranchDiscovery(new Repository(rows),authorization);
        var result=await discovery.ListAsync(org,"kitchen","Bearer opaque",default);
        Assert.Equal(allowed,Assert.Single(result).BranchId);
        Assert.Equal(2,authorization.Calls.Count);
        Assert.All(authorization.Calls,call=>{Assert.Equal(org,call.Org);Assert.Equal(restaurant,call.Restaurant);Assert.Equal("kitchen.ticket.read",call.Permission);});
        authorization.Granted=false;
        Assert.Empty(await discovery.ListAsync(org,"kitchen","Bearer opaque",default));
    }
    [Fact]
    public async Task InvalidCapabilityOrOrganizationCannotQueryRepository()
    {
        var repository=new Repository([]);var discovery=new OperationalBranchDiscovery(repository,new Authorizer(Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(()=>discovery.ListAsync(Guid.NewGuid(),"pos.day-close.finalize","Bearer opaque",default));
        await Assert.ThrowsAsync<ArgumentException>(()=>discovery.ListAsync(Guid.Empty,"kitchen","Bearer opaque",default));
        Assert.Equal(0,repository.Calls);
    }
    [Fact]
    public void KitchenOperatorHasOnlyMenuDiscoveryAndKitchenPermissions()
    {
        Assert.Equal(new[]{"catalog.menu.read","kitchen.ticket.read","kitchen.ticket.transition"},ProductRoleDefaults.PermissionsFor("kitchen-operator"));
    }
    private static BranchSummary Row(Guid org,Guid restaurant,Guid branch,string status)=>new(branch,restaurant,org,"draft","Draft","Asia/Singapore","THB",status,null,null,1);
    private sealed class Repository(IReadOnlyCollection<BranchSummary> rows):IBranchManagementRepository
    {
        public int Calls{get;private set;}
        public Task<IReadOnlyCollection<BranchSummary>> ListAsync(Guid org,CancellationToken ct){Calls++;return Task.FromResult<IReadOnlyCollection<BranchSummary>>(rows.Where(r=>r.OrganizationId==org).ToArray());}
        public Task<BranchSummary?> CreateAsync(Guid org,CreateManagedBranchCommand command,string actor,CancellationToken ct)=>throw new NotSupportedException();
        public Task<BranchSummary?> UpdateAsync(Guid org,Guid branch,UpdateManagedBranchCommand command,string actor,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Authorizer(Guid allowed):IBranchCustomerAuthorizer
    {
        public bool Granted{get;set;}=true;
        public List<(Guid Org,Guid? Restaurant,string Permission)> Calls{get;}=[];
        public Task<bool> IsGrantedAsync(Guid org,Guid? restaurant,Guid? branch,string permission,string header,CancellationToken ct){Calls.Add((org,restaurant,permission));return Task.FromResult(Granted&&branch==allowed);}
    }
}
