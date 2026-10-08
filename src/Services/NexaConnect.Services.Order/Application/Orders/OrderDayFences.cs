using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Order.Domain;
namespace NexaConnect.Services.Order.Application.Orders;
public interface IOrderDayFenceStore
{
 Task<SourceDayFence> ExecuteAsync(SourceFenceCommand command,string actor,bool cancel,CancellationToken ct);
 Task<SourceDayFence?> ReadAsync(EndOfDayWindow window,Guid operation,CancellationToken ct);
}
public sealed class OrderDayFences(OrderDayRead authorization,NexaConnect.Services.Order.Application.Tenant.IOrderTenantAuthorizer permissions,IOrderDayFenceStore? store=null)
{
 public async Task<SourceDayFence> ExecuteAsync(SourceFenceCommand command,string bearer,string actor,bool cancel,CancellationToken ct)
 {
  await authorization.AuthorizeAsync(command.Window,bearer,ct);if(await permissions.GetBranchDecisionAsync(command.Window.OrganizationId,command.Window.BranchId,FinancialDayFence.PreparePermission,bearer,ct) is not {} id||id==Guid.Empty)throw new UnauthorizedAccessException();
  return await (store??throw new InvalidOperationException("Durable fences required.")).ExecuteAsync(command,actor,cancel,ct);
 }
 public async Task<SourceDayFence?> ReadAsync(EndOfDayWindow window,Guid operation,string bearer,CancellationToken ct)
 {if(operation==Guid.Empty)throw new ArgumentException();await authorization.AuthorizeAsync(window,bearer,ct);return await (store??throw new InvalidOperationException("Durable fences required.")).ReadAsync(window,operation,ct);}
}
