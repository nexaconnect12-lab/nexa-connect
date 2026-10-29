using System.Net;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Media.Application;
namespace NexaConnect.Services.Media.Infrastructure;

public sealed class PromptPayMediaOwnerValidator(HttpMediaOwnerValidator catalog,IHttpClientFactory clients,IServiceWorkloadTokenProvider tokens,ILogger<PromptPayMediaOwnerValidator> logger):IMediaOwnerValidator
{
    public async Task<bool> ExistsAsync(Guid org,string owner,string kind,Guid id,CancellationToken ct)
    {
        if(owner=="catalog"&&kind=="product")return await catalog.ExistsAsync(org,owner,kind,id,ct);
        if(owner!="restaurant"||kind!="promptpay_qr")return false;
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/restaurant/v1/internal/organizations/{org:D}/branches/{id:D}/promptpay-owner");
        request.Headers.Authorization=new("Bearer",await tokens.GetAsync(ct));
        using var response=await clients.CreateClient("PromptPayRestaurant").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.IsSuccessStatusCode)return true;
        if(response.StatusCode==HttpStatusCode.NotFound)return false;
        logger.LogWarning("PromptPay media owner boundary rejected {StatusCode}",(int)response.StatusCode);
        throw new HttpRequestException("Restaurant owner validation unavailable.");
    }
}
