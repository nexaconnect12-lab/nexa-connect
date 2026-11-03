using System.Net;
using System.Net.Http.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Media;
using NexaConnect.CustomerBff.Infrastructure.Media;

namespace NexaConnect.UnitTests;

public sealed class CustomerMediaLibraryTests
{
    [Theory][InlineData("allowed",200)][InlineData("subject",403)][InlineData("organization",403)][InlineData("application",403)]
    public async Task Protected_membership_gates_organization_bound_read_requests(string scenario,int expected)
    {
        Guid org=Guid.NewGuid();var handler=new Handler(org){Allowed=scenario!="organization"};using var client=new HttpClient(handler){BaseAddress=new("https://service.test/")};var factory=new Factory(client);
        var app=new CustomerMediaLibrary(new HttpCustomerMediaLibraryPort(factory));
        var tenant=new TenantContext(scenario=="subject"?"other":"operator",org,scenario=="application"?"other":"nexa_connect");
        using var response=await app.ExecuteAsync(tenant,null," rice & soup ",2,true,"synthetic",default);Assert.Equal(expected,(int)response.StatusCode);
        Assert.Equal(expected==200?1:0,handler.Reads);
        if(expected==200){Assert.Equal(new[]{"MediaLibraryDirectory","MediaLibraryMedia"},factory.Names);Assert.Contains("search=rice%20%26%20soup",handler.Path);Assert.Contains("page=2&readyOnly=true",handler.Path);}
    }
    [Fact]public async Task Thumbnail_path_contains_only_current_org_and_typed_asset()
    {
        Guid org=Guid.NewGuid(),asset=Guid.NewGuid();var handler=new Handler(org);using var client=new HttpClient(handler){BaseAddress=new("https://service.test/")};
        var app=new CustomerMediaLibrary(new HttpCustomerMediaLibraryPort(new Factory(client)));
        using var response=await app.ExecuteAsync(new("operator",org,"nexa_connect"),asset,"",1,true,"synthetic",default);
        Assert.Equal($"/api/media/v1/customer/organizations/{org:D}/library/assets/{asset:D}/thumbnail",handler.Path);
    }
    private sealed class Factory(HttpClient client):IHttpClientFactory
    {public List<string> Names=[];public HttpClient CreateClient(string name){Names.Add(name);return client;}}
    private sealed class Handler(Guid org):HttpMessageHandler
    {
        public bool Allowed=true;public int Reads;public string Path="";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get,request.Method);Assert.Equal("Bearer synthetic",request.Headers.Authorization?.ToString());Assert.False(request.Headers.Contains("Cookie"));Assert.Null(request.Content);
            if(request.RequestUri!.AbsolutePath=="/api/platform-directory/v1/me/access")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new CurrentPlatformAccessResponse("operator",Allowed?[new(org,"org","Org","nexa_connect")]:[]))});
            Reads++;Path=request.RequestUri.PathAndQuery;Assert.StartsWith($"/api/media/v1/customer/organizations/{org:D}/library",Path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{items=Array.Empty<object>(),hasMore=false,page=2})});
        }
    }
}
