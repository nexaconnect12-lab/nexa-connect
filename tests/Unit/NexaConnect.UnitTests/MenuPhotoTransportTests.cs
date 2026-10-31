using System.Net;
using NexaConnect.POS;
namespace NexaConnect.UnitTests;
public sealed class MenuPhotoTransportTests
{
    [Theory][InlineData("image/png",3,true)][InlineData("text/html",3,false)][InlineData("image/png",524289,false)]
    public async Task Native_photo_requests_use_configured_tenant_branch_and_bounded_binary_only(string type,int length,bool valid)
    {
        var c=new PosClientConfiguration("https://identity.test/","nexaconnect-pos","nexaconnect-pos://oauth/callback","openid","https://pos.test/","https://order.test/",Guid.NewGuid(),Guid.NewGuid(),"THB","cash_manual",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),CatalogApi:"https://catalog.test/");
        var product=Guid.NewGuid();using var api=new PosApiClient(c,catalogHandler:new Handler(c,product,type,length));var token=new PosTokenSet("synthetic",null,DateTimeOffset.UtcNow.AddMinutes(5),"Bearer");
        if(valid)Assert.Equal(length,(await api.GetMenuPhotoAsync(token,product))!.Length);
        else await Assert.ThrowsAnyAsync<Exception>(()=>api.GetMenuPhotoAsync(token,product));
    }
    private sealed class Handler(PosClientConfiguration c,Guid product,string type,int length):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get,request.Method);Assert.Equal($"/api/catalog/v1/customer/branches/{c.BranchId:D}/products/{product:D}/photo",request.RequestUri!.AbsolutePath);Assert.Equal("",request.RequestUri.Query);
            Assert.Equal(c.OrganizationId.ToString("D"),Assert.Single(request.Headers.GetValues("X-Nexa-Organization-Id")));Assert.Equal("Bearer synthetic",request.Headers.Authorization?.ToString());Assert.False(request.Headers.Contains("Cookie"));Assert.True(Guid.TryParse(Assert.Single(request.Headers.GetValues("X-Correlation-ID")),out _));
            var content=new ByteArrayContent(new byte[length]);content.Headers.ContentType=new(type);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
        }
    }
}
