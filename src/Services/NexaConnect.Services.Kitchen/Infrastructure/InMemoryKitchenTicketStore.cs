using System.Collections.Concurrent;
using NexaConnect.Services.Kitchen.Application;

namespace NexaConnect.Services.Kitchen.Infrastructure;

public sealed class InMemoryKitchenTicketStore : IKitchenTicketStore
{
    private readonly ConcurrentDictionary<Guid, KitchenTicket> tickets = new();

    public Task<KitchenTicket> CreateAsync(CreateKitchenTicket command, CancellationToken cancellationToken)
    {
        Validate(command);
        KitchenTicket ticket = new(
            Guid.NewGuid(), command.OrderId, command.BranchId, KitchenTicketStatus.Queued,
            DateTimeOffset.UtcNow, command.Lines.ToArray());
        tickets[ticket.TicketId] = ticket;
        return Task.FromResult(ticket);
    }

    public Task<KitchenTicket?> GetAsync(Guid ticketId, CancellationToken cancellationToken) =>
        Task.FromResult(tickets.TryGetValue(ticketId, out KitchenTicket? ticket) ? ticket : null);

    public Task<bool> CancelAsync(Guid orderId, CancellationToken cancellationToken)
    {
        foreach ((Guid ticketId, KitchenTicket ticket) in tickets)
        {
            if (ticket.OrderId != orderId) continue;
            tickets[ticketId] = ticket with { Status = KitchenTicketStatus.Cancelled };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private static void Validate(CreateKitchenTicket command)
    {
        if (command.OrderId == Guid.Empty || command.BranchId == Guid.Empty || command.Lines is null || command.Lines.Count == 0)
            throw new ArgumentException("Order, branch, and at least one kitchen line are required.");
        if (command.Lines.Any(line => line.ProductId == Guid.Empty || line.Quantity <= 0 ||
            string.IsNullOrWhiteSpace(line.Name) || string.IsNullOrWhiteSpace(line.PreparationStation)))
            throw new ArgumentException("Kitchen lines require a product, name, positive quantity, and preparation station.");
    }
}
