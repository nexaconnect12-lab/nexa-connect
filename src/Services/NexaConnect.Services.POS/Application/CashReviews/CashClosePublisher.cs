using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Application.CashReviews;

public sealed record CashCloseCandidate(Guid SessionId, Guid RestaurantId, Guid BranchId);
public interface ICashClosePublicationStore
{
    Task<IReadOnlyList<CashCloseCandidate>> FindAsync(Guid? after, CancellationToken ct);
    Task<bool> PublishAsync(CashCloseCandidate candidate, Guid organizationId, Guid correlationId, CancellationToken ct);
}

public sealed class CashClosePublisher(ICashClosePublicationStore store, IRestaurantScopeReader scopes)
{
    public Task<IReadOnlyList<CashCloseCandidate>> FindAsync(Guid? after, CancellationToken ct) => store.FindAsync(after, ct);
    public async Task<bool> PublishAsync(CashCloseCandidate candidate, Guid correlationId, CancellationToken ct)
    {
        // Resolve organization through its owner before opening a database transaction.
        var scope = await scopes.GetAsync(candidate.BranchId, ct);
        if (scope.OrganizationId == Guid.Empty || scope.BranchId != candidate.BranchId || scope.RestaurantId != candidate.RestaurantId)
            throw new InvalidOperationException("Cash-close publication scope mismatch.");
        return await store.PublishAsync(candidate, scope.OrganizationId, correlationId, ct);
    }
}
