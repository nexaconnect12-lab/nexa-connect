using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.Administration;

namespace NexaConnect.UnitTests;

public sealed class PlatformAdministrationTests
{
    [Fact]
    public async Task Create_user_rejects_unknown_platform_role()
    {
        var identity = new FakeIdentity(); var store = new FakeStore();
        var service = new PlatformAdministrationService(identity, store);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateUserAsync(
            new("owner", null, true, ["tenant-admin"]), "actor-1", CancellationToken.None));
        Assert.False(identity.CreateCalled);
    }

    [Fact]
    public async Task Create_user_normalizes_roles_and_records_audit()
    {
        var identity = new FakeIdentity(); var store = new FakeStore();
        var service = new PlatformAdministrationService(identity, store);
        PlatformUserSummary result = await service.CreateUserAsync(
            new(" owner ", " owner@example.test ", true, ["platform-auditor", "platform-auditor"]), "actor-1", CancellationToken.None);
        Assert.Equal("owner", result.Username); Assert.Equal(["platform-auditor"], result.Roles);
        Assert.Equal("platform-user.created", store.Action); Assert.Equal("actor-1", store.Actor);
    }

    [Fact]
    public void Role_catalog_keeps_product_roles_out_of_platform_permissions()
    {
        var service = new PlatformAdministrationService(new FakeIdentity(), new FakeStore());
        Assert.DoesNotContain(service.ListRoles(), role => role.Code == "tenant-admin");
        Assert.All(service.ListRoles(), role => Assert.NotEmpty(role.Permissions));
    }

    [Fact]
    public async Task Audit_query_rejects_unbounded_limit()
    {
        var service = new PlatformAdministrationService(new FakeIdentity(), new FakeStore());
        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryAuditAsync(new(null, null, null, null, 501), CancellationToken.None));
    }

    private sealed class FakeIdentity : IPlatformIdentityAdministration
    {
        public bool CreateCalled { get; private set; }
        public Task<IReadOnlyCollection<PlatformUserSummary>> ListUsersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformUserSummary>>([]);
        public Task<PlatformUserSummary> CreateUserAsync(CreatePlatformUserRequest request, CancellationToken cancellationToken) { CreateCalled=true; return Task.FromResult(new PlatformUserSummary("subject-1",request.Username,request.Email,request.Enabled,request.Roles)); }
        public Task<PlatformUserSummary?> UpdateUserAsync(string subjectId, UpdatePlatformUserRequest request, CancellationToken cancellationToken) => Task.FromResult<PlatformUserSummary?>(null);
        public Task<PlatformUserSummary?> ChangeRolesAsync(string subjectId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) => Task.FromResult<PlatformUserSummary?>(null);
    }

    private sealed class FakeStore : IPlatformControlPlaneStore
    {
        public string? Action { get; private set; } public string? Actor { get; private set; }
        public Task RecordAuditAsync(string action,string resourceType,string resourceId,string actorSubjectId,string outcome,CancellationToken cancellationToken){Action=action;Actor=actorSubjectId;return Task.CompletedTask;}
        public Task<IReadOnlyCollection<PlatformAuditRecord>> QueryAuditAsync(PlatformAuditQuery query,CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyCollection<PlatformAuditRecord>>([]);
        public Task<PlatformSummary> GetSummaryAsync(CancellationToken cancellationToken)=>Task.FromResult(new PlatformSummary(0,0,0,0,0,0,DateTimeOffset.UtcNow));
    }
}
