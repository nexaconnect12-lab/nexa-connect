using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.Platform;
namespace NexaConnect.CustomerBff.Application.PromptPay;

public interface ICustomerPromptPayQrPort
{
    Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct);
    Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,UpdateBranchPromptPayQrV1? update,bool image,string token,CancellationToken ct);
    Task<HttpResponseMessage> UploadAsync(TenantContext tenant,Guid branch,Guid? asset,StartPromptPayQrUploadV1? start,CompletePromptPayQrUploadV1? complete,string token,CancellationToken ct);
}
public sealed class CustomerPromptPayQrService(ICustomerPromptPayQrPort port)
{
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,Guid branch,UpdateBranchPromptPayQrV1? update,bool image,string token,CancellationToken ct)
    {
        var access=await port.AccessAsync(token,ct);
        if(branch==Guid.Empty||tenant.ApplicationCode!="nexa_connect"||access?.SubjectId!=tenant.SubjectId
            ||!access.Organizations.Any(a=>a.OrganizationId==tenant.OrganizationId&&a.ApplicationCode==tenant.ApplicationCode))return new(System.Net.HttpStatusCode.Forbidden);
        return await port.SendAsync(tenant,branch,update,image,token,ct);
    }
    public async Task<HttpResponseMessage> UploadAsync(TenantContext tenant,Guid branch,Guid? asset,StartPromptPayQrUploadV1? start,CompletePromptPayQrUploadV1? complete,string token,CancellationToken ct)
    {
        var access=await port.AccessAsync(token,ct);
        if(branch==Guid.Empty||tenant.ApplicationCode!="nexa_connect"||access?.SubjectId!=tenant.SubjectId
            ||!access.Organizations.Any(a=>a.OrganizationId==tenant.OrganizationId&&a.ApplicationCode==tenant.ApplicationCode))return new(System.Net.HttpStatusCode.Forbidden);
        if(start is null&&(asset is null||asset==Guid.Empty||complete is null)||start is not null&&(asset is not null||complete is not null))return new(System.Net.HttpStatusCode.BadRequest);
        return await port.UploadAsync(tenant,branch,asset,start,complete,token,ct);
    }
}
