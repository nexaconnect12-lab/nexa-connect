using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.POS;
using NexaConnect.Services.Order.Application.Tenant;
namespace NexaConnect.Services.Order.Application.ManualTenders;
public interface IManualTenderEvidenceStore
{
 Task<OrderManualTenderSettledV1?> ReadAsync(Guid organization,Guid restaurant,Guid branch,Guid eventId,CancellationToken ct);
 Task<ManualRefundSaleV1?> ReadRefundSaleAsync(Guid organization, Guid restaurant, Guid branch, Guid order, CancellationToken ct);
}
public sealed class ManualTenderEvidence(IManualTenderEvidenceStore store,IOrderTenantAuthorizer authorization)
{
 public async Task<OrderManualTenderSettledV1?> ReadAsync(Guid organization,Guid restaurant,Guid branch,Guid eventId,string bearer,CancellationToken ct)
 {
  if(new[]{organization,restaurant,branch,eventId}.Any(x=>x==Guid.Empty))throw new ArgumentException();
  if(!await authorization.HasBranchFinancialAccessAsync(organization,restaurant,branch,bearer,ct))throw new UnauthorizedAccessException();
  return await store.ReadAsync(organization,restaurant,branch,eventId,ct);
 }

 public async Task<ManualRefundSaleV1?> ReadRefundSaleAsync(Guid organization, Guid restaurant, Guid branch,
     Guid order, string bearer, CancellationToken ct)
 {
     if (new[] { organization, restaurant, branch, order }.Any(id => id == Guid.Empty)) throw new ArgumentException();
     if (!await authorization.HasBranchFinancialAccessAsync(organization, restaurant, branch, bearer, ct)
         || !await authorization.HasBranchAccessAsync(organization, branch, ProductPermissions.PosManualRefundApprove, bearer, ct))
         throw new UnauthorizedAccessException();
     return await store.ReadRefundSaleAsync(organization, restaurant, branch, order, ct);
 }
}
