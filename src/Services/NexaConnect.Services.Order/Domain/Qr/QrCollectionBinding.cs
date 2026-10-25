namespace NexaConnect.Services.Order.Domain.Qr;

// Immutable custody: a reference identifies an Order but cannot transfer collection authority.
public sealed record QrCollectionBinding(Guid OrderId, Guid OrganizationId, Guid BranchId, Guid TerminalId,
    Guid StoreId, Guid ShiftId, Guid SettlementKey, string Actor)
{
    public static bool CanReadOriginalOrder(string channel, OrderStatus status, bool hasPricing, bool hasProviderIntent) =>
        channel == "qr" && status is OrderStatus.KitchenAccepted or OrderStatus.Paid && hasPricing && !hasProviderIntent;

    public void RequireOwner(Guid order, Guid organization, Guid branch, Guid terminal, Guid operation, string actor)
    {
        if (OrderId != order || OrganizationId != organization || BranchId != branch || TerminalId != terminal
            || SettlementKey != operation || Actor != actor)
            throw new InvalidOperationException("Use the original collecting cashier, terminal and operation.");
    }
}
