namespace NexaConnect.Services.Inventory.Application.Reservations;

public sealed record StockItem(Guid ProductId, decimal AvailableQuantity);
public sealed record ReservationLine(Guid ProductId, decimal Quantity);
public sealed record ReserveStock(Guid OrderId, Guid BranchId, IReadOnlyCollection<ReservationLine> Lines);
public sealed record StockReservation(Guid ReservationId, Guid OrderId, Guid BranchId, IReadOnlyCollection<ReservationLine> Lines);

public interface IInventoryReservations
{
    IReadOnlyCollection<StockItem> GetStock(Guid branchId);
    StockItem SetStock(Guid branchId, Guid productId, decimal quantity);
    StockReservation Reserve(ReserveStock command);
    void Release(Guid orderId) { }
}
