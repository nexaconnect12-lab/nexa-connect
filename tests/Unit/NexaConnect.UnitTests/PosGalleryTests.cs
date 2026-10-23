using System.Security.Cryptography;
using NexaConnect.Contracts.Media;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosGalleryTests
{
    internal static GalleryBundle Bundle(DateTimeOffset now)
    {
        var one=Guid.NewGuid();var two=Guid.NewGuid();
        byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aE4kAAAAASUVORK5CYII=");
        return new(new(Guid.NewGuid(),Guid.NewGuid(),1,[new(one,"Staff","")],[new(two,"Customer","")],10),now,now.AddMinutes(5),[new(one,Convert.ToHexString(SHA256.HashData(png)),png),new(two,Convert.ToHexString(SHA256.HashData(png)),png)]);
    }
    [Theory]
    [InlineData("org")][InlineData("branch")][InlineData("expiry")][InlineData("lifetime")][InlineData("future")]
    [InlineData("hash")][InlineData("signature")][InlineData("pixels")][InlineData("missing")][InlineData("text")][InlineData("duplicate")]
    public void Scope_lease_corruption_and_unbounded_content_fail_closed(string change)
    {
        var now=DateTimeOffset.UtcNow;var b=Bundle(now);var s=b.Selection;var org=s.OrganizationId;var branch=s.BranchId;
        Assert.True(PosGalleryPolicy.Valid(b,org,branch,now));
        switch(change){
            case "org":org=Guid.NewGuid();break;case "branch":branch=Guid.NewGuid();break;
            case "expiry":b=b with{ExpiresAtUtc=now};break;case "lifetime":b=b with{ExpiresAtUtc=now.AddMinutes(6)};break;
            case "future":b=b with{IssuedAtUtc=now.AddMinutes(1)};break;
            case "hash":b=b with{Images=[b.Images[0] with{Sha256="bad"},b.Images[1]]};break;
            case "signature":b.Images[0].Png[0]=0;break;
            case "pixels":b.Images[0].Png[16]=255;break;
            case "missing":b=b with{Images=[]};break;
            case "text":b=b with{Selection=s with{Staff=[s.Staff[0] with{Caption=new string('x',161)}]}};break;
            case "duplicate":b=b with{Selection=s with{Staff=[s.Staff[0],s.Staff[0]]}};break;
        }
        Assert.False(PosGalleryPolicy.Valid(b,org,branch,now));
    }
    [Fact]
    public void Role_playlists_rotate_independently_and_offline_expiry_is_neutral()
    {
        var now=DateTimeOffset.UtcNow;var b=Bundle(now);
        var first=b.Selection.Staff[0];var second=b.Selection.Customer[0];
        b=b with{Selection=b.Selection with{Staff=[first,second],Customer=[second,first]}};
        Assert.Equal(first,PosGalleryPolicy.Slide(b,false,now));Assert.Equal(second,PosGalleryPolicy.Slide(b,true,now));
        Assert.Equal(second,PosGalleryPolicy.Slide(b,false,now.AddSeconds(10)));
        Assert.Null(PosGalleryPolicy.Slide(b,true,now.AddMinutes(5)));
        var removed=b with{Selection=b.Selection with{Customer=[]},Images=[b.Images[0]]};
        Assert.Null(PosGalleryPolicy.Slide(removed,true,now));
    }
    [PosDpapiAcceptanceFact]
    public void Protected_cache_is_scoped_atomic_bounded_and_removable_without_credentials()
    {
        var dir=Path.Combine(Path.GetTempPath(),"nexa-gallery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try{
            var now=DateTimeOffset.UtcNow;var b=Bundle(now);var cache=new PosGalleryCache(b.Selection.OrganizationId,b.Selection.BranchId,dir);
            cache.Save(b,now);Assert.NotNull(cache.Load(now));Assert.Null(cache.Load(now.AddMinutes(5)));
            Assert.Null(new PosGalleryCache(Guid.NewGuid(),b.Selection.BranchId,dir).Load(now));
            Assert.Single(Directory.GetFiles(dir));Assert.DoesNotContain("Customer",File.ReadAllText(Directory.GetFiles(dir)[0]));
            File.WriteAllBytes(Directory.GetFiles(dir)[0],[1,2,3]);Assert.Null(cache.Load(now));
            cache.Save(b,now);cache.Clear();Assert.Empty(Directory.GetFiles(dir));
        }finally{Directory.Delete(dir,true);}
    }
}
