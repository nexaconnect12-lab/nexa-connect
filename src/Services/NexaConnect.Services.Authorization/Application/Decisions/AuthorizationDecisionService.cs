using NexaConnect.Services.Authorization.Domain;

namespace NexaConnect.Services.Authorization.Application.Decisions;

public sealed record AuthorizationQuery(string SubjectId, Guid OrganizationId, Guid? RestaurantId,
    Guid? BranchId, string Permission, decimal? Amount, string? Currency);

public interface IAuthorizationDecisionStore
{
    Task<AuthorizationEvidence> ReadAsync(AuthorizationQuery query, CancellationToken cancellationToken);
    Task RecordAsync(AuthorizationQuery query, AuthorizationDecision decision, CancellationToken cancellationToken);
}

public sealed class AuthorizationDecisionService(IAuthorizationDecisionStore store) : IAuthorizationDecisionService
{
    public async Task<AuthorizationDecision> DecideAsync(string subjectId, Guid organizationId, Guid? restaurantId,
        Guid? branchId, string permission, decimal? amount, string? currency, CancellationToken cancellationToken)
    {
        var query = new AuthorizationQuery(subjectId, organizationId, restaurantId, branchId, permission, amount, currency);
        AuthorizationEvidence evidence = await store.ReadAsync(query, cancellationToken);
        var decision = new AuthorizationDecision(Guid.NewGuid(), AuthorizationPolicy.Grants(evidence, amount), evidence.Limit);
        // Never return an unaudited grant. Each invocation re-reads current policy; there is no grant cache.
        await store.RecordAsync(query, decision, cancellationToken);
        return decision;
    }
}
