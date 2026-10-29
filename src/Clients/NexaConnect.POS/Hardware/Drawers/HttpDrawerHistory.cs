using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using NexaConnect.Contracts.POS;
namespace NexaConnect.POS.Hardware.Drawers;

public sealed partial class HttpDrawerAuthority
{
    public async Task<DrawerHistoryPageV1> ListAsync(string subject,DateTimeOffset from,DateTimeOffset to,string? cursor,CancellationToken ct)
    {
        if(from>=to||to-from>TimeSpan.FromDays(31)||to>DateTimeOffset.UtcNow.AddSeconds(5)||cursor?.Length>256)throw new ArgumentException();
        var correlation=Guid.NewGuid();using var scope=logger.BeginScope(new Dictionary<string,object>{{"CorrelationId",correlation.ToString("D")}});
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(10));ct=deadline.Token;
        using var call=HistoryRequest(subject,null,correlation);call.RequestUri=new Uri(call.RequestUri!.OriginalString+$"&fromUtc={Uri.EscapeDataString(from.ToUniversalTime().ToString("O"))}&toUtc={Uri.EscapeDataString(to.ToUniversalTime().ToString("O"))}&limit=25"+(cursor is null?"":"&cursor="+Uri.EscapeDataString(cursor)),UriKind.Relative);
        using var response=await Send(call,ct);if(!response.IsSuccessStatusCode)throw new HttpRequestException("Drawer history unavailable.",null,response.StatusCode);
        try{await response.Content.LoadIntoBufferAsync(65536,ct);var page=await response.Content.ReadFromJsonAsync<DrawerHistoryPageV1>(ct)??throw new InvalidDataException();DrawerHistoryReader.ValidatePage(configuration,subject,page,from,to);return page;}
        catch(Exception e)when(e is InvalidDataException or System.Text.Json.JsonException or HttpRequestException or ArgumentException or OperationCanceledException){logger.LogWarning("Drawer history response rejected; category {Category}",e.GetType().Name);throw;}
    }
    public async Task<DrawerHistoryItemV1?> ReviewAsync(string subject,Guid operation,CancellationToken ct)
    {
        if(operation==Guid.Empty)throw new ArgumentException();
        using var scope=logger.BeginScope(new Dictionary<string,object>{{"CorrelationId",operation.ToString("D")}});
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(10));ct=deadline.Token;
        using var call=HistoryRequest(subject,operation,operation);using var response=await Send(call,ct);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        if(!response.IsSuccessStatusCode)throw new HttpRequestException("Original drawer review unavailable.",null,response.StatusCode);
        try{await response.Content.LoadIntoBufferAsync(4096,ct);var item=await response.Content.ReadFromJsonAsync<DrawerHistoryItemV1>(ct)??throw new InvalidDataException();if(item.OperationId!=operation)throw new InvalidDataException();return item;}
        catch(Exception e)when(e is InvalidDataException or System.Text.Json.JsonException or HttpRequestException or OperationCanceledException){logger.LogWarning("Drawer history response rejected; category {Category}",e.GetType().Name);throw;}
    }
    private HttpRequestMessage HistoryRequest(string subject,Guid? operation,Guid correlation)
    {
        var current=RequireIdentity(subject);var c=configuration;
        var call=new HttpRequestMessage(HttpMethod.Get,$"api/pos/v1/customer/organizations/{c.OrganizationId:D}/drawer-intents"+(operation is {} op?$"/{op:D}/review":"")+$"?restaurantId={c.RestaurantId:D}&branchId={c.BranchId:D}&storeId={c.StoreId:D}&terminalId={c.TerminalId:D}");
        call.Headers.Authorization=new("Bearer",current.AccessToken);call.Headers.Add("X-Correlation-ID",correlation.ToString("D"));return call;
    }
}
