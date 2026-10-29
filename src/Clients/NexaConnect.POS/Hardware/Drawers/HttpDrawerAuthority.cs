using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using NexaConnect.Contracts.POS;
using NexaConnect.Observability;
namespace NexaConnect.POS.Hardware.Drawers;

public sealed partial class HttpDrawerAuthority:IDrawerAuthorization,IDrawerHistoryPort,IDisposable
{
    private readonly PosClientConfiguration configuration;
    private readonly Func<PosTokenSet?> token;
    private readonly HttpClient http;
    private readonly ILogger logger;
    internal ILogger HistoryLogger=>logger;
    private readonly ILoggerFactory? ownedLoggers;
    public HttpDrawerAuthority(PosClientConfiguration configuration,Func<PosTokenSet?> token,HttpMessageHandler? handler=null,ILogger? logger=null)
    {
        configuration.ValidateCheckout();this.configuration=configuration;this.token=token;
        if(logger is null){ownedLoggers=NexaConnectObservabilityExtensions.CreateClientLoggerFactory("nexaconnect-pos-client");logger=ownedLoggers.CreateLogger("NexaConnect.POS.Drawer");}this.logger=logger;
        http=new(handler??new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){BaseAddress=new Uri(configuration.PosApi),Timeout=TimeSpan.FromSeconds(10)};
    }
    public async Task<DrawerPermit> AuthorizeAsync(DrawerRequest request,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(10));ct=deadline.Token;
        using var scope=logger.BeginScope(new Dictionary<string,object>{{"CorrelationId",request.OperationId.ToString("D")}});
        using var call=Create(HttpMethod.Post,request,"");
        call.Content=JsonContent.Create(new DrawerIntentRequestV1(request.OrganizationId,request.RestaurantId,request.BranchId,request.StoreId,request.TerminalId,request.ShiftId,request.CashSessionId,request.OperationId,request.Cause==DrawerCause.Manual?"manual":"cash_sale",request.OrderId,Reason(request.Reason)));
        using var response=await Send(call,ct);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)return new(DrawerPermitStatus.Denied);
        if(response.StatusCode==HttpStatusCode.Conflict)return new(DrawerPermitStatus.Conflict);
        if(!response.IsSuccessStatusCode)return new(DrawerPermitStatus.Unavailable);
        var value=await Read(response,ct);
        if(value.MaySubmit){if(value.OperationId!=request.OperationId||value.Status!="reserved")throw Rejected();return new(DrawerPermitStatus.Granted,value.ExpiresAtUtc);}
        return new(DrawerPermitStatus.AlreadyAttempted);
    }
    public async Task ReportAsync(DrawerRequest request,DrawerOutcome outcome,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(10));ct=deadline.Token;
        using var scope=logger.BeginScope(new Dictionary<string,object>{{"CorrelationId",request.OperationId.ToString("D")}});
        string status=outcome switch{DrawerOutcome.Submitted=>"submitted",DrawerOutcome.Unknown=>"unknown",DrawerOutcome.Cancelled=>"cancelled",_=>throw new ArgumentException()};
        using var call=Create(HttpMethod.Post,request,$"/{request.OperationId:D}/outcome");call.Content=JsonContent.Create(new DrawerOutcomeRequestV1(request.OrganizationId,status));
        using var response=await Send(call,ct);if(!response.IsSuccessStatusCode)throw new HttpRequestException("Drawer audit report unavailable.");
        var value=await Read(response,ct);if(value.OperationId!=request.OperationId||value.MaySubmit||value.Status!=status||value.ReportedAtUtc is null)throw Rejected();
    }
    public async Task<DrawerIntentV1> ReadAsync(DrawerRequest request,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(10));ct=deadline.Token;
        using var scope=logger.BeginScope(new Dictionary<string,object>{{"CorrelationId",request.OperationId.ToString("D")}});
        using var call=Create(HttpMethod.Get,request,$"/{request.OperationId:D}");using var response=await Send(call,ct);
        if(!response.IsSuccessStatusCode)throw new HttpRequestException("Original drawer intent is unavailable.");
        var value=await Read(response,ct);if(value.OperationId!=request.OperationId||value.MaySubmit)throw Rejected();return value;
    }
    private HttpRequestMessage Create(HttpMethod method,DrawerRequest request,string suffix)
    {
        request.Validate();var current=RequireIdentity(request.Subject);
        if(request.OrganizationId!=configuration.OrganizationId||request.RestaurantId!=configuration.RestaurantId||request.BranchId!=configuration.BranchId||request.StoreId!=configuration.StoreId||request.TerminalId!=configuration.TerminalId){logger.LogWarning("Drawer client scope rejected");throw new UnauthorizedAccessException();}
        var call=new HttpRequestMessage(method,$"api/pos/v1/customer/organizations/{request.OrganizationId:D}/drawer-intents{suffix}");
        call.Headers.Authorization=new("Bearer",current.AccessToken);call.Headers.Add("X-Correlation-ID",request.OperationId.ToString("D"));
        return call;
    }
    private PosTokenSet RequireIdentity(string subject)
    {
        var current=token();
        if(string.IsNullOrWhiteSpace(subject)||current is null||current.Subject!=subject||current.ExpiresAtUtc<=DateTimeOffset.UtcNow||PosSessionPolicy.IsAbsoluteExpired(current,DateTimeOffset.UtcNow)||current.LockedAtUtc is not null
            ||current.Authority!=configuration.Authority||string.IsNullOrWhiteSpace(current.AccessToken)
            ){logger.LogWarning("Drawer client identity rejected");throw new UnauthorizedAccessException();}
        return current;
    }
    private async Task<HttpResponseMessage> Send(HttpRequestMessage request,CancellationToken ct)
    {
        try{var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);if(!response.IsSuccessStatusCode)logger.LogWarning("Drawer client boundary rejected; status {StatusCode} correlation {CorrelationId}",(int)response.StatusCode,request.Headers.GetValues("X-Correlation-ID").Single());return response;}
        catch(Exception e)when(e is HttpRequestException or OperationCanceledException){logger.LogWarning("Drawer client transport unavailable; category {Category}",e.GetType().Name);throw;}
    }
    private async Task<DrawerIntentV1> Read(HttpResponseMessage response,CancellationToken ct)
    {
        try{
        await response.Content.LoadIntoBufferAsync(4096,ct);var value=await response.Content.ReadFromJsonAsync<DrawerIntentV1>(ct)??throw new InvalidDataException();
        if(value.OperationId==Guid.Empty||value.Status is not ("reserved" or "submitted" or "unknown" or "cancelled")||value.ExpiresAtUtc-value.ReservedAtUtc!=TimeSpan.FromSeconds(60)
            ||value.ReservedAtUtc>DateTimeOffset.UtcNow.AddSeconds(5)||value.ReportedAtUtc<value.ReservedAtUtc)throw new InvalidDataException();
        return value;
        }catch(Exception e)when(e is InvalidDataException or System.Text.Json.JsonException or HttpRequestException or OperationCanceledException){logger.LogWarning("Drawer client response rejected; category {Category}",e.GetType().Name);throw;}
    }
    private InvalidDataException Rejected(){logger.LogWarning("Drawer client response rejected; original intent differs");return new();}
    private static string? Reason(DrawerReason? reason)=>reason switch{DrawerReason.ChangeRequired=>"change_required",DrawerReason.CashCheck=>"cash_check",DrawerReason.SupervisorAccess=>"supervisor_access",_=>null};
    public void Dispose(){http.Dispose();ownedLoggers?.Dispose();}
}
