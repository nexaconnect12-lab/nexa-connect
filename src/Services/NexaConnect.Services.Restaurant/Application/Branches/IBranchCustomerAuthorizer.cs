namespace NexaConnect.Services.Restaurant.Application.Branches;
public interface IBranchCustomerAuthorizer{Task<bool> IsGrantedAsync(Guid organizationId,Guid? restaurantId,Guid? branchId,string permission,string authorizationHeader,CancellationToken cancellationToken);}
