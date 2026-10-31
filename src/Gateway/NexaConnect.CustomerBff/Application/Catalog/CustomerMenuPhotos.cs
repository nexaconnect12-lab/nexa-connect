using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
namespace NexaConnect.CustomerBff.Application.Catalog;
public interface ICustomerMenuPhotoPort
{
    Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct);
    Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,Guid product,bool photo,Guid? preview,SetMenuPhoto? input,string token,CancellationToken ct);
}
public sealed class CustomerMenuPhotos(ICustomerMenuPhotoPort port)
{
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,Guid branch,Guid product,bool photo,Guid? preview,SetMenuPhoto? input,string token,CancellationToken ct)
    {
        var access=await port.AccessAsync(token,ct);
        if(branch==Guid.Empty||product==Guid.Empty||tenant.OrganizationId==Guid.Empty||tenant.ApplicationCode!="nexa_connect"||access?.SubjectId!=tenant.SubjectId
            ||access.Organizations is null||!access.Organizations.Any(o=>o is not null&&o.OrganizationId==tenant.OrganizationId&&o.ApplicationCode==tenant.ApplicationCode))return new(System.Net.HttpStatusCode.Forbidden);
        return await port.SendAsync(tenant,branch,product,photo,preview,input,token,ct);
    }
}
