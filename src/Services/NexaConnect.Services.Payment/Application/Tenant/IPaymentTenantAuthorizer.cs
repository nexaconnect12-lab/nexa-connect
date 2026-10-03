using NexaConnect.Services.Payment.Application.Intents;

namespace NexaConnect.Services.Payment.Application.Tenant;

public interface IPaymentTenantAuthorizer
{
    Task<bool> CanReadBranchFinancialsAsync(Guid organizationId, Guid restaurantId, Guid branchId,
        string authorizationHeader, CancellationToken cancellationToken) => Task.FromResult(false);
    Task<bool> CanAccessAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId, string permission,
        string authorizationHeader, CancellationToken cancellationToken);
    async Task<PaymentAccessDecision> DecideAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId,
        string permission, string authorizationHeader, CancellationToken cancellationToken,
        decimal? amount = null, string? currency = null) =>
        new(await CanAccessAsync(organizationId, restaurantId, branchId, orderId, permission, authorizationHeader,
            cancellationToken), Guid.Empty);
}

public sealed record PaymentAccessDecision(bool Granted, Guid DecisionId, decimal? EvaluatedLimit = null);
