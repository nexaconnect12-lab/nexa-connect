using System.IO;

namespace NexaConnect.POS;

public sealed record CheckoutLine(Guid ProductId, int Quantity);

public sealed record PendingCheckout(Guid OrderId, Guid SettlementKey, Guid OrganizationId,
    Guid RestaurantId, Guid BranchId, Guid StoreId, Guid TerminalId, string Currency,
    string PaymentMethod, CheckoutLine[] Lines)
{
    public static PendingCheckout Create(PosClientConfiguration configuration, CheckoutLine[] lines)
    {
        configuration.ValidateCheckout();
        var result = new PendingCheckout(Guid.NewGuid(), Guid.NewGuid(), configuration.OrganizationId,
            configuration.RestaurantId, configuration.BranchId, configuration.StoreId, configuration.TerminalId,
            configuration.Currency, configuration.PaymentMethod, lines.ToArray());
        result.Validate(configuration);
        return result;
    }

    public void Validate(PosClientConfiguration configuration)
    {
        if (OrderId == Guid.Empty || SettlementKey == Guid.Empty || Lines is null || Lines.Length == 0 ||
            Lines.Any(line => line is null || line.ProductId == Guid.Empty || line.Quantity <= 0) ||
            OrganizationId != configuration.OrganizationId || RestaurantId != configuration.RestaurantId ||
            BranchId != configuration.BranchId || StoreId != configuration.StoreId || TerminalId != configuration.TerminalId ||
            Currency != configuration.Currency || PaymentMethod != configuration.PaymentMethod)
            throw new InvalidDataException("Pending checkout differs from terminal configuration or is invalid. Restore the original configuration and reconcile; do not submit another order.");
    }
}
