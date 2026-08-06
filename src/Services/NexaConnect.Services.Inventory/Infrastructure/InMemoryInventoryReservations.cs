using System.Collections.Concurrent;
using NexaConnect.Services.Inventory.Application.Reservations;

namespace NexaConnect.Services.Inventory.Infrastructure;

public sealed class InMemoryInventoryReservations : IInventoryReservations
{
    private readonly ConcurrentDictionary<(Guid BranchId, Guid ProductId), decimal> stock = new();
    private readonly ConcurrentDictionary<Guid, StockReservation> reservations = new();

    public IReadOnlyCollection<StockItem> GetStock(Guid branchId) => stock
        .Where(entry => entry.Key.BranchId == branchId)
        .Select(entry => new StockItem(entry.Key.ProductId, entry.Value))
        .OrderBy(item => item.ProductId)
        .ToArray();

    public StockItem SetStock(Guid branchId, Guid productId, decimal quantity)
    {
        if (branchId == Guid.Empty || productId == Guid.Empty || quantity < 0)
            throw new ArgumentException("Branch, product, and a non-negative quantity are required.");
        stock[(branchId, productId)] = quantity;
        return new StockItem(productId, quantity);
    }

    public StockReservation Reserve(ReserveStock command)
    {
        if (command.OrderId == Guid.Empty || command.BranchId == Guid.Empty || command.Lines.Count == 0)
            throw new ArgumentException("Order, branch, and at least one reservation line are required.");
        lock (stock)
        {
            foreach (ReservationLine line in command.Lines)
            {
                if (line.ProductId == Guid.Empty || line.Quantity <= 0 ||
                    !stock.TryGetValue((command.BranchId, line.ProductId), out decimal available) || available < line.Quantity)
                    throw new InvalidOperationException($"Insufficient stock for product {line.ProductId}.");
            }
            foreach (ReservationLine line in command.Lines)
                stock[(command.BranchId, line.ProductId)] -= line.Quantity;
        }
        var reservation = new StockReservation(Guid.NewGuid(), command.OrderId, command.BranchId, command.Lines);
        reservations[command.OrderId] = reservation;
        return reservation;
    }

    public void Release(Guid orderId)
    {
        if (!reservations.TryRemove(orderId, out var reservation)) return;
        lock (stock)
            foreach (var line in reservation.Lines)
                stock.AddOrUpdate((reservation.BranchId, line.ProductId), line.Quantity, (_, value) => value + line.Quantity);
    }
}
