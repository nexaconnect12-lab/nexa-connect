using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Catalog;

public interface ICustomerBarcodePort
{
    Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct);
    Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,Guid product,SetProductBarcode? update,string token,CancellationToken ct);
}
public sealed class CustomerBarcodeService(ICustomerBarcodePort port)
{
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,Guid branch,Guid product,SetProductBarcode? update,string token,CancellationToken ct)
    {
        var access=await port.AccessAsync(token,ct);
        if(branch==Guid.Empty||product==Guid.Empty||tenant.ApplicationCode!="nexa_connect"||access?.SubjectId!=tenant.SubjectId
            ||!access.Organizations.Any(a=>a.OrganizationId==tenant.OrganizationId&&a.ApplicationCode==tenant.ApplicationCode))
            return new(System.Net.HttpStatusCode.Forbidden);
        return await port.SendAsync(tenant,branch,product,update,token,ct);
    }
}
