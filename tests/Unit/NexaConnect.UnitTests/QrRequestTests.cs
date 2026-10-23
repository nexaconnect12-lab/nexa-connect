using NexaConnect.Services.Order.Domain.Qr;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.RestaurantQr;
namespace NexaConnect.UnitTests;
public sealed class QrRequestTests
{
    [Fact]public void Aggregate_deadline_and_version_fence_rejection_and_expiry()
    {
        var now=DateTimeOffset.UtcNow;var request=new GuestRequest(Guid.NewGuid(),Scope(),new string('A',64),now,now.AddMinutes(15));
        Assert.False(request.Expire(now));Assert.Throws<GuestRequestConflictException>(()=>request.Reject(2,"closing",now));
        Assert.Throws<ArgumentException>(()=>request.Reject(1,"free text",now));request.Reject(1,"closing",now);Assert.Equal("rejected",request.Status);Assert.Equal(2,request.Version);
        Assert.False(request.Expire(now.AddHours(1)));Assert.Throws<GuestRequestConflictException>(()=>request.Reject(2,"closing",now));
        var expiring=new GuestRequest(Guid.NewGuid(),Scope(),new string('A',64),now,now.AddMinutes(1));Assert.True(expiring.Expire(now.AddMinutes(1)));Assert.Equal("expired",expiring.Status);Assert.False(expiring.Expire(now.AddHours(1)));Assert.Equal(2,expiring.Version);
    }
    [Theory][InlineData(10,0,0,0,0,0)][InlineData(0,30,0,0,0,0)][InlineData(0,0,500,0,0,0)][InlineData(0,0,0,0,10,0)][InlineData(0,0,0,0,0,200)]
    public void Retained_hourly_and_pending_budgets_fail_closed(int visitor,int entry,int branch,int vp,int ep,int bp)=>Assert.Throws<GuestRequestLimitException>(()=>GuestRequest.Admit(visitor,entry,branch,vp,ep,bp));
    [Fact]public void Pending_visitor_blocks_new_identity_and_quote_budgets_are_bounded()
    {
        GuestRequest.Admit(0,0,0,0,0,0);Assert.Throws<GuestRequestConflictException>(()=>GuestRequest.Admit(0,0,0,1,0,0));
        GuestRequest.AdmitQuote(9,99,999);Assert.Throws<GuestRequestLimitException>(()=>GuestRequest.AdmitQuote(10,0,0));Assert.Throws<GuestRequestLimitException>(()=>GuestRequest.AdmitQuote(0,100,0));Assert.Throws<GuestRequestLimitException>(()=>GuestRequest.AdmitQuote(0,0,1000));
    }
    [Fact]public async Task Staff_requires_live_decision_and_scoped_permissions_before_persistence()
    {
        var repo=new Repo();var auth=new Auth();var service=new QrRequestService(new(new Port(),new Policies(),TimeProvider.System),repo,auth);var scope=Scope();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.QueueAsync(scope.OrganizationId,scope.BranchId,"pending","Bearer staff",default));Assert.Equal(0,repo.Reads);Assert.Equal(ProductPermissions.OrderRead,auth.Permission);
        auth.Decision=Guid.NewGuid();await service.QueueAsync(scope.OrganizationId,scope.BranchId,"pending","Bearer staff",default);Assert.Equal(1,repo.Reads);
        await service.RejectAsync(scope.OrganizationId,scope.BranchId,Guid.NewGuid(),new(1,"closing"),"operator","Bearer staff",Guid.NewGuid(),default);Assert.Equal(ProductPermissions.OrderPlace,auth.Permission);Assert.Equal(auth.Decision,repo.Decision);
        auth.Decision=null;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.RejectAsync(scope.OrganizationId,scope.BranchId,Guid.NewGuid(),new(1,"closing"),"operator","Bearer staff",Guid.NewGuid(),default));Assert.Equal(1,repo.Rejects);
    }
    [Fact]public async Task Revocation_denies_replay_and_private_status_before_repository_access()
    {
        var repo=new Repo();var service=new QrRequestService(new(new Port(),new Policies(),TimeProvider.System),repo,new Auth());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.MineAsync(new string('S',43),default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.SubmitAsync(new string('S',43),new(Guid.NewGuid(),new string('A',64)),Guid.NewGuid(),default));Assert.Equal(0,repo.Reads);
    }
    private static GuestRequestScope Scope()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"A1");
    private sealed class Port:IQrMenuPort
    {
        public Task<QrGuestContext?> ContextAsync(string token,CancellationToken ct)=>Task.FromResult<QrGuestContext?>(null);
        public Task<IReadOnlyList<CatalogMenuItem>> MenuAsync(Guid org,Guid branch,CancellationToken ct)=>throw new InvalidOperationException();
    }
    private sealed class Policies:IBranchPricingPort{public Task<PricingPolicy> GetAsync(Guid org,Guid restaurant,Guid branch,CancellationToken ct)=>throw new InvalidOperationException();}
    private sealed class Auth:IOrderTenantAuthorizer
    {
        public Guid? Decision;public string? Permission;
        public Task<bool> HasBranchAccessAsync(Guid org,Guid branch,string permission,string bearer,CancellationToken ct)=>Task.FromResult(Decision is not null);
        public Task<Guid?> GetBranchDecisionAsync(Guid org,Guid branch,string permission,string bearer,CancellationToken ct){Permission=permission;return Task.FromResult(Decision);}
    }
    private sealed class Repo:IQrRequestRepository
    {
        public int Reads,Rejects;public Guid Decision;
        public Task<QrRequestList> ListAsync(Guid org,Guid branch,Guid? visitor,string state,CancellationToken ct){Reads++;return Task.FromResult(new QrRequestList([],false));}
        public Task<RetainedGuestRequest?> RejectAsync(Guid org,Guid branch,Guid id,QrRejection input,string actor,Guid decision,Guid correlation,CancellationToken ct){Rejects++;Decision=decision;return Task.FromResult<RetainedGuestRequest?>(null);}
        public Task SaveQuoteAsync(RetainedGuestQuote quote,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<RetainedGuestRequest> SubmitAsync(GuestRequestScope scope,QrSubmission input,DateTimeOffset expiry,Guid correlation,CancellationToken ct)=>throw new InvalidOperationException();
    }
}
