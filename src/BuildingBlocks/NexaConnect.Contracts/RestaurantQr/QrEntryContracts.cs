namespace NexaConnect.Contracts.RestaurantQr;

public sealed record QrEntryView(Guid Id, string TableCode, long Version, bool Active, DateTimeOffset ExpiresAtUtc);
public sealed record CreateQrEntry(string TableCode);
public sealed record ChangeQrEntry(long ExpectedVersion);
public sealed record QrEntryIssue(QrEntryView Entry, string EntryToken)
{
    public override string ToString() => "QrEntryIssue { secret omitted }";
}
public sealed record QrEntryExchange(string EntryToken)
{
    public override string ToString() => "QrEntryExchange { secret omitted }";
}
public sealed record QrGuestContext(Guid VisitorId, Guid EntryId, Guid OrganizationId, Guid RestaurantId,
    Guid BranchId, string RestaurantName, string BranchName, string TableCode, string Currency, DateTimeOffset ExpiresAtUtc);
public sealed record QrSessionIssue(string SessionToken, QrGuestContext Context)
{
    public override string ToString() => "QrSessionIssue { secret omitted }";
}
