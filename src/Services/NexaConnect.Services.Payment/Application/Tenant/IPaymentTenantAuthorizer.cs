using NexaConnect.Services.Payment.Application.Intents;

namespace NexaConnect.Services.Payment.Application.Tenant;

public interface IPaymentTenantAuthorizer
{
    Task<bool> CanAccessAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId,
        string authorizationHeader, CancellationToken cancellationToken);
}
