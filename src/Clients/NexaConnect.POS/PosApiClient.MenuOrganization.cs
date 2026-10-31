using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using NexaConnect.Contracts.Catalog;
namespace NexaConnect.POS;
public sealed partial class PosApiClient
{
    public async Task<BranchMenuOrganization> GetMenuOrganizationAsync(PosTokenSet token,CancellationToken ct=default)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var request=CreateRequest(HttpMethod.Get,$"api/catalog/v1/customer/branches/{_configuration.BranchId:D}/menu-organization",token);AddTenantContext(request,_configuration.OrganizationId);
        using var response=await SendCheckoutAsync(_catalogHttpClient,request,"catalog.menu-organization",Guid.NewGuid(),deadline.Token,true);
        if(!response.IsSuccessStatusCode)throw new PosApiException((int)response.StatusCode,"Menu categories unavailable.");
        if(response.Content.Headers.ContentLength>65536||response.Content.Headers.ContentType?.MediaType!="application/json")throw new InvalidDataException();await response.Content.LoadIntoBufferAsync(65536,deadline.Token);
        var layout=await response.Content.ReadFromJsonAsync<BranchMenuOrganization>(deadline.Token)??throw new InvalidDataException();PosMenuOrganizationView.Validate(layout,_configuration.OrganizationId,_configuration.BranchId);return layout;
    }
}
