using System.Net;
using System.Net.Http.Json;
using NexaConnect.Contracts.Media;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosGalleryReaderTests
{
    private static PosClientConfiguration Configuration()=>new("https://identity.test/realms/test","nexaconnect-pos","nexaconnect-pos://oauth/callback","openid",
        "https://pos.test/","https://order.test/",Guid.NewGuid(),Guid.NewGuid(),"THB","cash_manual",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
        CatalogApi:"https://catalog.test/",MediaApi:"https://media.test/");
    [PosDpapiAcceptanceFact]
    public void Reader_cache_is_protected_and_bound_to_original_operator_authority_terminal_and_expiry()
    {
        var root=Path.Combine(Path.GetTempPath(),"Nexa-GalleryReaderTests",Guid.NewGuid().ToString("N"));
        try{
            var c=Configuration();var now=DateTimeOffset.UtcNow;var cache=new PosGalleryReaderCache(c,root);
            var reader=new GalleryReaderCredential("synthetic-gallery-only",c.OrganizationId,c.BranchId,now,now.AddHours(24));
            cache.Save(reader,"original",now);
            Assert.Equal(reader,new PosGalleryReaderCache(c,root).Load("original",now));
            Assert.Null(cache.Load("other",now));Assert.Null(cache.Load("original",reader.ExpiresAtUtc));
            foreach(var other in new[]{c with{Authority="https://other.test/"},c with{TerminalId=Guid.NewGuid()},c with{MediaApi="https://other.test/"},c with{BranchId=Guid.NewGuid()}})
                Assert.Null(new PosGalleryReaderCache(other,root).Load("original",now));
            var path=Assert.Single(Directory.GetFiles(root,"*.bin",SearchOption.AllDirectories));
            Assert.DoesNotContain("synthetic-gallery-only",File.ReadAllText(path));
            File.WriteAllBytes(path,[1,2,3]);Assert.Null(cache.Load("original",now));
            cache.Save(reader,"original",now);cache.Clear();Assert.Null(cache.Load("original",now));
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]
    public async Task Locked_gallery_request_has_only_the_narrow_credential_and_correlation()
    {
        var c=Configuration();var handler=new Capture(c);using var api=new PosApiClient(c,mediaHandler:handler);
        await api.ReadGalleryAsync("synthetic-gallery-only",default);Assert.Equal(1,handler.Reads);
        await api.CreateGalleryReaderAsync(new("caller-access","caller-refresh",DateTimeOffset.UtcNow.AddMinutes(5),"Bearer"),default);
        Assert.Equal(1,handler.Issues);
    }
    private sealed class Capture(PosClientConfiguration c):HttpMessageHandler
    {
        public int Reads,Issues;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal("media.test",request.RequestUri!.Host);Assert.Equal("",request.RequestUri.Query);
            Assert.True(Guid.TryParse(Assert.Single(request.Headers.GetValues("X-Correlation-ID")),out _));
            Assert.Null(request.Content);
            object result;
            if(request.Method==HttpMethod.Get){Reads++;Assert.Null(request.Headers.Authorization);
                Assert.Equal("synthetic-gallery-only",Assert.Single(request.Headers.GetValues("X-Nexa-Gallery-Reader")));
                result=new GalleryBundle(new(c.OrganizationId,c.BranchId,1,[],[]),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMinutes(5),[]);
            }else{Issues++;Assert.Equal("caller-access",request.Headers.Authorization?.Parameter);Assert.False(request.Headers.Contains("X-Nexa-Gallery-Reader"));
                result=new GalleryReaderCredential("synthetic-gallery-only",c.OrganizationId,c.BranchId,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(24));}
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(result)});
        }
    }
}
