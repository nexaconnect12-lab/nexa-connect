using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.Order.Application.Orders;

public interface IOrderReceiptRepository
{
    Task<PaidOrderReceipt?> GetReceiptAsync(Guid organizationId, Guid branchId, Guid orderId, CancellationToken cancellationToken);
}
public sealed class OrderReceiptService(IOrderReceiptRepository receipts, IOrderTenantAuthorizer authorizer)
{
    public async Task<PaidOrderReceipt?> ReadAsync(Guid organizationId, Guid branchId, Guid orderId,
        string bearer, CancellationToken cancellationToken)
    {
        if (!await authorizer.HasBranchAccessAsync(organizationId, branchId, ProductPermissions.OrderRead, bearer, cancellationToken))
            throw new UnauthorizedAccessException();
        return await receipts.GetReceiptAsync(organizationId, branchId, orderId, cancellationToken);
    }
}
