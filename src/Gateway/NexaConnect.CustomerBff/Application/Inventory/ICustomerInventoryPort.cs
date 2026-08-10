using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Inventory;

public interface ICustomerInventoryPort
{
    Task<HttpResponseMessage> GetStockAsync(TenantContext tenant, Guid branchId, string accessToken, CancellationToken cancellationToken);
}
