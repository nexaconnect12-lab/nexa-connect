extern alias CUSTOMERBFF;
using System.Net;
using System.Text;
using System.Text.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.RestaurantQr;
using CUSTOMERBFF::NexaConnect.CustomerBff.Application.Qr;
using CUSTOMERBFF::NexaConnect.CustomerBff.Infrastructure.Qr;

namespace NexaConnect.IntegrationTests;

public sealed class QrGatewayTransportTests
{
    [Fact] public async Task Full_bounded_list_fits_and_guest_calls_never_inherit_staff_credentials()
    {
        using var handler = new Handler(); using var client = new HttpClient(handler) { BaseAddress = new Uri("https://restaurant.test/") };
        var port = new HttpQrGatewayPort(client, new Clients(client));
        handler.Body = JsonSerializer.Serialize(Enumerable.Range(0,200).Select(_ => new QrEntryView(Guid.NewGuid(),new string('A',32),long.MaxValue,true,DateTimeOffset.UtcNow.AddDays(30))),JsonSerializerOptions.Web);
        var result = await port.StaffAsync(new("operator",Guid.NewGuid(),"nexa_connect"),"staff-token",Guid.NewGuid(),QrAdminOperation.List,null,null,default);
        Assert.Equal(200,result.Body!.Value.GetArrayLength()); Assert.Equal("Bearer staff-token",handler.Authorization);
        handler.Body = "{}"; await port.GuestAsync("context",new string('S',43),null,default);
        Assert.Null(handler.Authorization); Assert.False(handler.Cookie); Assert.False(handler.Tenant); Assert.Equal(new string('S',43),handler.Session);
        await port.GuestAsync("start",null,new(new string('E',43)),default); Assert.Null(handler.Authorization); Assert.Null(handler.Session);
        await port.ShoppingAsync("menu",new string('S',43),null,default);Assert.Null(handler.Authorization);Assert.False(handler.Cookie);Assert.False(handler.Tenant);Assert.Equal(new string('S',43),handler.Session);
        await port.ShoppingAsync("quote",new string('S',43),new([new(Guid.NewGuid(),2)]),default);Assert.Null(handler.Authorization);Assert.False(handler.Cookie);Assert.False(handler.Tenant);
        handler.Body="{}";await port.ReviewAsync(new("operator",Guid.NewGuid(),"nexa_connect"),"staff-token",Guid.NewGuid(),"pending",null,null,default);Assert.Equal("Bearer staff-token",handler.Authorization);Assert.True(handler.Tenant);Assert.Null(handler.Session);
        await port.RequestsAsync(new string('S',43),new(Guid.NewGuid(),new string('A',64)),default);Assert.Null(handler.Authorization);Assert.False(handler.Cookie);Assert.False(handler.Tenant);Assert.Equal(new string('S',43),handler.Session);
        await port.AcceptAsync(new("operator",Guid.NewGuid(),"nexa_connect"),"staff-token",Guid.NewGuid(),Guid.NewGuid(),new(1),default);Assert.Equal("Bearer staff-token",handler.Authorization);Assert.True(handler.Tenant);Assert.Null(handler.Session);
        await port.ConfirmAsync(new string('S',43),Guid.NewGuid(),new(2,new string('A',64)),default);Assert.Null(handler.Authorization);Assert.False(handler.Tenant);Assert.Equal(new string('S',43),handler.Session);
        handler.Body = new string('X',65537); await Assert.ThrowsAsync<HttpRequestException>(() => port.GuestAsync("context",new string('S',43),null,default));
    }
    private sealed class Clients(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Handler : HttpMessageHandler
    {
        public string Body="{}"; public string? Authorization,Session; public bool Cookie,Tenant;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Authorization=request.Headers.Authorization?.ToString(); Cookie=request.Headers.Contains("Cookie");Tenant=request.Headers.Contains(TenantContextHeaders.OrganizationId);
            Session=request.Headers.TryGetValues("X-Nexa-Guest-Session",out var values)?values.Single():null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Body,Encoding.UTF8,"application/json")});
        }
    }
}
