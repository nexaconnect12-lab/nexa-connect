namespace NexaConnect.Services.Restaurant.Domain.Qr;

public sealed class QrConflictException : Exception;
public sealed class QrLimitException : Exception;

public sealed record QrScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    string RestaurantName, string BranchName, string Currency);

public sealed class QrEntry
{
    public Guid Id { get; }
    public QrScope Scope { get; }
    public string TableCode { get; }
    public string SecretHash { get; private set; }
    public long Version { get; private set; }
    public bool Active { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public QrEntry(Guid id, QrScope scope, string tableCode, string secretHash, long version, bool active, DateTimeOffset expiresAtUtc)
    {
        if (id == Guid.Empty || scope.OrganizationId == Guid.Empty || scope.RestaurantId == Guid.Empty || scope.BranchId == Guid.Empty
            || !ValidHash(secretHash) || version < 1 || tableCode != NormalizeCode(tableCode)) throw new ArgumentException("Invalid QR entry.");
        Id = id; Scope = scope; TableCode = tableCode; SecretHash = secretHash; Version = version; Active = active; ExpiresAtUtc = expiresAtUtc;
    }
    public static string NormalizeCode(string? code)
    {
        string value = code?.Trim().ToUpperInvariant() ?? "";
        if (value.Length is < 1 or > 32 || value.Any(c => !(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')))
            throw new ArgumentException("Use a table code of 1–32 letters, digits, hyphens or underscores.");
        return value;
    }
    public void Rotate(long expectedVersion, string hash, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        if (!ValidHash(hash)) throw new ArgumentException("Invalid QR secret hash.");
        SecretHash = hash; Version = checked(Version + 1); Active = true; ExpiresAtUtc = now.AddDays(30);
    }
    public void Revoke(long expectedVersion)
    {
        RequireVersion(expectedVersion); Version = checked(Version + 1); Active = false;
    }
    private void RequireVersion(long expected) { if (expected != Version) throw new QrConflictException(); }
    private static bool ValidHash(string hash) => hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}

public static class QrGuestAdmission
{
    public static void RequireEntryCapacity(long count) { if (count >= 200) throw new QrLimitException(); }
    public static void RequireCapacity(long entryActive, long branchActive, long entryLastMinute)
    {
        if (entryActive >= 100 || branchActive >= 1000 || entryLastMinute >= 30) throw new QrLimitException();
    }
    public static DateTimeOffset Expiry(DateTimeOffset now, DateTimeOffset entryExpiry) =>
        entryExpiry < now.AddHours(2) ? entryExpiry : now.AddHours(2);
}
