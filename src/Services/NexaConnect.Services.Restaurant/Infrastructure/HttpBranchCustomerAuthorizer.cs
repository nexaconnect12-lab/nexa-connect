using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Restaurant.Application.Branches;
namespace NexaConnect.Services.Restaurant.Infrastructure;
public sealed class HttpBranchCustomerAuthorizer(HttpClient directory,ProductAuthorizationClient authorization):IBranchCustomerAuthorizer
{
 public async Task<bool> IsGrantedAsync(Guid organizationId,Guid? restaurantId,Guid? branchId,string permission,string authorizationHeader,CancellationToken cancellationToken){using var request=new HttpRequestMessage(HttpMethod.Get,$"api/platform-directory/v1/organizations/{organizationId:D}/access");request.Headers.TryAddWithoutValidation("Authorization",authorizationHeader);using HttpResponseMessage response=await directory.SendAsync(request,cancellationToken);return response.IsSuccessStatusCode&&await authorization.IsGrantedAsync(organizationId,restaurantId,branchId,permission,authorizationHeader,cancellationToken);}
}
