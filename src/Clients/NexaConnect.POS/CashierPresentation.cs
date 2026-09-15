using System.Globalization;

namespace NexaConnect.POS;

/// <summary>Presentation only; service authorization and totals remain authoritative.</summary>
public static class CashierPresentation
{
    public static string NewShiftNumber(DateTimeOffset localNow, Guid nonce) =>
        $"SHIFT-{localNow:yyyyMMdd-HHmmss}-{nonce.ToString("N")[..6].ToUpperInvariant()}";

    public static string SessionGuidance(bool signedIn, bool shiftOpen, bool cashOpen,
        bool checkoutPending, bool paymentPending, bool movementsPending)
    {
        if (!signedIn) return "Sign in to continue. Saved shift and payment work will be retained.";
        if (paymentPending) return "Open Payment and confirm or verify the pending payment before closing cash, shift, or signing out.";
        if (checkoutPending) return "Open Checkout and choose Verify original order before closing cash, shift, or signing out.";
        if (cashOpen && movementsPending) return "Open Terminal & sync and resolve pending/rejected movements, then close the cash session in Shift & cash.";
        if (cashOpen) return "In Shift & cash, enter counted closing cash and choose Close cash session, then Close shift.";
        if (shiftOpen) return "A saved shift is open. Choose Close shift in Shift & cash before signing out. If it fails, read the status message below.";
        return "Open a shift to start selling, or choose Sign out.";
    }

    public static bool MatchesMenu(string name, string station, string? search, string? selectedStation) =>
        (string.IsNullOrWhiteSpace(search) || name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrEmpty(selectedStation) || selectedStation == "All stations" || station == selectedStation);

    public static string Money(decimal amount, string currency) =>
        $"{currency.ToUpperInvariant()} {amount.ToString("N2", CultureInfo.CurrentCulture)}";

    public static bool CanEditCart(bool signedIn, bool shiftOpen, bool busy, bool pendingPayment) =>
        signedIn && shiftOpen && !busy && !pendingPayment;

    public static bool CanAttemptOrder(bool signedIn, bool shiftOpen, bool pendingPayment,
        bool checkoutPending, int cartLineCount) =>
        signedIn && shiftOpen && !pendingPayment && (checkoutPending || cartLineCount > 0);

    public static bool MatchesCurrency(string itemCurrency, string orderCurrency) =>
        string.Equals(itemCurrency, orderCurrency, StringComparison.OrdinalIgnoreCase);
}
