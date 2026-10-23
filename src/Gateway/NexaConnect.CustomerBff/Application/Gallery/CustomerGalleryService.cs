using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Gallery;

public interface ICustomerGalleryPort
{
    Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct);
    Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,UpdateBranchGallery? update,string token,CancellationToken ct);
}
public sealed class CustomerGalleryService(ICustomerGalleryPort port)
{
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,Guid branch,UpdateBranchGallery? update,string token,CancellationToken ct)
    {
        var access=await port.AccessAsync(token,ct);
        if(branch==Guid.Empty||tenant.ApplicationCode!="nexa_connect"||access?.SubjectId!=tenant.SubjectId
            || !access.Organizations.Any(a=>a.OrganizationId==tenant.OrganizationId&&a.ApplicationCode==tenant.ApplicationCode))
            return new(System.Net.HttpStatusCode.Forbidden);
        return await port.SendAsync(tenant,branch,update,token,ct);
    }
}
