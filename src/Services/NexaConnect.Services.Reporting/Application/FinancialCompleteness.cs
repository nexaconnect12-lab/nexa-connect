using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexaConnect.Services.Reporting.Domain;

namespace NexaConnect.Services.Reporting.Application;

public sealed record FinancialCompletenessSource(ReportingRange Range, DateTimeOffset OrderObservedAtUtc,
    DateTimeOffset RefundObservedAtUtc, int SaleCandidates, int RefundCandidates, int SaleEvidenceGaps,
    int RefundEvidenceGaps, int UnretainedSales, int UnretainedRefunds,
    IReadOnlyList<SaleFinancialFact> Sales, IReadOnlyList<RefundFinancialFact> Refunds)
{
    public void Validate()
    {
        FinancialCompleteness.ValidateRange(Range);
        if (OrderObservedAtUtc < Range.ToUtc || RefundObservedAtUtc < Range.ToUtc
            || OrderObservedAtUtc > DateTimeOffset.UtcNow || RefundObservedAtUtc > DateTimeOffset.UtcNow
            || SaleCandidates is < 0 or > 10000 || RefundCandidates is < 0 or > 10000
            || SaleEvidenceGaps < 0 || RefundEvidenceGaps < 0 || UnretainedSales < 0 || UnretainedRefunds < 0
            || Sales.Count + SaleEvidenceGaps != SaleCandidates || Refunds.Count + RefundEvidenceGaps != RefundCandidates
            || UnretainedSales > Sales.Count || UnretainedRefunds > Refunds.Count
            || Sales.Select(s=>s.OrderId).Distinct().Count()!=Sales.Count || Sales.Select(s=>s.PaymentId).Distinct().Count()!=Sales.Count
            || Sales.Select(s=>s.SourceEventId).Distinct().Count()!=Sales.Count
            || Refunds.Select(r=>r.RefundId).Distinct().Count()!=Refunds.Count
            || Refunds.Select(r=>r.SourceEventId).Distinct().Count()!=Refunds.Count)
            throw new ArgumentException("Financial source observation is incomplete or invalid.");
        foreach (var sale in Sales)
        {
            sale.Validate();
            if (sale.OrganizationId != Range.OrganizationId || sale.BranchId != Range.BranchId
                || (!InRange(sale.OrderedAtUtc) && !InRange(sale.PaidAtUtc))) throw new ArgumentException("Sale evidence is outside the selected scope.");
        }
        foreach (var refund in Refunds)
        {
            refund.Validate();
            if (refund.OrganizationId != Range.OrganizationId || refund.BranchId != Range.BranchId || !InRange(refund.RefundedAtUtc))
                throw new ArgumentException("Refund evidence is outside the selected scope.");
        }
    }
    private bool InRange(DateTimeOffset time)=>time>=Range.FromUtc && time<Range.ToUtc;
}

public sealed record FinancialFactCounts(int Expected,int Matched,int Missing,int Conflicting,int Unexpected)
{
    public int Gaps=>Missing+Conflicting+Unexpected;
}
public sealed record FinancialCompletenessObservation(Guid CheckId, ReportingRange Range, string Status,
    DateTimeOffset CheckedAtUtc,DateTimeOffset OrderObservedAtUtc,DateTimeOffset RefundObservedAtUtc,string ManifestHash,
    int SaleEvidenceGaps,int RefundEvidenceGaps,int UnretainedSales,int UnretainedRefunds,
    FinancialFactCounts Sales,FinancialFactCounts Payments,FinancialFactCounts Refunds);

public interface IFinancialCompletenessRepository
{
    Task<FinancialCompletenessObservation> CheckAsync(FinancialCompletenessSource source,CancellationToken cancellationToken);
    Task RecordAsync(FinancialCompletenessObservation observation,string actor,CancellationToken cancellationToken);
    Task<FinancialCompletenessObservation?> LatestAsync(ReportingRange range,CancellationToken cancellationToken);
}
public sealed class FinancialCompleteness(IFinancialCompletenessRepository repository)
{
    public static void ValidateRange(ReportingRange range)
    {
        if (range.OrganizationId==Guid.Empty || range.BranchId==Guid.Empty || range.FromUtc==default
            || range.ToUtc<=range.FromUtc || range.ToUtc-range.FromUtc>TimeSpan.FromDays(31) || range.ToUtc>DateTimeOffset.UtcNow)
            throw new ArgumentException("Explicit organization, branch and a closed positive maximum 31-day UTC window are required.");
    }
    public Task<FinancialCompletenessObservation> CheckAsync(FinancialCompletenessSource source,CancellationToken cancellationToken)
    { source.Validate(); return repository.CheckAsync(source,cancellationToken); }
    public Task<FinancialCompletenessObservation?> LatestAsync(Guid organization,Guid? branch,DateTimeOffset? from,DateTimeOffset? to,CancellationToken cancellationToken)
    {
        if (branch is null || from is null || to is null) throw new ArgumentException("Explicit branch and time range are required.");
        var range=new ReportingRange(organization,branch.Value,from.Value.ToUniversalTime(),to.Value.ToUniversalTime());
        ValidateRange(range); return repository.LatestAsync(range,cancellationToken);
    }
    public Task RecordAsync(FinancialCompletenessObservation observation,string actor,CancellationToken cancellationToken)
    {
        ValidateRange(observation.Range);
        if (string.IsNullOrWhiteSpace(actor) || actor.Length>128 || actor.Any(char.IsControl)) throw new ArgumentException("Explicit bounded operator attribution is required.");
        return repository.RecordAsync(observation,actor,cancellationToken);
    }
    public static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static bool SameInstant(DateTimeOffset left,DateTimeOffset right)=>left.UtcTicks/10==right.UtcTicks/10;
}
