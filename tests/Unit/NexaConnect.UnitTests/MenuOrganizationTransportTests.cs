using System.Net;
using System.Net.Http.Json;
using NexaConnect.Contracts.Catalog;
using NexaConnect.POS;
namespace NexaConnect.UnitTests;
public sealed class MenuOrganizationTransportTests
{
    [Theory][InlineData("valid")][InlineData("foreign-org")][InlineData("foreign-branch")][InlineData("oversized")][InlineData("mime")]
    public async Task Native_categories_are_bounded_scoped_and_never_send_financial_commands(string condition)
    {
        var c=new PosClientConfiguration("https://identity.test/","nexaconnect-pos","nexaconnect-pos://oauth/callback","openid","https://pos.test/","https://order.test/",Guid.NewGuid(),Guid.NewGuid(),"THB","cash_manual",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),CatalogApi:"https://catalog.test/");
        var category=new MenuCategory(Guid.NewGuid(),"Food");var result=new BranchMenuOrganization(condition=="foreign-org"?Guid.NewGuid():c.OrganizationId,condition=="foreign-branch"?Guid.NewGuid():c.BranchId,1,[category],[new(Guid.NewGuid(),category.Id)]);
        using var api=new PosApiClient(c,catalogHandler:new Handler(c,result,condition));var token=new PosTokenSet("synthetic",null,DateTimeOffset.UtcNow.AddMinutes(5),"Bearer");
        if(condition=="valid")Assert.Equal(category,Assert.Single((await api.GetMenuOrganizationAsync(token)).Categories));
        else await Assert.ThrowsAnyAsync<Exception>(()=>api.GetMenuOrganizationAsync(token));
    }
    private sealed class Handler(PosClientConfiguration c,BranchMenuOrganization result,string condition):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get,request.Method);Assert.Null(request.Content);Assert.EndsWith($"/branches/{c.BranchId:D}/menu-organization",request.RequestUri!.AbsolutePath);Assert.Equal("",request.RequestUri.Query);
            Assert.Equal(c.OrganizationId.ToString("D"),Assert.Single(request.Headers.GetValues("X-Nexa-Organization-Id")));Assert.Equal("Bearer synthetic",request.Headers.Authorization?.ToString());Assert.True(Guid.TryParse(Assert.Single(request.Headers.GetValues("X-Correlation-ID")),out _));Assert.False(request.Headers.Contains("Cookie"));
            HttpContent content=condition=="oversized"?new StringContent(new string('x',65537)):JsonContent.Create(result);if(condition=="mime")content.Headers.ContentType=new("text/html");return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
        }
    }
}
