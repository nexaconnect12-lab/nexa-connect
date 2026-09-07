using System.Globalization;

namespace NexaConnect.POS;

/// <summary>Presentation only; service authorization and totals remain authoritative.</summary>
public static class CashierPresentation
{
    public static bool MatchesMenu(string name, string station, string? search, string? selectedStation) =>
        (string.IsNullOrWhiteSpace(search) || name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrEmpty(selectedStation) || selectedStation == "All stations" || station == selectedStation);

    public static string Money(decimal amount, string currency) =>
        $"{currency.ToUpperInvariant()} {amount.ToString("N2", CultureInfo.CurrentCulture)}";

    public static bool CanEditCart(bool signedIn, bool shiftOpen, bool busy, bool pendingPayment) =>
        signedIn && shiftOpen && !busy && !pendingPayment;

    public static bool MatchesCurrency(string itemCurrency, string orderCurrency) =>
        string.Equals(itemCurrency, orderCurrency, StringComparison.OrdinalIgnoreCase);
}
