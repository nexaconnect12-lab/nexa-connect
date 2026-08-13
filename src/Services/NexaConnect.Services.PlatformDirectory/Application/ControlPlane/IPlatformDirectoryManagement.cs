using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.PlatformDirectory.Application.ControlPlane;

public sealed class PlatformDirectoryConflictException(string message) : Exception(message);

public interface IPlatformDirectoryManagement
{
    Task<IReadOnlyCollection<OrganizationSummary>> ListOrganizationsAsync(CancellationToken cancellationToken);

    Task<OrganizationSummary> CreateOrganizationAsync(
        CreateOrganizationRequest request,
        string actorSubjectId,
        CancellationToken cancellationToken);

    Task<bool> UpdateOrganizationAsync(
        Guid organizationId,
        UpdateOrganizationRequest request,
        string actorSubjectId,
        CancellationToken cancellationToken);

    Task<bool> ChangeMembershipAsync(
        Guid organizationId,
        string subjectId,
        ChangeOrganizationMembershipRequest request,
        string actorSubjectId,
        CancellationToken cancellationToken);

    Task<ProductRegistration> RegisterProductAsync(
        RegisterProductRequest request,
        string actorSubjectId,
        CancellationToken cancellationToken);

    Task<bool> ChangeProductAccessAsync(
        Guid organizationId,
        ChangeOrganizationProductAccessRequest request,
        string actorSubjectId,
        CancellationToken cancellationToken);
}

public sealed class PlatformDirectoryManagementService(IPlatformDirectoryManagementRepository repository)
    : IPlatformDirectoryManagement
{
    private static readonly HashSet<string> OrganizationStatuses = ["pending", "active", "suspended", "closed"];
    private static readonly HashSet<string> MembershipStatuses = ["invited", "active", "suspended", "removed"];
    private static readonly HashSet<string> ProductStatuses = ["active", "suspended", "retired"];
    private static readonly HashSet<string> ProductAccessStatuses = ["enabled", "suspended", "disabled"];

    public Task<IReadOnlyCollection<OrganizationSummary>> ListOrganizationsAsync(CancellationToken cancellationToken) =>
        repository.ListOrganizationsAsync(cancellationToken);

    public Task<OrganizationSummary> CreateOrganizationAsync(
        CreateOrganizationRequest request,
        string actorSubjectId,
        CancellationToken cancellationToken)
    {
        RequireActor(actorSubjectId);
        string code = request.Code?.Trim() ?? string.Empty;
        RequireText(code, "Organization code");
        RequireText(request.Name, "Organization name");
        RequireText(request.DefaultTimeZone, "Organization time zone");
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z0-9][a-z0-9_-]{0,63}$"))
            throw new ArgumentException("Organization code has an invalid format.");
        return repository.CreateOrganizationAsync(request with
        {
            Code = code,
            Name = request.Name.Trim(),
            DefaultTimeZone = request.DefaultTimeZone.Trim()
        }, actorSubjectId.Trim(), cancellationToken);
    }

    public Task<bool> UpdateOrganizationAsync(
        Guid organizationId,
        UpdateOrganizationRequest request,
        string actorSubjectId,
        CancellationToken cancellationToken)
    {
        RequireId(organizationId, "Organization");
        RequireActor(actorSubjectId);
        RequireText(request.Name, "Organization name");
        RequireText(request.DefaultTimeZone, "Organization time zone");
        RequireStatus(request.Status, OrganizationStatuses, "organization");
        return repository.UpdateOrganizationAsync(organizationId, request with
        {
            Name = request.Name.Trim(),
            Status = request.Status.Trim().ToLowerInvariant(),
            DefaultTimeZone = request.DefaultTimeZone.Trim()
        }, actorSubjectId.Trim(), cancellationToken);
    }

    public Task<bool> ChangeMembershipAsync(
        Guid organizationId,
        string subjectId,
        ChangeOrganizationMembershipRequest request,
        string actorSubjectId,
        CancellationToken cancellationToken)
    {
        RequireId(organizationId, "Organization");
        RequireText(subjectId, "Member subject");
        RequireActor(actorSubjectId);
        RequireText(request.SubjectId, "Member subject");
        if (!string.Equals(subjectId.Trim(), request.SubjectId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("The route subject and request subject must match.");
        RequireStatus(request.Status, MembershipStatuses, "membership");
        return repository.ChangeMembershipAsync(organizationId, subjectId.Trim(), request with
        {
            SubjectId = request.SubjectId.Trim(),
            Status = request.Status.Trim().ToLowerInvariant()
        }, actorSubjectId.Trim(), cancellationToken);
    }

    public Task<ProductRegistration> RegisterProductAsync(
        RegisterProductRequest request,
        string actorSubjectId,
        CancellationToken cancellationToken)
    {
        RequireActor(actorSubjectId);
        string applicationCode = request.ApplicationCode?.Trim() ?? string.Empty;
        RequireText(applicationCode, "Product code");
        RequireText(request.Name, "Product name");
        if (!System.Text.RegularExpressions.Regex.IsMatch(applicationCode, "^[a-z0-9][a-z0-9_-]{0,63}$"))
            throw new ArgumentException("Product code has an invalid format.");
        return repository.RegisterProductAsync(request with
        {
            ApplicationCode = applicationCode,
            Name = request.Name.Trim()
        }, actorSubjectId.Trim(), cancellationToken);
    }

    public Task<bool> ChangeProductAccessAsync(
        Guid organizationId,
        ChangeOrganizationProductAccessRequest request,
        string actorSubjectId,
        CancellationToken cancellationToken)
    {
        RequireId(organizationId, "Organization");
        RequireActor(actorSubjectId);
        RequireText(request.ApplicationCode, "Product code");
        RequireStatus(request.Status, ProductAccessStatuses, "product access");
        return repository.ChangeProductAccessAsync(organizationId, request with
        {
            ApplicationCode = request.ApplicationCode.Trim(),
            Status = request.Status.Trim().ToLowerInvariant()
        }, actorSubjectId.Trim(), cancellationToken);
    }

    private static void RequireActor(string actorSubjectId) => RequireText(actorSubjectId, "Actor subject");
    private static void RequireId(Guid id, string label) { if (id == Guid.Empty) throw new ArgumentException($"{label} identifier is required."); }
    private static void RequireText(string? value, string label) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{label} is required."); }
    private static void RequireStatus(string status, HashSet<string> allowed, string label)
    {
        RequireText(status, $"{label} status");
        if (!allowed.Contains(status.Trim().ToLowerInvariant())) throw new ArgumentException($"Invalid {label} status.");
    }
}

public interface IPlatformDirectoryManagementRepository : IPlatformDirectoryManagement;
