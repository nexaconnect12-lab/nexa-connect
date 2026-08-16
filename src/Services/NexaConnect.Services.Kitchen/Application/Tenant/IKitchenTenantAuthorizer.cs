namespace NexaConnect.Services.Kitchen.Application.Tenant;
public interface IKitchenTenantAuthorizer{Task<bool> HasBranchAccessAsync(Guid organizationId,Guid branchId,string permission,string authorizationHeader,CancellationToken cancellationToken);}
