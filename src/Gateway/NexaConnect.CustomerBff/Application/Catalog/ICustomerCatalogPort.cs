using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Catalog;

public interface ICustomerCatalogPort
{
    Task<HttpResponseMessage> GetMenuAsync(TenantContext tenant, Guid branchId, string accessToken, CancellationToken cancellationToken);
}
