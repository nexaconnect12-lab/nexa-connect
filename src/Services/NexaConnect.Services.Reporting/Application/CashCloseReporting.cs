using System.Globalization;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Reporting.Domain;

namespace NexaConnect.Services.Reporting.Application;

public sealed record CashCloseRow(CashCloseSnapshot Snapshot, DateTimeOffset ProjectedAtUtc);
public sealed record CashClosePage(IReadOnlyList<CashCloseRow> Items, string? NextCursor);
public sealed record CashCloseQuery(Guid OrganizationId, Guid BranchId, Guid StoreId,
    DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Limit, DateTimeOffset? BeforeUtc, Guid? BeforeId);
public interface ICashCloseRepository
{
    Task<bool> ProjectAsync(Guid eventId, CashCloseSnapshot snapshot, CancellationToken ct);
    Task<IReadOnlyList<CashCloseRow>> ReadAsync(CashCloseQuery query, CancellationToken ct);
}
public interface ICashCloseAccess
{
    Task<bool> CanReadAsync(Guid organization, Guid branch, Guid store, string authorization, CancellationToken ct);
}
public sealed class CashCloseReporting(ICashCloseRepository repository, ICashCloseAccess access)
{
    public static CashCloseSnapshot Translate(PosCashCloseSnapshotV1 value)
    {
        if (value.EventId == Guid.Empty || value.CorrelationId == Guid.Empty) throw new ArgumentException("Invalid event identity.");
        var snapshot = new CashCloseSnapshot(value.OrganizationId, value.RestaurantId, value.BranchId, value.StoreId,
            value.CashSessionId, value.ShiftId, value.ClosedAtUtc, value.Currency, value.ExpectedAmount, value.CountedAmount,
            value.VarianceAmount, value.SnapshotVersion, value.FinancialVersion, value.ReviewVersion, value.ReviewStatus, value.OccurredAtUtc);
        snapshot.Validate();
        return snapshot;
    }
    public Task<bool> ProjectAsync(PosCashCloseSnapshotV1 value, CancellationToken ct) => repository.ProjectAsync(value.EventId, Translate(value), ct);

    public async Task<CashClosePage> ReadAsync(Guid organization, Guid branch, Guid store,
        DateTimeOffset from, DateTimeOffset to, int limit, string? cursor, string authorization, CancellationToken ct)
    {
        if (organization == Guid.Empty || branch == Guid.Empty || store == Guid.Empty || from >= to ||
            to - from > TimeSpan.FromDays(31) || limit is < 1 or > 100) throw new ArgumentException("Invalid report scope or range.");
        if (!await access.CanReadAsync(organization, branch, store, authorization, ct)) throw new UnauthorizedAccessException();
        DateTimeOffset? before = null; Guid? id = null;
        if (cursor is not null)
        {
            string[] parts = cursor.Split('_');
            if (cursor.Length > 64 || parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) ||
                ticks < 0 || ticks > DateTimeOffset.MaxValue.Ticks || !Guid.TryParseExact(parts[1], "D", out Guid parsed) || parsed == Guid.Empty)
                throw new ArgumentException("Invalid report cursor.");
            before = new DateTimeOffset(ticks, TimeSpan.Zero); id = parsed;
        }
        var rows = await repository.ReadAsync(new(organization, branch, store, from.ToUniversalTime(), to.ToUniversalTime(), limit + 1, before, id), ct);
        var items = rows.Take(limit).ToArray();
        return new(items, rows.Count > limit ? $"{items[^1].Snapshot.ClosedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)}_{items[^1].Snapshot.SessionId:D}" : null);
    }
}
