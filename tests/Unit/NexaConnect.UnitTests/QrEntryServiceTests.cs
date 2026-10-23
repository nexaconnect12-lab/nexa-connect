using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Restaurant.Application.Qr;
using NexaConnect.Services.Restaurant.Application.Branches;
using NexaConnect.Services.Restaurant.Domain.Qr;
using NexaConnect.Services.Restaurant.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class QrEntryServiceTests
{
    [Fact] public async Task Live_exact_scope_configuration_authority_is_required_for_every_staff_operation()
    {
        var repo = new Repository(); var auth = new Authorizer(); var service = new QrEntryService(repo, auth, new QrSecrets(), TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(repo.Scope.OrganizationId, repo.Scope.BranchId, "Bearer staff", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(repo.Scope.OrganizationId, repo.Scope.BranchId, "A1", "operator", "Bearer staff", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ChangeAsync(repo.Scope.OrganizationId, repo.Scope.BranchId, Guid.NewGuid(), 1, false, "operator", "Bearer staff", default));
        Assert.Equal(0, repo.Saves); Assert.Equal(ProductPermissions.RestaurantConfigurationManage, auth.Permission); Assert.Equal(repo.Scope.RestaurantId, auth.Restaurant);
        auth.Allowed = true; var issued = await service.CreateAsync(repo.Scope.OrganizationId, repo.Scope.BranchId, "A1", "operator", "Bearer staff", default);
        Assert.Equal(1, repo.Saves); Assert.NotEqual(issued.EntryToken, repo.Entry!.SecretHash);
        // Revocation is re-evaluated, not cached after an earlier authorized issue.
        auth.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ChangeAsync(repo.Scope.OrganizationId, repo.Scope.BranchId, issued.Entry.Id, 1, true, "operator", "Bearer staff", default)); Assert.True(repo.Entry.Active);
    }
    [Fact] public async Task Wrong_branch_or_tenant_and_missing_actor_never_write()
    {
        var repo = new Repository(); var auth = new Authorizer { Allowed = true }; var service = new QrEntryService(repo, auth, new QrSecrets(), TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(Guid.NewGuid(), repo.Scope.BranchId, "A1", "operator", "Bearer staff", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(repo.Scope.OrganizationId, Guid.NewGuid(), "A1", "operator", "Bearer staff", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(repo.Scope.OrganizationId, repo.Scope.BranchId, "A1", "", "Bearer staff", default)); Assert.Equal(0, repo.Saves);
    }
    [Fact] public async Task Malformed_guest_secrets_never_reach_persistence_or_staff_authority()
    {
        var repo = new Repository(); var service = new QrEntryService(repo, new Authorizer(), new QrSecrets(), TimeProvider.System);
        Assert.Null(await service.StartAsync("malformed", default)); Assert.Null(await service.ContextAsync("malformed", default)); await service.EndAsync("malformed", default); Assert.Equal(0, repo.GuestCalls);
    }
    [Fact] public void Domain_public_contracts_have_no_framework_or_other_context_dependencies()
    {
        foreach (var model in new[] { typeof(QrEntry), typeof(QrScope), typeof(QrGuestAdmission) })
        foreach (var method in model.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
        foreach (var type in method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType))
            Assert.True(type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true || type.Namespace == typeof(QrEntry).Namespace);
    }
    private sealed class Authorizer : IBranchCustomerAuthorizer
    {
        public bool Allowed; public string? Permission; public Guid? Restaurant;
        public Task<bool> IsGrantedAsync(Guid org, Guid? restaurant, Guid? branch, string permission, string bearer, CancellationToken ct)
        { Permission = permission; Restaurant = restaurant; return Task.FromResult(Allowed); }
    }
    private sealed class Repository : IQrEntryRepository
    {
        public QrScope Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Restaurant", "Branch", "THB"); public int Saves, GuestCalls; public QrEntry? Entry;
        public Task<QrScope?> ScopeAsync(Guid org, Guid branch, CancellationToken ct) => Task.FromResult<QrScope?>(org == Scope.OrganizationId && branch == Scope.BranchId ? Scope : null);
        public Task<IReadOnlyList<QrEntry>> ListAsync(QrScope scope, CancellationToken ct) => Task.FromResult<IReadOnlyList<QrEntry>>([]);
        public Task<QrEntry?> GetAsync(QrScope scope, Guid id, CancellationToken ct) => Task.FromResult(Entry?.Id == id ? Entry : null);
        public Task SaveAsync(QrEntry entry, long expected, string actor, DateTimeOffset now, CancellationToken ct) { Saves++; Entry = entry; return Task.CompletedTask; }
        public Task<QrGuestContext?> AdmitAsync(string entry, string session, Guid visitor, DateTimeOffset now, CancellationToken ct) { GuestCalls++; return Task.FromResult<QrGuestContext?>(null); }
        public Task<QrGuestContext?> ContextAsync(string session, DateTimeOffset now, CancellationToken ct) { GuestCalls++; return Task.FromResult<QrGuestContext?>(null); }
        public Task EndAsync(string session, CancellationToken ct) { GuestCalls++; return Task.CompletedTask; }
    }
}
