using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Infrastructure.Authentication;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Infrastructure;

public sealed class HttpReportingCustomerAuthorizer(IHttpClientFactory clients,IServiceWorkloadTokenProvider tokens, ProductAuthorizationClient authorization) : IReportingAccessDependencies
{
    public async Task<bool> HasOrganizationAccessAsync(Guid organizationId,string authorizationHeader,CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        using HttpResponseMessage response = await clients.CreateClient("PlatformDirectory").SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }
    public async Task<ReportingBranchScope?> GetBranchScopeAsync(Guid branchId,CancellationToken cancellationToken)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/restaurant/v1/branches/{branchId:D}/authorization-scope");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await tokens.GetAsync(cancellationToken));
        using var response=await clients.CreateClient("ReportingRestaurant").SendAsync(request,cancellationToken);
        if(response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ReportingBranchScope>(cancellationToken:cancellationToken);
    }
    public Task<bool> HasPermissionAsync(Guid organizationId,Guid? restaurantId,Guid? branchId,string permission,string authorizationHeader,CancellationToken cancellationToken)
        =>authorization.IsGrantedAsync(organizationId,restaurantId,branchId,permission,authorizationHeader,cancellationToken);
}
