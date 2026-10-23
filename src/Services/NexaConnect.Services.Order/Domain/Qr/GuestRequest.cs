namespace NexaConnect.Services.Order.Domain.Qr;

public sealed record GuestRequestScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid VisitorId, Guid EntryId, string TableCode);
public sealed class GuestRequestConflictException : Exception;
public sealed class GuestRequestLimitException : Exception;

public sealed class GuestRequest(Guid id, GuestRequestScope scope, string fingerprint, DateTimeOffset submittedAtUtc,
    DateTimeOffset deadlineUtc, string status = "pending", long version = 1, string? reason = null)
{
    public Guid Id { get; } = id;
    public GuestRequestScope Scope { get; } = scope;
    public string Fingerprint { get; } = fingerprint;
    public DateTimeOffset SubmittedAtUtc { get; } = submittedAtUtc;
    public DateTimeOffset DeadlineUtc { get; } = deadlineUtc;
    public string Status { get; private set; } = status;
    public long Version { get; private set; } = version;
    public string? Reason { get; private set; } = reason;
    public bool Expire(DateTimeOffset now)
    {
        if (Status != "pending" || now < DeadlineUtc) return false;
        Status = "expired"; Version++; return true;
    }
    public void Reject(long expectedVersion, string rejection, DateTimeOffset now)
    {
        if (rejection is not ("unavailable" or "closing" or "staff_review")) throw new ArgumentException("Invalid rejection category.");
        if (Status != "pending" || Version != expectedVersion || now >= DeadlineUtc) throw new GuestRequestConflictException();
        Status = "rejected"; Reason = rejection; Version++;
    }
    public static void Admit(int visitorRetained, int entryHour, int branchHour, int visitorPending, int entryPending, int branchPending)
    {
        if (visitorPending > 0) throw new GuestRequestConflictException();
        if (visitorRetained >= 10 || entryHour >= 30 || branchHour >= 500 || entryPending >= 10 || branchPending >= 200)
            throw new GuestRequestLimitException();
    }
    public static void AdmitQuote(int visitor, int entry, int branch)
    {
        if (visitor >= 10 || entry >= 100 || branch >= 1000) throw new GuestRequestLimitException();
    }
}
