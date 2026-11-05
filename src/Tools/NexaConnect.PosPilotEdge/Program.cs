using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http.Features;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Observability;
using NexaConnect.PosPilotEdge;

var builder=WebApplication.CreateBuilder(args);
if(builder.Configuration.GetValue<bool>("Observability:OtlpEnabled"))throw new InvalidOperationException("Staff pilot OTLP export is disabled pending identity-URL and scope redaction verification.");
builder.AddNexaConnectObservability("nexaconnect-pos-pilot-edge");
// Hosting creates an original-path scope before our middleware. Do not serialize it:
// the safe completion event below includes the shared validated correlation explicitly.
builder.Logging.AddJsonConsole(options=>options.IncludeScopes=false);
if(!builder.Environment.IsDevelopment()&&!builder.Environment.IsEnvironment("Testing"))throw new InvalidOperationException("Staff pilot is private test tooling.");
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics",LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing",LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient",LogLevel.None);
builder.Services.AddSingleton(sp=>sp.GetRequiredService<IConfiguration>().GetSection("StaffPilot").Get<StaffPilotOptions>()??throw new InvalidOperationException("Staff pilot configuration required."));
builder.Services.AddSingleton(new BoundedRequestGate(TimeProvider.System,perKey:1200,global:2400));
builder.Services.AddHttpClient("staff-pilot",client=>client.Timeout=TimeSpan.FromSeconds(25))
    .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false})
    .AddNexaConnectCorrelationPropagation();
