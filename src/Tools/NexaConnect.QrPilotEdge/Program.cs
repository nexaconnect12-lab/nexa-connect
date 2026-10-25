using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Observability;
using NexaConnect.QrPilotEdge;

var builder=WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-qr-pilot-edge");
if(!builder.Environment.IsDevelopment()&&!builder.Environment.IsEnvironment("Testing"))throw new InvalidOperationException("QR pilot edge is local test tooling only.");
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing",LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient",LogLevel.None);
builder.Services.AddSingleton(services=>services.GetRequiredService<IConfiguration>().GetSection("QrPilot").Get<PilotOptions>()??throw new InvalidOperationException("QR pilot configuration required."));
builder.Services.AddSingleton(new BoundedRequestGate(TimeProvider.System,perKey:120,global:1000));
builder.Services.AddHttpClient("pilot",(services,client)=>{var options=services.GetRequiredService<PilotOptions>();options.Validate();client.BaseAddress=new Uri(options.BackendOrigin);client.Timeout=TimeSpan.FromSeconds(20);})
    .ConfigurePrimaryHttpMessageHandler(services=>new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,ServerCertificateCustomValidationCallback=(_,cert,_,errors)=>services.GetRequiredService<PilotOptions>().BackendCertificate(cert,errors)})
    .AddNexaConnectCorrelationPropagation();
