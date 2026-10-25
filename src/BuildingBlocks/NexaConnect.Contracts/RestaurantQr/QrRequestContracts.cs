using System.Text.Json.Serialization;
namespace NexaConnect.Contracts.RestaurantQr;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrSubmission(Guid QuoteId, string Fingerprint);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrRejection(long ExpectedVersion, string Reason);
public sealed record QrRequestView(Guid Id, string TableCode, string Status, long Version,
    DateTimeOffset SubmittedAtUtc, DateTimeOffset DeadlineUtc, string? Reason, QrQuote Quote,
    Guid? OrderId=null,string? OrderStatus=null,QrQuote? ReviewQuote=null,QrGuestReceipt? Receipt=null,string? CollectionStatus=null)
{
    public string? CollectionReference => CollectionStatus != "unavailable" && OrderId is { } id ? $"QR-{id:N}" : null;
}
public sealed record QrGuestReceipt(string ReceiptNumber,DateTimeOffset PaidAtUtc,string Currency,string Tender,
    IReadOnlyList<QrGuestReceiptLine> Lines,decimal SubtotalAmount,decimal ServiceChargeAmount,decimal TaxAmount,decimal TotalAmount);
public sealed record QrGuestReceiptLine(string Name,decimal UnitPrice,int Quantity,decimal Total);
public sealed record QrRequestList(IReadOnlyList<QrRequestView> Items, bool HasMore);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrAcceptance(long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrPriceConfirmation(long ExpectedVersion,string Fingerprint);
public sealed record QrAcceptanceContext(QrGuestContext Context,bool DineInEnabled);
