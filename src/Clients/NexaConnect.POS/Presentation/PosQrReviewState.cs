using System.Globalization;
using System.IO;
using NexaConnect.Contracts.RestaurantQr;

namespace NexaConnect.POS;

public sealed record PosQrRequest(Guid Id, string TableCode, string Status, long Version,
    DateTimeOffset DeadlineUtc, Guid? OrderId, string? OrderStatus, string Currency, decimal TotalAmount, string Details)
{
    public string StatusLabel => Status switch { "price_review" => "Guest price confirmation needed", "accepted" => "Accepted · " + OrderStatus, "pending" => "Pending", "rejected" => "Rejected", "expired" => "Expired", _ => Status };
    public static PosQrRequest From(QrRequestView value, string currency)
    {
        if (value is null || value.Id == Guid.Empty || value.Version < 1 || !PosQrReviewState.ValidFilter(value.Status)
            || string.IsNullOrWhiteSpace(value.TableCode) || value.TableCode.Length > 32 || value.TableCode.Any(char.IsControl)
            || value.DeadlineUtc == default || value.SubmittedAtUtc == default
            || (value.Status == "accepted" ? value.OrderId != value.Id : value.OrderId is not null)
            || value.OrderStatus is not (null or "submitted" or "inventory_reserved" or "kitchen_accepted" or "paid" or "cancelled"))
            throw new InvalidDataException("Invalid QR request.");
        ValidateQuote(value.Quote, currency);
        if (value.ReviewQuote is not null) ValidateQuote(value.ReviewQuote, currency);
        static string Money(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);
        static string Render(QrQuote quote) => string.Join("\n", quote.Lines.Select(l => $"{l.Quantity} × {l.Name} · {quote.Currency} {Money(l.UnitPrice)} = {Money(l.LineTotal)}"))
            + $"\nSubtotal {Money(quote.SubtotalAmount)} · Service {Money(quote.ServiceChargeAmount)} · Tax {Money(quote.TaxAmount)}\nTotal {quote.Currency} {Money(quote.TotalAmount)}";
        string details = Render(value.Quote);
        if (value.Reason is "unavailable" or "closing" or "staff_review") details += "\nRejection reason: " + value.Reason.Replace('_', ' ');
        if (value.ReviewQuote is { } review) details += "\n\nUpdated prices — only the original guest can confirm:\n" + Render(review);
        if (value.Status == "price_review") details += "\nWaiting for guest confirmation. Refreshing review does not accept the order.";
        if (value.OrderId is { } order) details += $"\n\nOriginal Order: {order:D}\nAcceptance is not payment. Use Orders to check preparation and collect once.";
        return new(value.Id, value.TableCode, value.Status, value.Version, value.DeadlineUtc, value.OrderId,
            value.OrderStatus, value.Quote.Currency, value.Quote.TotalAmount, details);
    }

    private static void ValidateQuote(QrQuote? quote, string currency)
    {
        if (quote is null || quote.Currency != currency || quote.Fingerprint?.Length != 64 || !quote.Fingerprint.All(char.IsAsciiHexDigit)
            || quote.Lines is null || quote.Lines.Count is < 1 or > 50 || quote.TotalAmount < 0
            || quote.SubtotalAmount < 0 || quote.ServiceChargeAmount < 0 || quote.TaxAmount < 0
            || quote.TotalAmount != quote.SubtotalAmount + quote.ServiceChargeAmount + quote.TaxAmount
            || quote.Lines.Any(l => l is null || l.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(l.Name)
                || l.Name.Length > 200 || l.Name.Any(char.IsControl) || l.Quantity is < 1 or > 99 || l.UnitPrice < 0 || l.LineTotal != l.UnitPrice * l.Quantity))
            throw new InvalidDataException("Invalid QR quote.");
    }
}

public sealed record PosQrRequestPage(IReadOnlyList<PosQrRequest> Items, bool HasMore);

// Memory-only, generation-fenced state. It never grants server permission or creates collection custody.
public sealed class PosQrReviewState
{
    public int Generation { get; private set; }
    public IReadOnlyList<PosQrRequest> Items { get; private set; } = [];
    public bool HasMore { get; private set; }
    public static bool ValidFilter(string state) => state is "pending" or "price_review" or "accepted" or "rejected" or "expired";
    public int Clear() { Items = []; HasMore = false; return ++Generation; }
    public bool Apply(int generation, PosQrRequestPage page)
    {
        if (generation != Generation) return false;
        Items = Array.AsReadOnly(page.Items.ToArray()); HasMore = page.HasMore; return true;
    }
    public bool CanDecide(Guid? id, bool signedIn, bool busy, bool confirmed) => signedIn && !busy && confirmed
        && Items.Any(i => i.Id == id && i.Status is "pending" or "price_review");
}
