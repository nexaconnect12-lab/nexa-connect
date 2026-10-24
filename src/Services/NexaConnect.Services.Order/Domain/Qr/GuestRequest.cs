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
        if (Status is not ("pending" or "price_review") || now < DeadlineUtc) return false;
        Status = "expired"; Version++; return true;
    }
    public void Reject(long expectedVersion, string rejection, DateTimeOffset now)
    {
        if (rejection is not ("unavailable" or "closing" or "staff_review")) throw new ArgumentException("Invalid rejection category.");
        if (Status is not ("pending" or "price_review") || Version != expectedVersion || now >= DeadlineUtc) throw new GuestRequestConflictException();
        Status = "rejected"; Reason = rejection; Version++;
    }
    public void ReviewPrice(long expectedVersion,DateTimeOffset now){if(Status is not ("pending" or "price_review"))throw new GuestRequestConflictException();Require(Status,expectedVersion,now);Status="price_review";Version++;}
    public void ConfirmPrice(long expectedVersion,DateTimeOffset now){Require("price_review",expectedVersion,now);Status="pending";Version++;}
    public void Accept(long expectedVersion,DateTimeOffset now){Require("pending",expectedVersion,now);Status="accepted";Version++;}
    private void Require(string state,long version,DateTimeOffset now)
    {if(Status!=state||Version!=version||now>=DeadlineUtc)throw new GuestRequestConflictException();}
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
