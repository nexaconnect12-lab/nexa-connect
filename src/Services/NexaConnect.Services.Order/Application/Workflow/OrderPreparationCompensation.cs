using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Application.Workflow;

public static class OrderPreparationCompensation
{
    public static async Task CancelAndReleaseAsync(OrderAggregate order, IInventoryReservationPort inventory,
        IKitchenPort kitchen, CancellationToken cancellationToken)
    {
        // Kitchen atomically rejects cancellation if any preparation is completed. Stock must
        // remain reserved on that conflict or any unconfirmed Kitchen outcome. Both calls are
        // idempotent so a failed Inventory release can repeat the confirmed Kitchen cancellation.
        await kitchen.CancelTicketAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
        await inventory.ReleaseAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
    }
}
