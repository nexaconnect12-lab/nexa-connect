namespace NexaConnect.Services.Reporting.Application;

public sealed record ReportingBranchScope(Guid OrganizationId,Guid RestaurantId,Guid BranchId);
public interface IReportingAccessDependencies
{
    Task<bool> HasOrganizationAccessAsync(Guid organizationId,string authorizationHeader,CancellationToken cancellationToken);
    Task<ReportingBranchScope?> GetBranchScopeAsync(Guid branchId,CancellationToken cancellationToken);
    Task<bool> HasPermissionAsync(Guid organizationId,Guid? restaurantId,Guid? branchId,string permission,string authorizationHeader,CancellationToken cancellationToken);
}

public sealed class ReportingCustomerAuthorizer(IReportingAccessDependencies dependencies):IReportingCustomerAuthorizer
{
    public async Task<bool> IsGrantedAsync(Guid organizationId,Guid? branchId,string permission,string authorizationHeader,CancellationToken cancellationToken)
    {
        if(organizationId==Guid.Empty||branchId==Guid.Empty||string.IsNullOrWhiteSpace(authorizationHeader))return false;
        if(!await dependencies.HasOrganizationAccessAsync(organizationId,authorizationHeader,cancellationToken))return false;
        Guid? restaurantId=null;
        if(branchId is Guid branch)
        {
            var scope=await dependencies.GetBranchScopeAsync(branch,cancellationToken);
            if(scope is null||scope.OrganizationId!=organizationId||scope.BranchId!=branch||scope.RestaurantId==Guid.Empty)return false;
            restaurantId=scope.RestaurantId;
        }
        return await dependencies.HasPermissionAsync(organizationId,restaurantId,branchId,permission,authorizationHeader,cancellationToken);
    }
}
