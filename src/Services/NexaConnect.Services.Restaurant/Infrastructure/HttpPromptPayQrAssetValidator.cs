using NexaConnect.Services.Restaurant.Application.Configuration;
namespace NexaConnect.Services.Restaurant.Infrastructure;

public sealed class HttpPromptPayQrAssetValidator(HttpClient client,ILogger<HttpPromptPayQrAssetValidator> logger):IPromptPayQrAssetValidator
{
    public async Task<bool> ReadyAsync(Guid org,Guid branch,Guid asset,string bearer,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/media/v1/customer/organizations/{org:D}/branches/{branch:D}/promptpay-assets/{asset:D}");
        request.Headers.TryAddWithoutValidation("Authorization",bearer);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)logger.LogWarning("PromptPay asset validation rejected {StatusCode}",(int)response.StatusCode);
        if((int)response.StatusCode>=500)throw new HttpRequestException("Media unavailable.");
        return response.IsSuccessStatusCode;
    }
}
