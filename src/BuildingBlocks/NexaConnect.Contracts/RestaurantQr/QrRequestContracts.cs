using System.Text.Json.Serialization;
namespace NexaConnect.Contracts.RestaurantQr;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrSubmission(Guid QuoteId, string Fingerprint);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrRejection(long ExpectedVersion, string Reason);
public sealed record QrRequestView(Guid Id, string TableCode, string Status, long Version,
    DateTimeOffset SubmittedAtUtc, DateTimeOffset DeadlineUtc, string? Reason, QrQuote Quote);
public sealed record QrRequestList(IReadOnlyList<QrRequestView> Items, bool HasMore);
