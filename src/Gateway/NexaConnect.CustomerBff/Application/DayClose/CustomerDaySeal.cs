using NexaConnect.Contracts.Platform;
namespace NexaConnect.CustomerBff.Application.DayClose;
public interface ICustomerDaySealPort : ICustomerDayClosePort;
public sealed class CustomerDaySeal(ICustomerDaySealPort port)
{
    private readonly CustomerDayClose workflow=new(port);
    public Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,DayCloseRequest request,bool prepare,CancellationToken ct)=>
        prepare&&request.ReviewedCutoffVersion is null or <=0?Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)):
        workflow.ExecuteAsync(tenant,token,request,prepare,ct);
}