// TestServer supplies its own listener. Actual startup always binds one explicit private IPv4.
if(!builder.Environment.IsEnvironment("Testing"))
{
    var options=builder.Configuration.GetSection("QrPilot").Get<PilotOptions>()??throw new InvalidOperationException("QR pilot configuration required.");options.Validate();
    using var pem=X509Certificate2.CreateFromPemFile(options.CertificatePath,options.PrivateKeyPath);
    var certificate=X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pfx),null);
    if(!certificate.HasPrivateKey||certificate.NotBefore.ToUniversalTime()>DateTime.UtcNow||certificate.NotAfter.ToUniversalTime()<=DateTime.UtcNow.AddDays(1)
        ||!certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e=>e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o=>o.Value=="1.3.6.1.5.5.7.3.1"))
        ||!certificate.MatchesHostname(new Uri(options.PublicOrigin).Host,allowWildcards:false,allowCommonName:false))throw new InvalidOperationException("QR pilot certificate does not match its exact origin or lifetime.");
    builder.WebHost.ConfigureKestrel(server=>{server.Limits.MaxRequestBodySize=8192;server.Limits.MaxRequestHeadersTotalSize=16384;server.Limits.MaxConcurrentConnections=100;server.Listen(IPAddress.Parse(options.ListenAddress),new Uri(options.PublicOrigin).Port,listen=>listen.UseHttps(certificate));});
}
var app=builder.Build();
app.Use(async(context,next)=>
{
    string? raw=context.Features.Get<IHttpRequestFeature>()?.RawTarget;
    context.Items["PilotTarget"]=string.IsNullOrEmpty(raw)?context.Request.Path.Value+context.Request.QueryString.Value:raw;
    context.Request.Path="/guest-edge"; // Shared telemetry must never retain attacker-chosen URL paths or queries.
    context.Request.QueryString=QueryString.Empty;
    var publicAddress=new Uri(context.RequestServices.GetRequiredService<PilotOptions>().PublicOrigin);
    Activity.Current?.SetTag("server.address",publicAddress.Host);Activity.Current?.SetTag("server.port",publicAddress.Port);Activity.Current?.SetTag("http.host",publicAddress.Authority);
    Activity.Current?.SetTag("url.path","/guest-edge");Activity.Current?.SetTag("url.query",null);Activity.Current?.SetTag("url.full",null);Activity.Current?.SetTag("http.target","/guest-edge");Activity.Current?.SetTag("http.url",null);
    await next();
});
app.UseNexaConnectRequestLogging();
app.Run(async context=>
{
    const string cookie="__Host-nexa-qr-guest";
    var options=context.RequestServices.GetRequiredService<PilotOptions>();options.Validate();
    var logger=context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PilotBoundary");
    context.Response.Headers.CacheControl="no-store";context.Response.Headers["Referrer-Policy"]="no-referrer";context.Response.Headers["X-Content-Type-Options"]="nosniff";
    string target=(string)context.Items["PilotTarget"]!;
    if(!context.Request.IsHttps||!string.Equals(context.Request.Host.Value,new Uri(options.PublicOrigin).Authority,StringComparison.OrdinalIgnoreCase)
        ||!PilotOptions.Allowed(context.Request.Method,target))
    {logger.LogWarning("QR pilot route or host denied");context.Response.StatusCode=404;return;}
    if(context.Request.Method is "POST" or "DELETE"&&context.Request.Headers.Origin!=options.PublicOrigin)
    {logger.LogWarning("QR pilot mutation origin denied");context.Response.StatusCode=403;return;}
    if(context.Request.Method=="POST"&&!context.Request.HasJsonContentType()){context.Response.StatusCode=415;return;}
    if(context.Request.Method=="GET"&&(context.Request.ContentLength>0||context.Request.Headers.TransferEncoding.Count>0)){context.Response.StatusCode=400;return;}
    if(!context.RequestServices.GetRequiredService<BoundedRequestGate>().Admit(context.Connection.RemoteIpAddress?.ToString()??"unknown"))
    {logger.LogWarning("QR pilot admission limited");context.Response.StatusCode=429;context.Response.Headers.RetryAfter="60";return;}
    try
    {
        using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),target);
        request.Headers.Host=new Uri(options.PublicOrigin).Authority; // Trusted fixed Host preserves the BFF's exact-origin guard.
        if(context.Request.Method=="POST")
        {
            using var input=new MemoryStream();var buffer=new byte[1024];int read;
            while((read=await context.Request.Body.ReadAsync(buffer,context.RequestAborted))>0){if(input.Length+read>8192){context.Response.StatusCode=413;return;}input.Write(buffer,0,read);}
            request.Content=new ByteArrayContent(input.ToArray());request.Content.Headers.ContentType=new("application/json");
        }
        if(context.Request.Headers.Origin==options.PublicOrigin)request.Headers.TryAddWithoutValidation("Origin",options.PublicOrigin);
        string? token=context.Request.Cookies[cookie];
        if(token?.Length==43&&token.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_'))request.Headers.TryAddWithoutValidation("Cookie",cookie+"="+token);
        using var response=await context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("pilot").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,context.RequestAborted);
        if((int)response.StatusCode is >=300 and <400 or >=500)throw new HttpRequestException("Pilot upstream response unavailable.");
        await response.Content.LoadIntoBufferAsync(4*1024*1024,context.RequestAborted);
        context.Response.StatusCode=(int)response.StatusCode;
        context.Response.ContentType=response.Content.Headers.ContentType?.ToString()??"application/octet-stream";
        if(response.Headers.TryGetValues("Set-Cookie",out var cookies))foreach(string value in cookies)
        {
            if(SetCookieHeaderValue.TryParse(value,out var parsed)&&parsed.Name==cookie&&!parsed.Domain.HasValue&&parsed.Path=="/"&&parsed.Secure&&parsed.HttpOnly&&parsed.SameSite==Microsoft.Net.Http.Headers.SameSiteMode.Strict
                &&(parsed.Value.Length==0||parsed.Value.Length==43&&parsed.Value.Value!.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_')))
                context.Response.Headers.Append("Set-Cookie",parsed.ToString());
        }
        await response.Content.CopyToAsync(context.Response.Body,context.RequestAborted);
    }
    catch(Exception e)when(!context.RequestAborted.IsCancellationRequested)
    {logger.LogWarning("QR pilot upstream unavailable; category {Category}",e.GetType().Name);if(!context.Response.HasStarted)context.Response.StatusCode=503;}
});
app.Run();
public partial class QrPilotProgram;
