using System.Net;
using NexaConnect.Contracts.Media;
using NexaConnect.Services.Media.Application;
namespace NexaConnect.Services.Media.Infrastructure;

public sealed class HttpBranchPromptPayQrSelection(HttpClient client,ILogger<HttpBranchPromptPayQrSelection> logger):IBranchPromptPayQrSelectionPort
{
    public async Task<BranchPromptPayQrV1> ReadAsync(Guid org,Guid branch,string bearer,CancellationToken ct,bool manage=false)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/restaurant/v1/customer/organizations/{org:D}/branches/{branch:D}/promptpay-qr{(manage?"/manage-access":"")}");
        request.Headers.TryAddWithoutValidation("Authorization",bearer);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)logger.LogWarning("PromptPay selection boundary rejected {StatusCode}",(int)response.StatusCode);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new UnauthorizedAccessException();
        if(response.StatusCode==HttpStatusCode.NotFound)throw new KeyNotFoundException();
        response.EnsureSuccessStatusCode();await response.Content.LoadIntoBufferAsync(16384,ct);
        var value=await response.Content.ReadFromJsonAsync<BranchPromptPayQrV1>(ct)??throw new InvalidDataException();
        if(value.OrganizationId!=org||value.BranchId!=branch||value.RestaurantId==Guid.Empty||value.Version<1)throw new InvalidDataException("Invalid branch scope.");
        return value;
    }
}
