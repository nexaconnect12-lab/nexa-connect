using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.Restaurant.Domain.Qr;
using NexaConnect.Services.Restaurant.Infrastructure;
using NexaConnect.Services.Restaurant.Application.Qr;

namespace NexaConnect.UnitTests;

public sealed class QrEntryTests
{
    [Fact] public void Secrets_are_independent_256_bit_url_capabilities_and_only_hashes_enter_domain()
    {
        var secrets = new QrSecrets(); var tokens = Enumerable.Range(0, 100).Select(_ => secrets.Generate()).ToArray();
        Assert.Equal(100, tokens.Distinct().Count());
        Assert.All(tokens, token => { Assert.True(QrEntryService.ValidToken(token)); Assert.Equal(43, token.Length); Assert.Equal(64, secrets.Hash(token).Length); Assert.DoesNotContain(token, secrets.Hash(token)); });
    }
    [Theory][InlineData(null)][InlineData("")][InlineData("A/1")][InlineData("<script>")][InlineData("A\nB")]
    public void Table_codes_are_bounded_plain_identifiers(string? code) => Assert.Throws<ArgumentException>(() => QrEntry.NormalizeCode(code));
    [Fact] public void Rotation_and_revocation_are_explicit_versioned_transitions()
    {
        var now = DateTimeOffset.UtcNow; var scope = new QrScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "R", "B", "THB");
        var entry = new QrEntry(Guid.NewGuid(), scope, QrEntry.NormalizeCode(" a1 "), new string('A', 64), 1, true, now.AddDays(30));
        Assert.Equal("A1", entry.TableCode); Assert.Throws<QrConflictException>(() => entry.Rotate(0, new string('B', 64), now));
        entry.Rotate(1, new string('B', 64), now); Assert.Equal(2, entry.Version); Assert.Equal(new string('B', 64), entry.SecretHash);
        Assert.Throws<QrConflictException>(() => entry.Revoke(1)); entry.Revoke(2); Assert.False(entry.Active);
        entry.Rotate(3, new string('C', 64), now); Assert.True(entry.Active); Assert.Equal(4, entry.Version);
    }
    [Theory][InlineData(100, 100, 1)][InlineData(1, 1000, 1)][InlineData(1, 1, 30)]
    public void Admission_limits_are_domain_rules(long entry, long branch, long minute) => Assert.Throws<QrLimitException>(() => QrGuestAdmission.RequireCapacity(entry, branch, minute));
    [Fact] public void Session_expiry_is_absolute_and_cannot_outlive_printed_entry()
    {
        var now = DateTimeOffset.UtcNow; Assert.Equal(now.AddHours(2), QrGuestAdmission.Expiry(now, now.AddDays(30)));
        Assert.Equal(now.AddMinutes(5), QrGuestAdmission.Expiry(now, now.AddMinutes(5))); Assert.Throws<QrLimitException>(() => QrGuestAdmission.RequireEntryCapacity(200));
    }
    [Fact] public void Transport_memory_and_counts_are_bounded_and_windows_expire()
    {
        var clock = new Clock(); var gate = new BoundedRequestGate(clock, 2, 3, 2);
        Assert.True(gate.Admit("a")); Assert.True(gate.Admit("a")); Assert.False(gate.Admit("a"));
        Assert.True(gate.Admit("b")); Assert.False(gate.Admit("c")); clock.Now = clock.Now.AddMinutes(1); Assert.True(gate.Admit("c"));
        var limitedKeys = new BoundedRequestGate(clock, 10, 100, 1); Assert.True(limitedKeys.Admit("a")); Assert.False(limitedKeys.Admit("b"));
    }
    [Theory][InlineData("short")][InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")][InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    public void Arbitrary_guest_capabilities_are_rejected_before_lookup(string token) => Assert.False(QrEntryService.ValidToken(token));
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(6000); public override DateTimeOffset GetUtcNow() => Now; }
}
