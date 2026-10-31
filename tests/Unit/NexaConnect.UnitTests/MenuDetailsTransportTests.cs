using System.Net;
using System.Net.Http.Json;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Catalog;
using NexaConnect.CustomerBff.Infrastructure.Catalog;
namespace NexaConnect.UnitTests;
public sealed class MenuDetailsTransportTests
{
    [Fact] public async Task Adapter_uses_scoped_requests_and_current_membership_without_browser_headers()
    {
        Guid org=Guid.NewGuid(),branch=Guid.NewGuid(),product=Guid.NewGuid();var handler=new Handler(org,branch,product);using var http=new HttpClient(handler){BaseAddress=new Uri("https://service.test/")};
        var factory=new Factory(http);var app=new CustomerMenuDetails(new HttpCustomerMenuDetailsPort(factory));var tenant=new TenantContext("operator",org,"nexa_connect");
        using var response=await app.ExecuteAsync(tenant,branch,product,new("New meal",108,"bar",0),"synthetic",default);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(new[]{"MenuDetailsDirectory","MenuDetailsCatalog"},factory.Names);Assert.Equal(1,handler.Writes);
        handler.Allowed=false;using var denied=await app.ExecuteAsync(tenant,branch,product,new("New meal",109,"bar",1),"synthetic",default);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(1,handler.Writes);
    }
    private sealed class Factory(HttpClient http):IHttpClientFactory
    {public List<string> Names=[];public HttpClient CreateClient(string name){Names.Add(name);return http;}}
    private sealed class Handler(Guid org,Guid branch,Guid product):HttpMessageHandler
    {
        public bool Allowed=true;public int Writes;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal("Bearer synthetic",request.Headers.Authorization?.ToString());Assert.False(request.Headers.Contains("Cookie"));Assert.False(request.Headers.Contains("X-Nexa-CSRF"));
            if(request.RequestUri!.AbsolutePath.EndsWith("/me/access",StringComparison.Ordinal))
                return new(HttpStatusCode.OK){Content=JsonContent.Create(new CurrentPlatformAccessResponse("operator",Allowed?[new(org,"org","Org","nexa_connect")]:[]))};
            Assert.Equal(HttpMethod.Put,request.Method);Assert.Equal($"/api/catalog/v1/customer/branches/{branch:D}/menu-details/products/{product:D}",request.RequestUri.AbsolutePath);
            Assert.Equal(org.ToString("D"),Assert.Single(request.Headers.GetValues(TenantContextHeaders.OrganizationId)));Assert.Equal("nexa_connect",Assert.Single(request.Headers.GetValues(TenantContextHeaders.ApplicationCode)));
            var body=await request.Content!.ReadFromJsonAsync<UpdateMenuDetails>(ct);Assert.Equal(108,body!.UnitPrice);Assert.Equal("bar",body.PreparationStation);Writes++;
            return new(HttpStatusCode.OK){Content=JsonContent.Create(new EditableMenuItem(product,body.Name!,body.UnitPrice.GetValueOrDefault(),"THB",body.PreparationStation!,true,1))};
        }
    }
}
