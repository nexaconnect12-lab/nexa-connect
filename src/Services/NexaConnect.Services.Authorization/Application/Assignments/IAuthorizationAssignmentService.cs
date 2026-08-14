namespace NexaConnect.Services.Authorization.Application.Assignments;

public sealed record AssignRoleCommand(string SubjectId, Guid OrganizationId, Guid? RestaurantId, Guid? BranchId, string RoleCode);
public sealed record RoleAssignmentResult(Guid AssignmentId);

public interface IAuthorizationAssignmentService
{
    Task<RoleAssignmentResult> AssignAsync(AssignRoleCommand command, string assignedBy, CancellationToken cancellationToken);
}

public interface IAuthorizationAssignmentRepository
{
    Task<RoleAssignmentResult> AssignAsync(AssignRoleCommand command, string assignedBy, CancellationToken cancellationToken);
}

public sealed class AuthorizationAssignmentService(IAuthorizationAssignmentRepository repository) : IAuthorizationAssignmentService
{
    public Task<RoleAssignmentResult> AssignAsync(AssignRoleCommand command, string assignedBy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.SubjectId) || string.IsNullOrWhiteSpace(command.RoleCode) || string.IsNullOrWhiteSpace(assignedBy))
            throw new ArgumentException("Subject, role, and assigning administrator are required.");
        if (command.OrganizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.");
        if (command.RestaurantId == Guid.Empty || command.BranchId == Guid.Empty)
            throw new ArgumentException("Scope identifiers must be non-empty when supplied.");
        if (command.RestaurantId is null && command.BranchId is not null)
            throw new ArgumentException("A branch scope requires a restaurant scope.");

        string roleCode = command.RoleCode.Trim().ToLowerInvariant();
        if (roleCode == "tenant-admin")
        {
            if (command.RestaurantId is not null || command.BranchId is not null)
                throw new ArgumentException("Tenant administrators must be assigned at organization scope.");
        }
        else if (roleCode == "store-manager")
        {
            if (command.RestaurantId is null || command.BranchId is not null)
                throw new ArgumentException("Store managers must be assigned at restaurant scope.");
        }
        else if (command.RestaurantId is null || command.BranchId is null)
        {
            throw new ArgumentException("The selected role requires a branch scope.");
        }
        return repository.AssignAsync(command with
        {
            SubjectId = command.SubjectId.Trim(),
            RoleCode = roleCode
        }, assignedBy.Trim(), cancellationToken);
    }
}
