using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Domain.Administration;

namespace NexaConnect.Services.PlatformDirectory.Application.Administration;

public interface IPlatformIdentityAdministration
{
    Task<IReadOnlyCollection<PlatformUserSummary>> ListUsersAsync(CancellationToken cancellationToken);
    Task<PlatformUserSummary> CreateUserAsync(CreatePlatformUserRequest request, CancellationToken cancellationToken);
    Task<PlatformUserSummary?> UpdateUserAsync(string subjectId, UpdatePlatformUserRequest request, CancellationToken cancellationToken);
    Task<PlatformUserSummary?> ChangeRolesAsync(string subjectId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);
}

public interface IPlatformControlPlaneStore
{
    Task RecordAuditAsync(string action, string resourceType, string resourceId, string actorSubjectId, string outcome, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PlatformAuditRecord>> QueryAuditAsync(PlatformAuditQuery query, CancellationToken cancellationToken);
    Task<PlatformSummary> GetSummaryAsync(CancellationToken cancellationToken);
}

public interface IPlatformAdministration
{
    Task<IReadOnlyCollection<PlatformUserSummary>> ListUsersAsync(CancellationToken cancellationToken);
    Task<PlatformUserSummary> CreateUserAsync(CreatePlatformUserRequest request, string actorSubjectId, CancellationToken cancellationToken);
    Task<PlatformUserSummary?> UpdateUserAsync(string subjectId, UpdatePlatformUserRequest request, string actorSubjectId, CancellationToken cancellationToken);
    Task<PlatformUserSummary?> ChangeRolesAsync(string subjectId, ChangePlatformUserRolesRequest request, string actorSubjectId, CancellationToken cancellationToken);
    IReadOnlyCollection<PlatformRoleSummary> ListRoles();
    Task<IReadOnlyCollection<PlatformAuditRecord>> QueryAuditAsync(PlatformAuditQuery query, CancellationToken cancellationToken);
    Task<PlatformSummary> GetSummaryAsync(CancellationToken cancellationToken);
}

public sealed class PlatformAdministrationService(IPlatformIdentityAdministration identity, IPlatformControlPlaneStore store) : IPlatformAdministration
{
    public Task<IReadOnlyCollection<PlatformUserSummary>> ListUsersAsync(CancellationToken cancellationToken) => identity.ListUsersAsync(cancellationToken);

    public async Task<PlatformUserSummary> CreateUserAsync(CreatePlatformUserRequest request, string actorSubjectId, CancellationToken cancellationToken)
    {
        RequireActor(actorSubjectId); RequireText(request.Username, "Username");
        IReadOnlyCollection<string> roles = PlatformRoleCatalog.NormalizeAndValidate(request.Roles);
        PlatformUserSummary user = await identity.CreateUserAsync(request with { Username = request.Username.Trim(), Email = request.Email?.Trim(), Roles = roles }, cancellationToken);
        await store.RecordAuditAsync("platform-user.created", "platform-user", user.SubjectId, actorSubjectId.Trim(), "succeeded", cancellationToken);
        return user;
    }

    public async Task<PlatformUserSummary?> UpdateUserAsync(string subjectId, UpdatePlatformUserRequest request, string actorSubjectId, CancellationToken cancellationToken)
    {
        RequireText(subjectId, "User subject"); RequireActor(actorSubjectId);
        PlatformUserSummary? user = await identity.UpdateUserAsync(subjectId.Trim(), request with { Email = request.Email?.Trim() }, cancellationToken);
        if (user is not null) await store.RecordAuditAsync("platform-user.updated", "platform-user", subjectId.Trim(), actorSubjectId.Trim(), "succeeded", cancellationToken);
        return user;
    }

    public async Task<PlatformUserSummary?> ChangeRolesAsync(string subjectId, ChangePlatformUserRolesRequest request, string actorSubjectId, CancellationToken cancellationToken)
    {
        RequireText(subjectId, "User subject"); RequireActor(actorSubjectId);
        IReadOnlyCollection<string> roles = PlatformRoleCatalog.NormalizeAndValidate(request.Roles);
        PlatformUserSummary? user = await identity.ChangeRolesAsync(subjectId.Trim(), roles, cancellationToken);
        if (user is not null) await store.RecordAuditAsync("platform-user.roles-changed", "platform-user", subjectId.Trim(), actorSubjectId.Trim(), "succeeded", cancellationToken);
        return user;
    }

    public IReadOnlyCollection<PlatformRoleSummary> ListRoles() => PlatformRoleCatalog.PermissionsByRole
        .Select(pair => new PlatformRoleSummary(pair.Key, pair.Key switch { "platform-owner" => "Full platform ownership", "platform-admin" => "Platform administration", "platform-support" => "Time-limited customer support", _ => "Read-only platform audit" }, pair.Value)).ToArray();

    public Task<IReadOnlyCollection<PlatformAuditRecord>> QueryAuditAsync(PlatformAuditQuery query, CancellationToken cancellationToken)
    {
        if (query.Limit is < 1 or > 500) throw new ArgumentException("Audit query limit must be between 1 and 500.");
        if (query.FromUtc > query.ToUtc) throw new ArgumentException("Audit query start must not be after its end.");
        return store.QueryAuditAsync(query, cancellationToken);
    }

    public Task<PlatformSummary> GetSummaryAsync(CancellationToken cancellationToken) => store.GetSummaryAsync(cancellationToken);
    private static void RequireActor(string value) => RequireText(value, "Actor subject");
    private static void RequireText(string? value, string label) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{label} is required."); }
}
