namespace NexaConnect.Services.Order.Application.Tenant;

public interface IOrderTenantAuthorizer
{
    Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader, CancellationToken cancellationToken);
    async Task<Guid?> GetBranchDecisionAsync(Guid organizationId,Guid branchId,string permission,string authorizationHeader,CancellationToken cancellationToken)=>
        await HasBranchAccessAsync(organizationId,branchId,permission,authorizationHeader,cancellationToken)?Guid.Empty:null;
}
