using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Payment.Domain;
namespace NexaConnect.Services.Payment.Application.Refunds;
public interface IPaymentDayFenceStore
{
 Task<SourceDayFence> ExecuteAsync(SourceFenceCommand command,string actor,bool cancel,CancellationToken ct);
 Task<SourceDayFence?> ReadAsync(EndOfDayWindow window,Guid operation,CancellationToken ct);
}
public sealed class PaymentDayFences(PaymentDayRead authorization,NexaConnect.Services.Payment.Application.Tenant.IPaymentTenantAuthorizer permissions,IPaymentDayFenceStore? store=null)
{
 public async Task<SourceDayFence> ExecuteAsync(SourceFenceCommand command,string bearer,string actor,bool cancel,CancellationToken ct)
 {
  await authorization.AuthorizeAsync(command.Window,bearer,ct);if(!await permissions.CanPrepareDayAsync(command.Window.OrganizationId,command.Window.RestaurantId,command.Window.BranchId,bearer,ct))throw new UnauthorizedAccessException();
  return await (store??throw new InvalidOperationException("Durable fences required.")).ExecuteAsync(command,actor,cancel,ct);
 }
 public async Task<SourceDayFence?> ReadAsync(EndOfDayWindow window,Guid operation,string bearer,CancellationToken ct)
 {if(operation==Guid.Empty)throw new ArgumentException();await authorization.AuthorizeAsync(window,bearer,ct);return await (store??throw new InvalidOperationException("Durable fences required.")).ReadAsync(window,operation,ct);}
}
