namespace NexaConnect.Services.Authorization.Application.Assignments;

public sealed record AssignRoleCommand(string SubjectId, Guid OrganizationId, Guid RestaurantId, Guid BranchId, string RoleCode);
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
        if (command.OrganizationId == Guid.Empty || command.RestaurantId == Guid.Empty || command.BranchId == Guid.Empty)
            throw new ArgumentException("Organization, restaurant, and branch are required.");
        return repository.AssignAsync(command, assignedBy, cancellationToken);
    }
}
