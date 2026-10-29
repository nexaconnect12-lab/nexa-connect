using System.Net.Http;
using Microsoft.Extensions.Logging;
using NexaConnect.Observability;

namespace NexaConnect.POS;

// Header-only, anonymous reachability checks. Never consumes a token, response body or financial API.
public sealed class PosTerminalDiagnostics : IDisposable
{
    private readonly HttpClient client;
    private readonly PosClientConfiguration configuration;
    private readonly ILoggerFactory logs=NexaConnectObservabilityExtensions.CreateClientLoggerFactory("nexaconnect-pos-client");
    public PosTerminalDiagnostics(PosClientConfiguration configuration,HttpMessageHandler? handler=null)
    {
        configuration.ValidateCheckout();this.configuration=configuration;
        client=new(handler??new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(4)};
    }
    public TerminalServiceCheck[] Initial()=>Endpoints().Select(e=>new TerminalServiceCheck(e.Name,!string.IsNullOrWhiteSpace(e.Url),TerminalReadiness.Untested,null,null)).ToArray();
    public async Task<TerminalServiceCheck[]> CheckAsync(CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(6));
        return await Task.WhenAll(Endpoints().Select(e=>CheckOne(e.Name,e.Url,deadline.Token)));
    }
    private (TerminalService Name,string Url)[] Endpoints()=>[
        (TerminalService.Identity,configuration.Authority),(TerminalService.Pos,configuration.PosApi),
        (TerminalService.Order,configuration.OrderApi),(TerminalService.Catalog,configuration.CatalogApi),
        (TerminalService.Payment,string.IsNullOrWhiteSpace(configuration.PaymentApi)?configuration.OrderApi:configuration.PaymentApi),
        (TerminalService.Media,configuration.MediaApi)];
    private async Task<TerminalServiceCheck> CheckOne(TerminalService service,string url,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(url))return new(service,false,TerminalReadiness.Untested,null,null);
        Guid correlation=Guid.NewGuid();var logger=logs.CreateLogger("NexaConnect.POS.TerminalDiagnostics");
        try
        {
            var target=service==TerminalService.Identity?new Uri(url.TrimEnd('/')+"/.well-known/openid-configuration"):
                new Uri(new Uri(url).GetLeftPart(UriPartial.Authority)+"/");
            using var request=new HttpRequestMessage(HttpMethod.Get,target);request.Headers.Add("X-Correlation-ID",correlation.ToString("D"));
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            int status=(int)response.StatusCode;bool available=status is >=200 and <300 or >=400 and <500;
            if(!available)logger.LogWarning("Terminal reachability rejected {Service} {StatusCode} {CorrelationId}",service,status,correlation);
            return new(service,true,available?TerminalReadiness.Detected:TerminalReadiness.Unavailable,status,DateTimeOffset.UtcNow);
        }
        catch(Exception e)when(e is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning("Terminal reachability failed {Service} {CorrelationId}",service,correlation);
            return new(service,true,TerminalReadiness.Unavailable,null,DateTimeOffset.UtcNow);
        }
    }
    public void Dispose(){client.Dispose();logs.Dispose();}
}
