using NexaConnect.Contracts.Platform;
namespace NexaConnect.CustomerBff.Application.DayClose;
public interface ICustomerDayCutoffPort : ICustomerDayClosePort;
public sealed class CustomerDayCutoff(ICustomerDayCutoffPort port)
{
    private readonly CustomerDayClose workflow=new(port);
    public Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,DayCloseRequest request,bool prepare,CancellationToken ct)=>workflow.ExecuteAsync(tenant,token,request,prepare,ct);
}
