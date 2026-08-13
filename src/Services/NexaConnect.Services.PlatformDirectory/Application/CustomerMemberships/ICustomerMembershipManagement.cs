using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.Access;

namespace NexaConnect.Services.PlatformDirectory.Application.CustomerMemberships;

public sealed class CustomerMembershipConflictException(string message) : Exception(message);

public interface ICustomerMembershipRepository
{
    Task<IReadOnlyCollection<CustomerMembershipSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<CustomerMembershipSummary?> ChangeAsync(Guid organizationId, string subjectId, string status, long? expectedVersion, string actorSubjectId, CancellationToken cancellationToken);
}

public sealed class CustomerMembershipManagement(
    IOrganizationAccessReader accessReader,
    ICustomerMembershipRepository repository,
    ILogger<CustomerMembershipManagement> logger)
{
    private static readonly HashSet<string> AllowedStatuses = ["invited", "active", "suspended", "removed"];

    public async Task<IReadOnlyCollection<CustomerMembershipSummary>?> ListAsync(Guid organizationId, string actorSubjectId, CancellationToken cancellationToken)
    {
        if (!await accessReader.HasNexaConnectAccessAsync(organizationId, actorSubjectId, cancellationToken))
        {
            logger.LogWarning("Customer membership list denied for organization {OrganizationId}, actor {ActorSubjectId}", organizationId, actorSubjectId);
            return null;
        }
        return await repository.ListAsync(organizationId, cancellationToken);
    }

    public async Task<CustomerMembershipSummary?> ChangeAsync(Guid organizationId, string subjectId, ChangeCustomerMembershipRequest request, string actorSubjectId, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization identifier is required.");
        if (string.IsNullOrWhiteSpace(subjectId)) throw new ArgumentException("Member subject is required.");
        if (!await accessReader.HasNexaConnectAccessAsync(organizationId, actorSubjectId, cancellationToken))
        {
            logger.LogWarning("Customer membership change denied for organization {OrganizationId}, target {TargetSubjectId}, actor {ActorSubjectId}", organizationId, subjectId, actorSubjectId);
            return null;
        }
        if (string.Equals(subjectId, actorSubjectId, StringComparison.Ordinal))
            throw new CustomerMembershipConflictException("Customers cannot change their own membership.");
        string status = request.Status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AllowedStatuses.Contains(status)) throw new ArgumentException("Invalid membership status.");
        if (request.ExpectedVersion is <= 0) throw new ArgumentException("Expected version must be positive.");
        CustomerMembershipSummary? result = await repository.ChangeAsync(organizationId, subjectId.Trim(), status, request.ExpectedVersion, actorSubjectId, cancellationToken);
        if (result is not null) logger.LogInformation("Customer membership changed for organization {OrganizationId}, target {TargetSubjectId}, status {Status}, actor {ActorSubjectId}", organizationId, subjectId, status, actorSubjectId);
        return result;
    }
}
