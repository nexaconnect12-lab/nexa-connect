using System.Net;
using System.Net.Http;
using System.IO;
namespace NexaConnect.POS;
public sealed partial class PosApiClient
{
    public async Task<byte[]?> GetMenuPhotoAsync(PosTokenSet token,Guid product,CancellationToken ct=default)
    {
        if(product==Guid.Empty)throw new ArgumentException();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var request=CreateRequest(HttpMethod.Get,$"api/catalog/v1/customer/branches/{_configuration.BranchId:D}/products/{product:D}/photo",token);
        AddTenantContext(request,_configuration.OrganizationId);
        using var response=await SendCheckoutAsync(_catalogHttpClient,request,"catalog.menu-photo",Guid.NewGuid(),deadline.Token,true);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        if(!response.IsSuccessStatusCode)throw new PosApiException((int)response.StatusCode,"Menu photo unavailable.");
        if(response.Content.Headers.ContentType?.MediaType!="image/png"||response.Content.Headers.ContentLength>524288)throw new InvalidDataException();
        await response.Content.LoadIntoBufferAsync(524288,deadline.Token);var bytes=await response.Content.ReadAsByteArrayAsync(deadline.Token);if(bytes.Length>524288)throw new InvalidDataException();return bytes;
    }
}
