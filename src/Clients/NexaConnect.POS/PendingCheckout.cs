using System.IO;

namespace NexaConnect.POS;

public sealed record CheckoutLine(Guid ProductId, int Quantity);

public sealed record PendingCheckout(Guid OrderId, Guid SettlementKey, Guid OrganizationId,
    Guid RestaurantId, Guid BranchId, Guid StoreId, Guid TerminalId, string Currency,
    string PaymentMethod, CheckoutLine[] Lines, string? PricingFingerprint = null, bool NeedsPricingReview = false,
    Guid? QrShiftId = null, string? QrCashier = null)
{
    public bool IsQrCollection => QrShiftId is not null;
    public static Guid ParseCollectionReference(string value)
    {
        if (value.Length > 36) throw new ArgumentException("Enter the original QR collection reference or Order ID.");
        string id = value.StartsWith("QR-", StringComparison.OrdinalIgnoreCase) ? value[3..] : value;
        return (Guid.TryParseExact(id,"N",out var parsed) || Guid.TryParseExact(id,"D",out parsed)) && parsed != Guid.Empty
            ? parsed : throw new ArgumentException("Enter the original QR collection reference or Order ID.");
    }
    public static PendingCheckout ForQr(PosClientConfiguration configuration, string reference, Guid shiftId, string actor)
    {
        if (configuration.PaymentMethod == "card_omise_test" || shiftId == Guid.Empty || string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("QR collection requires a manual-tender terminal and authenticated cashier shift.");
        var checkout = new PendingCheckout(ParseCollectionReference(reference), Guid.NewGuid(), configuration.OrganizationId,
            configuration.RestaurantId, configuration.BranchId, configuration.StoreId, configuration.TerminalId,
            configuration.Currency,configuration.PaymentMethod,[],QrShiftId:shiftId,QrCashier:actor);
        checkout.Validate(configuration); return checkout;
    }
    public static PendingCheckout Create(PosClientConfiguration configuration, CheckoutLine[] lines, string? paymentMethod = null)
    {
        configuration.ValidateCheckout();
        var result = new PendingCheckout(Guid.NewGuid(), Guid.NewGuid(), configuration.OrganizationId,
            configuration.RestaurantId, configuration.BranchId, configuration.StoreId, configuration.TerminalId,
            configuration.Currency, paymentMethod ?? configuration.PaymentMethod, lines.ToArray());
        result.Validate(configuration);
        return result;
    }

    public void Validate(PosClientConfiguration configuration)
    {
        if (OrderId == Guid.Empty || SettlementKey == Guid.Empty || Lines is null || (!IsQrCollection && Lines.Length == 0) ||
            (IsQrCollection && (QrShiftId == Guid.Empty || string.IsNullOrWhiteSpace(QrCashier) || QrCashier.Length > 200 || NeedsPricingReview)) ||
            (!IsQrCollection && QrCashier is not null) ||
            Lines.Any(line => line is null || line.ProductId == Guid.Empty || line.Quantity <= 0) ||
            OrganizationId != configuration.OrganizationId || RestaurantId != configuration.RestaurantId ||
            BranchId != configuration.BranchId || StoreId != configuration.StoreId || TerminalId != configuration.TerminalId ||
            Currency != configuration.Currency || !configuration.AllowsCheckoutMethod(PaymentMethod)
            || IsQrCollection && (PaymentMethod != configuration.PaymentMethod || PaymentMethod == "card_omise_test"))
            throw new InvalidDataException("Pending checkout differs from terminal configuration or is invalid. Restore the original configuration and reconcile; do not submit another order.");
    }
}
