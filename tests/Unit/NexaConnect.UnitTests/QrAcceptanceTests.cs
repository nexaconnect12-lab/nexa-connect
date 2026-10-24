using NexaConnect.Services.Order.Domain.Qr;
namespace NexaConnect.UnitTests;
public sealed class QrAcceptanceTests
{
    [Fact]public void Acceptance_is_versioned_pending_only_and_cannot_expire_after_commit()
    {
        var now=DateTimeOffset.UtcNow;var request=Create(now);
        Assert.Throws<GuestRequestConflictException>(()=>request.Accept(2,now));request.Accept(1,now);
        Assert.Equal("accepted",request.Status);Assert.Equal(2,request.Version);Assert.False(request.Expire(now.AddHours(1)));
        Assert.Throws<GuestRequestConflictException>(()=>request.Reject(2,"closing",now));Assert.Throws<GuestRequestConflictException>(()=>request.Accept(2,now));
    }
    [Fact]public void Price_review_blocks_acceptance_until_guest_confirms_current_version()
    {
        var now=DateTimeOffset.UtcNow;var request=Create(now);request.ReviewPrice(1,now);Assert.Equal("price_review",request.Status);
        Assert.Throws<GuestRequestConflictException>(()=>request.Accept(2,now));Assert.Throws<GuestRequestConflictException>(()=>request.ConfirmPrice(1,now));
        request.ConfirmPrice(2,now);Assert.Equal("pending",request.Status);Assert.Equal(3,request.Version);request.Accept(3,now);Assert.Equal(4,request.Version);
    }
    [Fact]public void Price_review_does_not_extend_deadline_or_bypass_rejection()
    {
        var now=DateTimeOffset.UtcNow;var request=Create(now);request.ReviewPrice(1,now);var deadline=request.DeadlineUtc;
        Assert.True(request.Expire(deadline));Assert.Equal("expired",request.Status);Assert.Throws<GuestRequestConflictException>(()=>request.ConfirmPrice(3,deadline));
        var rejected=Create(now);rejected.ReviewPrice(1,now);rejected.Reject(2,"unavailable",now);Assert.Equal("rejected",rejected.Status);
    }
    [Fact]public void Staff_can_refresh_review_without_consent_or_deadline_extension()
    {
        var now=DateTimeOffset.UtcNow;var request=Create(now);var deadline=request.DeadlineUtc;request.ReviewPrice(1,now);request.ReviewPrice(2,now.AddMinutes(3));
        Assert.Equal("price_review",request.Status);Assert.Equal(3,request.Version);Assert.Equal(deadline,request.DeadlineUtc);Assert.Throws<GuestRequestConflictException>(()=>request.Accept(3,now.AddMinutes(3)));
    }
    private static GuestRequest Create(DateTimeOffset now)=>new(Guid.NewGuid(),new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"A1"),new string('A',64),now,now.AddMinutes(15));
}
