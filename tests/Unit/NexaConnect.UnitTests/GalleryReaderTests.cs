using Microsoft.AspNetCore.DataProtection;
using NexaConnect.Contracts.Media;
using NexaConnect.POS;
using NexaConnect.Services.Media.Application;
using NexaConnect.Services.Media.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class GalleryReaderTests
{
    private sealed class Clock:TimeProvider
    {
        public DateTimeOffset Now=new(2026,10,28,0,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    private sealed class Selections:IBranchGallerySelectionPort,IDelegatedGallerySelection
    {
        public Guid Org=Guid.NewGuid(),Branch=Guid.NewGuid();
        public bool Denied,Outage,ChangeDuringRead,RevokeDuringRead;public int Reads;public string? Subject;
        public Task<BranchGallery> ReadAsync(Guid org,Guid branch,string subject,CancellationToken ct)
        {
            Subject=subject;Reads++;
            if(Denied||RevokeDuringRead&&Reads%2==0)throw new UnauthorizedAccessException();
            if(Outage)throw new HttpRequestException();
            return Task.FromResult(new BranchGallery(Org,Branch,ChangeDuringRead?Reads:1,[],[]));
        }
    }
    [Fact]
    public async Task Locked_reader_renews_short_image_leases_beyond_five_minutes_but_not_its_own_lifetime()
    {
        var clock=new Clock();var p=new Selections();var protector=new GalleryReaderProtector(new EphemeralDataProtectionProvider());
        var service=new GalleryReaders(p,p,protector,new(p,null!,null!,null!,clock),clock);
        var reader=await service.IssueAsync(p.Org,p.Branch,"original-cashier","Bearer caller-only",default);
        var start=clock.Now;
        for(int minute=0;minute<=360;minute+=60){
            clock.Now=start.AddMinutes(minute);
            var bundle=await service.ReadAsync(p.Org,p.Branch,reader.Credential,default);
            Assert.Equal(clock.Now.AddMinutes(5),bundle.ExpiresAtUtc);
            Assert.Equal("original-cashier",p.Subject);
        }
        clock.Now=reader.ExpiresAtUtc.AddMinutes(-1);
        Assert.Equal(reader.ExpiresAtUtc,(await service.ReadAsync(p.Org,p.Branch,reader.Credential,default)).ExpiresAtUtc);
        clock.Now=reader.ExpiresAtUtc;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ReadAsync(p.Org,p.Branch,reader.Credential,default));
    }
    [Theory]
    [InlineData("revoked")][InlineData("mid-read-revoked")][InlineData("scope")][InlineData("tampered")][InlineData("expired")]
    public async Task Invalid_or_revoked_readers_fail_closed(string change)
    {
        var clock=new Clock();var p=new Selections();var protector=new GalleryReaderProtector(new EphemeralDataProtectionProvider());
        var service=new GalleryReaders(p,p,protector,new(p,null!,null!,null!,clock),clock);
        var reader=await service.IssueAsync(p.Org,p.Branch,"cashier","Bearer caller",default);p.Reads=0;
        if(change=="revoked")p.Denied=true;if(change=="mid-read-revoked")p.RevokeDuringRead=true;
        if(change=="scope")p.Branch=Guid.NewGuid();if(change=="tampered")reader=reader with{Credential="invalid"+reader.Credential};
        if(change=="expired")clock.Now=reader.ExpiresAtUtc;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ReadAsync(p.Org,p.Branch,reader.Credential,default));
    }
    [Fact]
    public async Task Outage_and_selection_change_never_create_a_new_lease_and_recovery_can_retry()
    {
        var clock=new Clock();var p=new Selections();var service=new GalleryReaders(p,p,new GalleryReaderProtector(new EphemeralDataProtectionProvider()),new(p,null!,null!,null!,clock),clock);
        var reader=await service.IssueAsync(p.Org,p.Branch,"cashier","Bearer caller",default);
        p.Outage=true;await Assert.ThrowsAsync<HttpRequestException>(()=>service.ReadAsync(p.Org,p.Branch,reader.Credential,default));
        p.Outage=false;p.ChangeDuringRead=true;await Assert.ThrowsAsync<InvalidDataException>(()=>service.ReadAsync(p.Org,p.Branch,reader.Credential,default));
        p.ChangeDuringRead=false;Assert.NotNull(await service.ReadAsync(p.Org,p.Branch,reader.Credential,default));
    }
    [Fact]
    public void Protected_readers_are_isolated_from_other_data_protection_purposes()
    {
        var provider=new EphemeralDataProtectionProvider();var protector=new GalleryReaderProtector(provider);var now=DateTimeOffset.UtcNow;
        var grant=new GalleryReaderGrant(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"cashier",now,now.AddHours(24));
        Assert.Equal(grant,protector.Read(protector.Protect(grant)));
        Assert.Null(protector.Read(provider.CreateProtector("other-purpose").Protect("{}")));
        Assert.False(GalleryReaders.Valid(grant with{ExpiresAtUtc=now.AddHours(25)},grant.OrganizationId,grant.BranchId,now));
    }
    [Fact]
    public void Renewing_a_lease_preserves_rotation_without_allowing_expired_images()
    {
        var start=DateTimeOffset.UtcNow;var b=PosGalleryTests.Bundle(start);var first=b.Selection.Staff[0];var second=b.Selection.Customer[0];
        b=b with{Selection=b.Selection with{Staff=[first,second],Customer=[second,first],SecondsPerSlide=60}};
        for(int minute=0;minute<30;minute++){
            var now=start.AddMinutes(minute);var renewed=b with{IssuedAtUtc=now,ExpiresAtUtc=now.AddMinutes(5)};
            Assert.True(PosGalleryPolicy.SamePlaylist(b.Selection,renewed.Selection));
            Assert.Equal(minute%2==0?first:second,PosGalleryPolicy.Slide(renewed,false,now,start));
            Assert.Equal(minute%2==0?second:first,PosGalleryPolicy.Slide(renewed,true,now,start));
        }
        Assert.Null(PosGalleryPolicy.Slide(b,false,start.AddMinutes(5),start));
        Assert.False(PosGalleryPolicy.SamePlaylist(b.Selection,b.Selection with{Staff=[]}));
        Assert.False(PosGalleryPolicy.SamePlaylist(b.Selection,b.Selection with{SecondsPerSlide=10}));
    }
}
