using NexaConnect.Services.Authorization.Domain;
using NexaConnect.Services.Authorization.Application.Decisions;

namespace NexaConnect.UnitTests;

public sealed class AuthorizationPolicyTests
{
    [Theory]
    [InlineData("deny", true, false)]
    [InlineData("deny", false, false)]
    [InlineData("allow", false, true)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData("unexpected", true, false)]
    public void Explicit_override_replaces_role_grant(string? effect, bool role, bool expected) =>
        Assert.Equal(expected, AuthorizationPolicy.Grants(new(effect, role, null), null));

    [Theory]
    [InlineData("allow", 100, 100, true)]
    [InlineData("allow", 101, 100, false)]
    [InlineData("allow", -1, 100, false)]
    [InlineData("deny", 10, 100, false)]
    public void Financial_limit_cannot_override_permission(string effect, decimal amount, decimal limit, bool expected) =>
        Assert.Equal(expected, AuthorizationPolicy.Grants(new(effect, true, limit), amount));

    [Fact]
    public void Financial_request_without_limit_fails_closed() =>
        Assert.False(AuthorizationPolicy.Grants(new("allow", true, null), 1));

    [Fact]
    public async Task Decision_is_not_returned_when_audit_write_fails()
    {
        var service = new AuthorizationDecisionService(new FailingAuditStore());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync("synthetic", Guid.NewGuid(), null, null, "read", null, null, default));
    }
    private sealed class FailingAuditStore : IAuthorizationDecisionStore
    {
        public Task<AuthorizationEvidence> ReadAsync(AuthorizationQuery query, CancellationToken ct) => Task.FromResult(new AuthorizationEvidence("allow", true, null));
        public Task RecordAsync(AuthorizationQuery query, AuthorizationDecision decision, CancellationToken ct) => throw new InvalidOperationException("Audit unavailable.");
    }
}
