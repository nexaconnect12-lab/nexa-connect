using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Media;

public interface ICustomerMediaLibraryPort
{
    Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct);
    Task<HttpResponseMessage> SendAsync(Guid organization,Guid? asset,string search,int page,bool readyOnly,string token,CancellationToken ct);
}
public sealed class CustomerMediaLibrary(ICustomerMediaLibraryPort port)
{
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,Guid? asset,string search,int page,bool readyOnly,string token,CancellationToken ct)
    {
        search=search?.Trim()??"";
        if(search.Length>100||search.Any(char.IsControl)||page is <1 or >10000||asset==Guid.Empty)throw new ArgumentException();
        var access=await port.AccessAsync(token,ct);
        if(tenant.OrganizationId==Guid.Empty||tenant.ApplicationCode!="nexa_connect"||access?.SubjectId!=tenant.SubjectId
            ||access.Organizations is null||!access.Organizations.Any(o=>o is not null&&o.OrganizationId==tenant.OrganizationId&&o.ApplicationCode==tenant.ApplicationCode))
            return new(System.Net.HttpStatusCode.Forbidden);
        return await port.SendAsync(tenant.OrganizationId,asset,search,page,readyOnly,token,ct);
    }
}
