using Microsoft.AspNetCore.DataProtection;
using NexaConnect.Services.POS.Domain.CashierPins;
using NexaConnect.Services.POS.Infrastructure.Identity;

namespace NexaConnect.UnitTests;

public sealed class CashierPinCredentialTests
{
    [Theory]
    [InlineData("12345")][InlineData("1234567")][InlineData("１２３４５６")][InlineData("12a456")][InlineData("")]
    public void Only_six_ascii_digits_are_valid(string pin) => Assert.False(CashierPinCredential.ValidPin(pin));
    [Fact]
    public void Verifiers_are_random_protected_and_require_the_same_key_ring()
    {
        var provider = new EphemeralDataProtectionProvider(); var hasher = new CashierPinProtector(provider);
        var key=new NexaConnect.Services.POS.Application.CashierPins.CashierPinKey(Guid.NewGuid(), "https://identity", "cashier");
        string first=hasher.Create(key,"826491"), second=hasher.Create(key,"826491");
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(()=>hasher.Matches(key with { Subject="another" }, first,"826491"));
        Assert.NotEqual(first,second); Assert.DoesNotContain("826491",first);
        Assert.True(hasher.Matches(key,first,"826491")); Assert.False(hasher.Matches(key,first,"123456"));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(()=>new CashierPinProtector(new EphemeralDataProtectionProvider()).Matches(key,first,"826491"));
    }
    [Fact]
    public void Durable_lockout_version_conflicts_revocation_and_reset_preserve_invariants()
    {
        var c = new CashierPinCredential(); var now=DateTimeOffset.UtcNow;
        Assert.True(c.Change(0,"protected",now,_=>false)); Assert.Equal(1,c.Version);
        Assert.False(c.Change(0,"other",now,_=>true));
        for(int i=0;i<5;i++) Assert.False(c.Verify(1,now,_=>false));
        Assert.False(c.Verify(1,now,_=>true)); Assert.False(c.Change(1,"other",now,_=>true));
        var restored=CashierPinCredential.Restore(c.State,c.Version,c.ProtectedVerifier,c.FailedAttempts,c.LockedUntilUtc);
        Assert.False(restored.Verify(1,now,_=>true)); Assert.True(restored.Verify(1,now.AddMinutes(16),_=>true));
        Assert.Equal(0,restored.FailedAttempts); Assert.True(restored.Administer(1,"revoke"));
        Assert.False(restored.Verify(1,now,_=>true)); Assert.False(restored.Change(2,"other",now,_=>true));
        Assert.True(restored.Administer(2,"reset")); Assert.True(restored.Change(3,"new",now,_=>false));
        Assert.Equal(4,restored.Version);
    }
}
