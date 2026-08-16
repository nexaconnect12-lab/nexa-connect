namespace NexaConnect.Services.Kitchen.Application;

using NexaConnect.Services.Kitchen.Domain;

public sealed record KitchenTicketLine(Guid ProductId,string Name,int Quantity,string PreparationStation);
public sealed record CreateKitchenTicket(Guid RestaurantId,Guid OrderId,Guid BranchId,IReadOnlyCollection<KitchenTicketLine> Lines);
public sealed record KitchenMutationContext(string ActorSubjectId,Guid CorrelationId,string? RequestCorrelationId=null);
public sealed record TransitionKitchenTicket(KitchenTicketStatus TargetStatus,long ExpectedConcurrencyVersion,string? ReasonCode=null);
public sealed record KitchenTicket(Guid TicketId,Guid OrganizationId,Guid RestaurantId,Guid OrderId,Guid BranchId,Guid PreparationStationId,KitchenTicketStatus Status,long ConcurrencyVersion,DateTimeOffset QueuedAtUtc,IReadOnlyCollection<KitchenTicketLine> Lines);

public interface IKitchenTicketStore
{
 Task<KitchenTicket> CreateAsync(Guid organizationId,CreateKitchenTicket command,KitchenMutationContext context,CancellationToken cancellationToken);
 Task<KitchenTicket?> GetAsync(Guid organizationId,Guid ticketId,CancellationToken cancellationToken);
 Task<KitchenTicket> TransitionAsync(Guid organizationId,Guid ticketId,TransitionKitchenTicket command,KitchenMutationContext context,CancellationToken cancellationToken);
 Task<bool> CancelAsync(Guid organizationId,Guid branchId,Guid orderId,KitchenMutationContext context,CancellationToken cancellationToken);
}
