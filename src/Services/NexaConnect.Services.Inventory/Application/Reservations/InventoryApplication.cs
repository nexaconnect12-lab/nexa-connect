namespace NexaConnect.Services.Inventory.Application.Reservations;

public sealed record StockItem(Guid ProductId, decimal AvailableQuantity);
public sealed record ReservationLine(Guid ProductId, decimal Quantity);
public sealed record ReserveStock(Guid OrderId, Guid BranchId, IReadOnlyCollection<ReservationLine> Lines);
public sealed record StockReservation(Guid ReservationId, Guid OrderId, Guid BranchId, IReadOnlyCollection<ReservationLine> Lines);

public interface IInventoryReservations
{
    IReadOnlyCollection<StockItem> GetStock(Guid branchId);
    IReadOnlyCollection<StockItem> GetStock(Guid organizationId, Guid branchId) => GetStock(branchId);
    StockItem SetStock(Guid branchId, Guid productId, decimal quantity);
    StockItem SetStock(Guid organizationId, Guid branchId, Guid productId, decimal quantity) => SetStock(branchId, productId, quantity);
    StockReservation Reserve(ReserveStock command);
    StockReservation Reserve(Guid organizationId, ReserveStock command) => Reserve(command);
    void Release(Guid orderId) { }
    void Release(Guid organizationId, Guid orderId) => Release(orderId);
}
