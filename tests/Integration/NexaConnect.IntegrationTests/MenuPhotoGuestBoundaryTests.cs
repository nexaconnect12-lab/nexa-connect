extern alias CUSTOMERBFF;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexaConnect.Infrastructure.Http;
using CUSTOMERBFF::NexaConnect.CustomerBff.Controllers;
namespace NexaConnect.IntegrationTests;
public sealed class MenuPhotoGuestBoundaryTests
{
    [Theory][InlineData(false,false,403)][InlineData(true,false,401)][InlineData(true,true,200)]
    public async Task Photos_require_https_origin_and_guest_cookie_and_never_forward_staff_authority(bool https,bool guest,int code)
    {
        var handler=new Handler();await using var app=await Host(handler);using var client=app.GetTestClient();client.BaseAddress=new(https?"https://qr.test":"http://qr.test");
        client.DefaultRequestHeaders.Authorization=new("Bearer","private-staff");client.DefaultRequestHeaders.Add("X-Nexa-Organization-Id",Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("Cookie","__Host-nexa-customer=private"+(guest?"; __Host-nexa-qr-guest="+new string('S',43):""));
        using var result=await client.GetAsync($"/bff/guest/qr/menu/{Guid.NewGuid()}/photo");Assert.Equal(code,(int)result.StatusCode);
        Assert.Equal(code==200?1:0,handler.Calls);if(code==200){Assert.True(result.Headers.CacheControl?.NoStore);Assert.Equal("image/png",result.Content.Headers.ContentType?.MediaType);}
    }
    [Theory][InlineData("text/html",3)][InlineData("image/png",524289)]
    public async Task Invalid_upstream_images_are_not_returned(string type,int length)
    {
        var handler=new Handler{Type=type,Length=length};await using var app=await Host(handler);using var client=app.GetTestClient();client.BaseAddress=new("https://qr.test");client.DefaultRequestHeaders.Add("Cookie","__Host-nexa-qr-guest="+new string('S',43));
        using var result=await client.GetAsync($"/bff/guest/qr/menu/{Guid.NewGuid()}/photo");Assert.Equal(HttpStatusCode.ServiceUnavailable,result.StatusCode);Assert.NotEqual("image/png",result.Content.Headers.ContentType?.MediaType);Assert.InRange((await result.Content.ReadAsByteArrayAsync()).Length,0,2048);
    }
    private static async Task<WebApplication> Host(Handler handler)
    {
        var b=WebApplication.CreateBuilder();b.WebHost.UseTestServer();b.Services.AddLogging(l=>l.ClearProviders());b.Services.AddAuthorization();
        b.Services.AddControllers().AddApplicationPart(typeof(QrMenuPhotosController).Assembly);b.Services.Configure<QrGuestOptions>(o=>{o.Enabled=true;o.MenuEnabled=true;o.PublicOrigin="https://qr.test";});
        b.Services.AddKeyedSingleton("menu-photos",new BoundedRequestGate(TimeProvider.System));b.Services.AddSingleton<IHttpClientFactory>(new Clients(handler));
        var app=b.Build();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
    }
    private sealed class Clients(Handler handler):IHttpClientFactory{public HttpClient CreateClient(string name){Assert.Equal("QrGuestOrder",name);return new(handler,false){BaseAddress=new("https://order.test/")};}}
    private sealed class Handler:HttpMessageHandler
    {
        public int Calls,Length=3;public string Type="image/png";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;Assert.Null(request.Headers.Authorization);Assert.False(request.Headers.Contains("Cookie"));Assert.False(request.Headers.Contains("X-Nexa-Organization-Id"));Assert.Equal(new string('S',43),Assert.Single(request.Headers.GetValues("X-Nexa-Guest-Session")));
            var content=new ByteArrayContent(new byte[Length]);content.Headers.ContentType=new(Type);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
        }
    }
}
