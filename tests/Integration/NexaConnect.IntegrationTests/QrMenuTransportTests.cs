extern alias ORDER;
using System.Net;
using System.Text;
using System.Text.Json;
using NexaConnect.Contracts.RestaurantQr;
using ORDER::NexaConnect.Services.Order.Infrastructure.Clients;

namespace NexaConnect.IntegrationTests;

public sealed class QrMenuTransportTests
{
    [Theory][InlineData(false)][InlineData(true)]public async Task Organization_uses_Order_workload_and_rejects_foreign_Catalog_scope(bool foreign)
    {
        var org=Guid.NewGuid();var branch=Guid.NewGuid();var category=new NexaConnect.Contracts.Catalog.MenuCategory(Guid.NewGuid(),"Food");
        using var handler=new Handler{Body=JsonSerializer.Serialize(new NexaConnect.Contracts.Catalog.BranchMenuOrganization(foreign?Guid.NewGuid():org,branch,1,[category],[new(Guid.NewGuid(),category.Id)]),JsonSerializerOptions.Web)};
        using var http=new HttpClient(handler){BaseAddress=new("https://catalog.test/")};http.DefaultRequestHeaders.Authorization=new("Bearer","own-order-workload");
        var port=new HttpQrMenuPort(new Clients(http,http));
        if(foreign)await Assert.ThrowsAsync<HttpRequestException>(()=>port.OrganizationAsync(org,branch,default));else Assert.Single((await port.OrganizationAsync(org,branch,default)).Categories);
        Assert.Equal("Bearer own-order-workload",handler.Authorization);Assert.Null(handler.Session);Assert.False(handler.Cookie);Assert.EndsWith("/menu-organization",handler.Path);
    }
    [Fact] public async Task Context_and_catalog_credentials_are_separate_and_responses_are_bounded()
    {
        var context=new QrGuestContext(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"R","B","A1","THB",DateTimeOffset.UtcNow.AddHours(1));
        using var contextHandler=new Handler {Body=JsonSerializer.Serialize(context,JsonSerializerOptions.Web)};
        using var catalogHandler=new Handler {Body=JsonSerializer.Serialize(new QrCatalogMenu([new(Guid.NewGuid(),"Soup",107,"THB",true,"kitchen")]),JsonSerializerOptions.Web)};
        using var contextClient=new HttpClient(contextHandler){BaseAddress=new Uri("https://restaurant.test/")};
        using var catalogClient=new HttpClient(catalogHandler){BaseAddress=new Uri("https://catalog.test/")};catalogClient.DefaultRequestHeaders.Authorization=new("Bearer","own-order-workload");
        var port=new HttpQrMenuPort(new Clients(contextClient,catalogClient));
        Assert.Equal(context,await port.ContextAsync(new string('S',43),default));Assert.Null(contextHandler.Authorization);Assert.Equal(new string('S',43),contextHandler.Session);Assert.False(contextHandler.Cookie);
        var menu=await port.MenuAsync(context.OrganizationId,context.BranchId,default);Assert.Equal("Soup",Assert.Single(menu).Name);
        Assert.Contains($"organizations/{context.OrganizationId:D}/branches/{context.BranchId:D}",catalogHandler.Path);Assert.Equal("Bearer own-order-workload",catalogHandler.Authorization);Assert.Null(catalogHandler.Session);Assert.False(catalogHandler.Cookie);
        catalogHandler.Body=JsonSerializer.Serialize(new QrAcceptanceContext(context,true),JsonSerializerOptions.Web);
        Assert.NotNull(await port.ContextAsync(new ORDER::NexaConnect.Services.Order.Domain.Qr.GuestRequestScope(context.OrganizationId,context.RestaurantId,context.BranchId,context.VisitorId,context.EntryId,context.TableCode),default));Assert.Null(catalogHandler.Session);Assert.Equal("Bearer own-order-workload",catalogHandler.Authorization);
        contextHandler.Status=HttpStatusCode.Unauthorized;Assert.Null(await port.ContextAsync(new string('S',43),default));
        contextHandler.Status=HttpStatusCode.OK;contextHandler.Body=new string('X',4097);await Assert.ThrowsAsync<HttpRequestException>(()=>port.ContextAsync(new string('S',43),default));
        catalogHandler.Body="{\"items\":null}";await Assert.ThrowsAsync<JsonException>(()=>port.MenuAsync(context.OrganizationId,context.BranchId,default));
        catalogHandler.Body=new string('X',524289);await Assert.ThrowsAsync<HttpRequestException>(()=>port.MenuAsync(context.OrganizationId,context.BranchId,default));
        catalogHandler.Status=HttpStatusCode.InternalServerError;await Assert.ThrowsAsync<HttpRequestException>(()=>port.MenuAsync(context.OrganizationId,context.BranchId,default));
    }
    private sealed class Clients(HttpClient context,HttpClient catalog):IHttpClientFactory{public HttpClient CreateClient(string name)=>name switch{"QrGuestContext"=>context,"QrGuestCatalog" or "QrAcceptanceContext"=>catalog,_=>throw new InvalidOperationException()};}
    private sealed class Handler:HttpMessageHandler
    {
        public string Body="{}",Path="";public string? Authorization,Session;public bool Cookie;public HttpStatusCode Status=HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Path=request.RequestUri!.AbsolutePath;Authorization=request.Headers.Authorization?.ToString();Cookie=request.Headers.Contains("Cookie");Session=request.Headers.TryGetValues("X-Nexa-Guest-Session",out var values)?values.Single():null;
            return Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(Body,Encoding.UTF8,"application/json")});
        }
    }
}
