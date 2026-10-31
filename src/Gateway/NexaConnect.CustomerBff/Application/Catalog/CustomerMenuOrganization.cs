using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
namespace NexaConnect.CustomerBff.Application.Catalog;
public interface ICustomerMenuOrganizationPort
{
    Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct);
    Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,UpdateMenuOrganization? input,string token,CancellationToken ct);
}
public sealed class CustomerMenuOrganization(ICustomerMenuOrganizationPort port)
{
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,Guid branch,UpdateMenuOrganization? input,string token,CancellationToken ct)
    {
        var access=await port.AccessAsync(token,ct);
        if(branch==Guid.Empty||tenant.OrganizationId==Guid.Empty||tenant.ApplicationCode!="nexa_connect"||access?.SubjectId!=tenant.SubjectId
            ||access.Organizations is null||!access.Organizations.Any(o=>o is not null&&o.OrganizationId==tenant.OrganizationId&&o.ApplicationCode==tenant.ApplicationCode))return new(System.Net.HttpStatusCode.Forbidden);
        return await port.SendAsync(tenant,branch,input,token,ct);
    }
}