if(!builder.Environment.IsEnvironment("Testing"))
{
    var o=builder.Configuration.GetSection("StaffPilot").Get<StaffPilotOptions>()??throw new InvalidOperationException("Staff pilot configuration required.");o.Validate();
    using var pem=X509Certificate2.CreateFromPemFile(o.CertificatePath,o.PrivateKeyPath);
    var cert=X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pfx),null);
    if(!cert.HasPrivateKey||cert.NotBefore.ToUniversalTime()>DateTime.UtcNow||cert.NotAfter.ToUniversalTime()<=DateTime.UtcNow.AddDays(1)
        ||!cert.MatchesHostname(o.ListenAddress,false,false)||!cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e=>e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(oid=>oid.Value=="1.3.6.1.5.5.7.3.1")))throw new InvalidOperationException("Exact valid server-auth certificate required.");
    builder.WebHost.ConfigureKestrel(s=>{s.Limits.MaxRequestBodySize=65536;s.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(10);s.Listen(IPAddress.Parse(o.ListenAddress),new Uri(o.PublicOrigin).Port,l=>l.UseHttps(cert));});
}
var app=builder.Build();
// Also cover host-added configuration providers applied during Build (e.g. TestServer).
if(app.Configuration.GetValue<bool>("Observability:OtlpEnabled"))throw new InvalidOperationException("Staff pilot OTLP export is disabled pending identity-URL and scope redaction verification.");
app.Services.GetRequiredService<StaffPilotOptions>().Validate();
app.Use(async(context,next)=>{
    string? raw=context.Features.Get<IHttpRequestFeature>()?.RawTarget;
    context.Items["StaffTarget"]=string.IsNullOrEmpty(raw)?context.Request.Path+context.Request.QueryString:raw;
    context.Request.Path="/staff-edge";context.Request.QueryString=QueryString.Empty;
    foreach(var name in new[]{"url.query","url.full","http.target","http.url"})Activity.Current?.SetTag(name,null);
    Activity.Current?.SetTag("url.path","/staff-edge");
    var front=new Uri(context.RequestServices.GetRequiredService<StaffPilotOptions>().PublicOrigin);
    Activity.Current?.SetTag("server.address",front.Host);Activity.Current?.SetTag("server.port",front.Port);Activity.Current?.SetTag("http.host",front.Authority);
    await next();
});
app.UseNexaConnectRequestLogging();
app.Use(async(context,next)=>{try{await next();}finally{context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("StaffPilotBoundary").LogInformation("Staff pilot request completed {StatusCode} {CorrelationId}",context.Response.StatusCode,context.Items[CorrelationLoggingMiddleware.ItemName]);}});
app.Run(async context=>{
    var o=context.RequestServices.GetRequiredService<StaffPilotOptions>();var logger=context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("StaffPilotBoundary");
    var target=(string)context.Items["StaffTarget"]!;var route=o.Route(context.Request.Method,target);
    context.Response.Headers.CacheControl="no-store";context.Response.Headers["X-Content-Type-Options"]="nosniff";
    // Preserve the browser's same-origin form Origin while disclosing no referrer path/query.
    // no-referrer makes Chromium form POSTs send Origin:null, which our CSRF gate rejects.
    context.Response.Headers["Referrer-Policy"]="strict-origin";context.Response.Headers["X-Frame-Options"]="DENY";
    if(!context.Request.IsHttps||context.Request.Host.Value!=new Uri(o.PublicOrigin).Authority||!o.ClientAllowed(context.Connection.RemoteIpAddress)||route is null)
    {logger.LogWarning("Staff pilot host, client or route denied");context.Response.StatusCode=404;return;}
    if(!context.RequestServices.GetRequiredService<BoundedRequestGate>().Admit(context.Connection.RemoteIpAddress!.ToString())){context.Response.StatusCode=429;context.Response.Headers.RetryAfter="60";return;}
    if(context.Request.Headers.Origin.Count>0&&context.Request.Headers.Origin!=o.PublicOrigin){logger.LogWarning("Staff pilot origin denied");context.Response.StatusCode=403;return;}
    if(context.Request.ContentLength>65536){context.Response.StatusCode=413;return;}
    // Business APIs retain bearer authorization in their owning service; browser cookies never authorize them.
    bool gallery=context.Request.Method=="GET"&&System.Text.RegularExpressions.Regex.IsMatch(target,"\\A/api/media/v1/gallery-readers/organizations/[a-fA-F0-9-]{36}/branches/[a-fA-F0-9-]{36}\\z")
        &&context.Request.Headers["X-Nexa-Gallery-Reader"].Count==1&&context.Request.Headers["X-Nexa-Gallery-Reader"].ToString().Length is >0 and <=4096;
    if(route!="identity"&&!gallery&&(!context.Request.Headers.Authorization.ToString().StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)||context.Request.Headers.Authorization.ToString().Length>16384))
    {logger.LogWarning("Staff pilot bearer missing");context.Response.StatusCode=401;return;}
    try
    {
        using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),new Uri(new Uri(o.Backends[route]),target));
        request.Headers.Host=new Uri(o.PublicOrigin).Authority;
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto","https");request.Headers.TryAddWithoutValidation("X-Forwarded-Host",new Uri(o.PublicOrigin).Authority);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Port",new Uri(o.PublicOrigin).Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Forwarded-For",context.Connection.RemoteIpAddress!.MapToIPv4().ToString());
        if(context.Request.Headers.Origin==o.PublicOrigin)request.Headers.TryAddWithoutValidation("Origin",o.PublicOrigin);
        if(route=="identity"){
            if(context.Request.Headers.Cookie.ToString().Length>16384){context.Response.StatusCode=400;return;}
            if(context.Request.Headers.Cookie.Count>0)request.Headers.TryAddWithoutValidation("Cookie",context.Request.Headers.Cookie.ToString());
        }
        foreach(var name in new[]{"Authorization","X-Nexa-Organization-Id","X-Nexa-Application-Code","Accept"})
            if(context.Request.Headers.TryGetValue(name,out var value)&&value.Count==1&&value.ToString().Length<=16384)request.Headers.TryAddWithoutValidation(name,value.ToString());
        if(route!="identity")foreach(var name in new[]{"X-Nexa-Terminal-Id","X-Client-Operation-Id"})
        {
            if(!context.Request.Headers.TryGetValue(name,out var value))continue;
            if(value.Count!=1||!Guid.TryParse(value.ToString(),out var id)||id==Guid.Empty){context.Response.StatusCode=400;return;}
            request.Headers.TryAddWithoutValidation(name,id.ToString("D"));
        }
        if(gallery)request.Headers.TryAddWithoutValidation("X-Nexa-Gallery-Reader",context.Request.Headers["X-Nexa-Gallery-Reader"].ToString());
        if(context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE"){
            using var input=new MemoryStream();byte[] buffer=new byte[4096];int count;
            while((count=await context.Request.Body.ReadAsync(buffer,context.RequestAborted))>0){if(input.Length+count>65536){context.Response.StatusCode=413;return;}input.Write(buffer,0,count);}
            request.Content=new ByteArrayContent(input.ToArray());if(context.Request.ContentType is {} type)request.Content.Headers.TryAddWithoutValidation("Content-Type",type);
        }else if(context.Request.ContentLength>0||context.Request.Headers.TransferEncoding.Count>0){context.Response.StatusCode=400;return;}
        using var response=await context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("staff-pilot").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,context.RequestAborted);
        if((int)response.StatusCode>=500)throw new HttpRequestException();
        if(response.Headers.Location is {} location){
            // Only the existing native callback or this exact staff origin can be returned by identity.
            bool native=location.IsAbsoluteUri&&location.Scheme=="nexaconnect-pos"&&location.Host=="oauth"&&location.AbsolutePath=="/callback"&&location.UserInfo==""&&location.Fragment=="";
            bool same=Uri.TryCreate(new Uri(o.PublicOrigin),location,out var absolute)&&absolute.GetLeftPart(UriPartial.Authority)==o.PublicOrigin;
            if(route!="identity"||!native&&!same)throw new HttpRequestException();
            context.Response.Headers.Location=location.ToString();
        }
        await response.Content.LoadIntoBufferAsync(route=="identity"?2*1024*1024:32*1024*1024,context.RequestAborted);
        context.Response.StatusCode=(int)response.StatusCode;context.Response.ContentType=response.Content.Headers.ContentType?.ToString()??"application/octet-stream";
        if(route=="identity"&&response.Headers.TryGetValues("Set-Cookie",out var cookies))foreach(var cookie in cookies)context.Response.Headers.Append("Set-Cookie",cookie);
        if(route=="identity"&&response.Headers.TryGetValues("Content-Security-Policy",out var policies))foreach(var policy in policies)context.Response.Headers.Append("Content-Security-Policy",policy);
        if(response.Headers.WwwAuthenticate.Count>0)context.Response.Headers.WWWAuthenticate=response.Headers.WwwAuthenticate.ToString();
        logger.LogInformation("Staff pilot upstream completed {Upstream} {StatusCode}",route,(int)response.StatusCode);
        await response.Content.CopyToAsync(context.Response.Body,context.RequestAborted);
    }
    catch(Exception e)when(!context.RequestAborted.IsCancellationRequested){logger.LogWarning("Staff pilot upstream unavailable {Upstream} {FailureKind}",route,e.GetType().Name);if(!context.Response.HasStarted){context.Response.Headers.Remove("Location");context.Response.StatusCode=503;}}
});
app.Run();
public partial class PosPilotProgram;
