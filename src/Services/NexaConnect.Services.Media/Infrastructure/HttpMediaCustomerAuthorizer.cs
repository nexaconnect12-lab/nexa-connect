using NexaConnect.Infrastructure.Authorization;using NexaConnect.Services.Media.Application;
namespace NexaConnect.Services.Media.Infrastructure;
public sealed class HttpMediaCustomerAuthorizer(HttpClient directory,ProductAuthorizationClient authorization,ILogger<HttpMediaCustomerAuthorizer> logger):IMediaCustomerAuthorizer
{
 public async Task<bool> IsGrantedAsync(Guid organizationId,string permission,string header,CancellationToken c){using var request=new HttpRequestMessage(HttpMethod.Get,$"api/platform-directory/v1/organizations/{organizationId:D}/access");request.Headers.TryAddWithoutValidation("Authorization",header);using HttpResponseMessage response=await directory.SendAsync(request,c);if(!response.IsSuccessStatusCode){logger.LogWarning("Media organization access dependency rejected request for organization {OrganizationId}, status {StatusCode}",organizationId,(int)response.StatusCode);return false;}return await authorization.IsGrantedAsync(organizationId,null,null,permission,header,c);}
}
