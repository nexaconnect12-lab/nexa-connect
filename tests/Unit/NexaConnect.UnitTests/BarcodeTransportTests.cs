using System.Net;
using System.Net.Http.Json;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class BarcodeTransportTests
{
    [Theory][InlineData("valid")][InlineData("org")][InlineData("branch")][InlineData("code")][InlineData("currency")][InlineData("name")]
    public async Task Scan_lookup_is_body_only_scoped_and_checks_the_live_menu_snapshot(string change)
    {
        var c=new PosClientConfiguration("https://identity.test/","nexaconnect-pos","nexaconnect-pos://oauth/callback","openid","https://pos.test/","https://order.test/",Guid.NewGuid(),Guid.NewGuid(),"THB","cash_manual",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),CatalogApi:"https://catalog.test/");
        var response=new PosBarcodeMatch(c.OrganizationId,c.BranchId,"001",new(Guid.NewGuid(),"Meal",10,"THB","kitchen",true));
        response=change switch{"org"=>response with{OrganizationId=Guid.NewGuid()},"branch"=>response with{BranchId=Guid.NewGuid()},"code"=>response with{Barcode="different"},"currency"=>response with{Item=response.Item with{Currency="USD"}},"name"=>response with{Item=response.Item with{Name=new string('x',201)}},_=>response};
        using var api=new PosApiClient(c,catalogHandler:new Handler(c,response));var token=new PosTokenSet("synthetic",null,DateTimeOffset.UtcNow.AddMinutes(5),"Bearer");
        if(change=="valid")Assert.Equal(response,await api.LookupBarcodeAsync(token,"001",default));
        else await Assert.ThrowsAsync<InvalidDataException>(()=>api.LookupBarcodeAsync(token,"001",default));
    }
    private sealed class Handler(PosClientConfiguration c,PosBarcodeMatch response):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post,request.Method);Assert.Equal("",request.RequestUri!.Query);Assert.EndsWith("/barcode-lookup",request.RequestUri.AbsolutePath);
            Assert.Equal(c.OrganizationId.ToString("D"),Assert.Single(request.Headers.GetValues("X-Nexa-Organization-Id")));
            Assert.True(Guid.TryParse(Assert.Single(request.Headers.GetValues("X-Correlation-ID")),out _));
            var body=await request.Content!.ReadFromJsonAsync<NexaConnect.Contracts.Catalog.BarcodeLookupRequest>(ct);Assert.Equal("001",body!.Barcode);
            return new(HttpStatusCode.OK){Content=JsonContent.Create(response)};
        }
    }
}
