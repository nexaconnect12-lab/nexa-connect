using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.Access;
using NexaConnect.Services.PlatformDirectory.Application.CustomerMemberships;
using Microsoft.Extensions.Logging.Abstractions;

namespace NexaConnect.UnitTests;
public sealed class CustomerMembershipManagementTests
{
    [Fact] public async Task Management_requires_access_to_exact_organization(){var r=new FakeRepository();var s=new CustomerMembershipManagement(new AccessReader(false),r,NullLogger<CustomerMembershipManagement>.Instance);Assert.Null(await s.ListAsync(Guid.NewGuid(),"actor",default));Assert.Equal(0,r.Calls);}
    [Fact] public async Task Manager_cannot_change_own_membership(){var s=new CustomerMembershipManagement(new AccessReader(true),new FakeRepository(),NullLogger<CustomerMembershipManagement>.Instance);await Assert.ThrowsAsync<CustomerMembershipConflictException>(()=>s.ChangeAsync(Guid.NewGuid(),"actor",new("suspended",1),"actor",default));}
    [Fact] public async Task Change_normalizes_status_and_passes_version(){var r=new FakeRepository();var s=new CustomerMembershipManagement(new AccessReader(true),r,NullLogger<CustomerMembershipManagement>.Instance);var result=await s.ChangeAsync(Guid.NewGuid(),"target",new(" ACTIVE ",3),"actor",default);Assert.Equal("active",result?.Status);Assert.Equal(3,r.Version);}
    private sealed class AccessReader(bool allowed):IOrganizationAccessReader{public Task<bool> HasNexaConnectAccessAsync(Guid o,string s,CancellationToken c)=>Task.FromResult(allowed);public Task<IReadOnlyList<OrganizationApplicationAccess>> GetCurrentAccessAsync(string s,CancellationToken c)=>Task.FromResult<IReadOnlyList<OrganizationApplicationAccess>>([]);}
    private sealed class FakeRepository:ICustomerMembershipRepository{public int Calls;public long? Version;public Task<IReadOnlyCollection<CustomerMembershipSummary>> ListAsync(Guid o,CancellationToken c){Calls++;return Task.FromResult<IReadOnlyCollection<CustomerMembershipSummary>>([]);}public Task<CustomerMembershipSummary?> ChangeAsync(Guid o,string s,string status,long? v,string a,CancellationToken c){Calls++;Version=v;return Task.FromResult<CustomerMembershipSummary?>(new(o,s,status,null,DateTimeOffset.UtcNow,null,null,v??1));}}
}
