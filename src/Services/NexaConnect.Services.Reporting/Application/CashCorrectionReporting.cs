using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Domain;

namespace NexaConnect.Services.Reporting.Application;

public interface ICashCorrectionFactRepository
{
    Task<bool> ProjectAsync(CashCorrectionFact fact, CancellationToken ct);
    Task<CashCorrectionReport> CompareAsync(CashCorrectionManifest source, CancellationToken ct);
}
public interface ICashCorrectionSource
{
    Task<CashCorrectionManifest> ReadAsync(ReportingRange range, string authorization, CancellationToken ct);
}
public sealed class CashCorrectionReporting(ICashCorrectionFactRepository repository,
    ICashCorrectionSource source, IReportingCustomerAuthorizer access)
{
    public static CashCorrectionFact Translate(PosLateCashCorrectionPostedV1 value)
    {
        if (value.CorrelationId == Guid.Empty) throw new ArgumentException("Correlation identity is required.");
        var fact = new CashCorrectionFact(value.EventId, value.OrganizationId, value.RestaurantId, value.BranchId,
            value.CorrectionId, value.WorkId, value.OriginalSettlementId, value.OrderId, value.TenderId, value.DrawerId,
            value.ReviewedVersion, value.PostingDate, value.PostingFromUtc.ToUniversalTime(), value.PostingToUtc.ToUniversalTime(),
            value.OccurredAtUtc.ToUniversalTime(), value.Currency,
            decimal.Parse(value.CashVarianceAdjustment.ToString("G29", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture));
        fact.Validate(); return fact;
    }
    public Task<bool> ProjectAsync(PosLateCashCorrectionPostedV1 value, CancellationToken ct) =>
        repository.ProjectAsync(Translate(value), ct);

    public static void ValidateManifest(CashCorrectionManifest manifest, ReportingRange range)
    {
        FinancialCompleteness.ValidateRange(range);
        if (manifest.OrganizationId != range.OrganizationId || manifest.BranchId != range.BranchId
            || manifest.RestaurantId == Guid.Empty || manifest.FromUtc != range.FromUtc || manifest.ToUtc != range.ToUtc
            || manifest.ObservedAtUtc < range.ToUtc || manifest.ObservedAtUtc > DateTimeOffset.UtcNow.AddSeconds(5)
            || manifest.Events is null || manifest.Events.Count > 1000
            || manifest.Events.Select(x => x.EventId).Distinct().Count() != manifest.Events.Count
            || manifest.Events.Select(x => x.CorrectionId).Distinct().Count() != manifest.Events.Count
            || manifest.Events.Select(x => x.WorkId).Distinct().Count() != manifest.Events.Count
            || manifest.Events.Select(x => x.TenderId).Distinct().Count() != manifest.Events.Count
            || manifest.Events.Select(x => x.OrderId).Distinct().Count() != manifest.Events.Count)
            throw new ArgumentException("Invalid correction source observation.");
        foreach (var value in manifest.Events)
        {
            var fact = Translate(value);
            if (fact.OrganizationId != range.OrganizationId || fact.RestaurantId != manifest.RestaurantId
                || fact.BranchId != range.BranchId || fact.PostedAtUtc < range.FromUtc || fact.PostedAtUtc >= range.ToUtc)
                throw new ArgumentException("Correction source scope mismatch.");
        }
    }
    public async Task<CashCorrectionReport> ReadAsync(ReportingRange range, string authorization, CancellationToken ct)
    {
        FinancialCompleteness.ValidateRange(range);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        if (!await access.IsGrantedAsync(range.OrganizationId, range.BranchId, "reporting.sales.read", authorization, deadline.Token))
            throw new UnauthorizedAccessException();
        var manifest = await source.ReadAsync(range, authorization, deadline.Token);
        ValidateManifest(manifest, range);
        return await repository.CompareAsync(manifest, deadline.Token);
    }
}
