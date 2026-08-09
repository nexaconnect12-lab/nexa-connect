namespace NexaConnect.Services.Kitchen.Application;

public enum KitchenTicketStatus
{
    Queued,
    InProgress,
    Ready,
    Completed,
    Cancelled
}

public sealed record KitchenTicketLine(
    Guid ProductId,
    string Name,
    int Quantity,
    string PreparationStation);

public sealed record CreateKitchenTicket(
    Guid OrderId,
    Guid BranchId,
    IReadOnlyCollection<KitchenTicketLine> Lines);

public sealed record KitchenTicket(
    Guid TicketId,
    Guid OrderId,
    Guid BranchId,
    KitchenTicketStatus Status,
    DateTimeOffset QueuedAtUtc,
    IReadOnlyCollection<KitchenTicketLine> Lines);

public interface IKitchenTicketStore
{
    Task<KitchenTicket> CreateAsync(CreateKitchenTicket command, CancellationToken cancellationToken);
    Task<KitchenTicket?> GetAsync(Guid ticketId, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid orderId, CancellationToken cancellationToken);
}

public sealed class KitchenOptions
{
    public Guid RestaurantId { get; set; }
}
