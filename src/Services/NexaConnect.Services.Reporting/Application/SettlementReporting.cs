using System.Globalization;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Domain;
namespace NexaConnect.Services.Reporting.Application;

public interface ISettlementFactRepository
{
    Task<bool> ProjectAsync(SettlementFact fact,CancellationToken ct);
    Task<SettlementReport> CompareAsync(SettlementManifest manifest,CancellationToken ct);
}
public interface ISettlementSource
{Task<SettlementManifest> ReadAsync(SettlementReportRange range,string authorization,CancellationToken ct);}
public sealed class SettlementReporting(ISettlementFactRepository repository,ISettlementSource source,IReportingCustomerAuthorizer access)
{
    private static decimal Normalize(decimal n)=>decimal.Parse(n.ToString("G29",CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture);
    public static SettlementFact Translate(SettlementPublication publication)
    {
        if((publication.V1 is null)==(publication.V2 is null))throw new ArgumentException("Exactly one original settlement version is required.");
        var v2=publication.V2;var s=publication.V1??v2!.Settlement;
        if(s is null||s.Tenders is null||s.Sources is null||s.Tenders.Any(t=>t is null)||s.Sources.Any(s=>s is null))throw new ArgumentException("Settlement payload absent.");
        if(v2 is not null&&(v2.EventId!=s.EventId||v2.CorrelationId!=s.CorrelationId||v2.OccurredAtUtc!=s.OccurredAtUtc||v2.Corrections?.Items is null))
            throw new ArgumentException("Settlement envelope identity mismatch.");
        var c=v2?.Corrections;
        if(c is not null&&c.Items.Any(i=>i is null))throw new ArgumentException("Correction item absent.");
        var fact=new SettlementFact(s.EventId,s.CorrelationId,s.SettlementId,s.OrganizationId,s.RestaurantId,s.BranchId,s.BusinessDate,
            s.OccurredAtUtc.ToUniversalTime(),s.ApprovalId,s.SealVersion,s.Currency,Normalize(s.GrossSales),Normalize(s.CompletedRefunds),Normalize(s.NetSales),Normalize(s.CashVariance),
            s.Tenders.OrderBy(t=>t.Method,StringComparer.Ordinal).ThenBy(t=>t.Currency,StringComparer.Ordinal).Select(t=>new SettlementTender(t.Method,t.Currency,Normalize(t.Amount))).ToArray(),
            s.Sources.OrderBy(s=>s.Source,StringComparer.Ordinal).Select(s=>new SettlementSource(s.Source,s.SealId,s.Epoch,s.Revision)).ToArray(),v2 is null?1:2,
            v2?.DecisionId,v2?.FromUtc.ToUniversalTime(),v2?.ToUtc.ToUniversalTime(),c is null?null:new(c.ManifestId,c.RevisionEpoch,c.SourceRevision,c.ManifestHash,
                c.Expected,c.Matched,c.Missing,c.Conflicting,c.Unexpected,Normalize(c.Adjustment),c.Items.OrderBy(i=>i.EventId).Select(i=>new SettlementCorrectionItem(i.EventId,i.CorrectionId,
                    i.OriginalSettlementId,i.WorkId,i.OrderId,i.TenderId,i.DrawerId,i.PostingDate,i.PostedAtUtc.ToUniversalTime(),Normalize(i.Adjustment))).ToArray()));
        fact.Validate();return fact;
    }
    public static void ValidateRange(SettlementReportRange r)
    {
        if(r.OrganizationId==Guid.Empty||r.BranchId==Guid.Empty||r.FromDate==default||r.FromDate>=r.ToDate
            ||r.ToDate.DayNumber-r.FromDate.DayNumber>31||r.ToDate>DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))throw new ArgumentException("Invalid settlement date range.");
    }
    public static void ValidateManifest(SettlementManifest m,SettlementReportRange r)
    {
        ValidateRange(r);
        if(m.OrganizationId!=r.OrganizationId||m.BranchId!=r.BranchId||m.RestaurantId==Guid.Empty||m.FromDate!=r.FromDate||m.ToDate!=r.ToDate
            ||m.ObservedAtUtc==default||m.ObservedAtUtc>DateTimeOffset.UtcNow.AddSeconds(5)||m.Events is null||m.Events.Count>31)throw new ArgumentException("Invalid settlement manifest.");
        var facts=m.Events.Select(Translate).ToArray();
        if(facts.Any(f=>f.OrganizationId!=r.OrganizationId||f.RestaurantId!=m.RestaurantId||f.BranchId!=r.BranchId||f.BusinessDate<r.FromDate||f.BusinessDate>=r.ToDate||f.SettledAtUtc>m.ObservedAtUtc)
            ||facts.Select(f=>f.EventId).Distinct().Count()!=facts.Length||facts.Select(f=>f.SettlementId).Distinct().Count()!=facts.Length
            ||facts.Select(f=>f.BusinessDate).Distinct().Count()!=facts.Length||facts.Sum(f=>f.Corrections?.Expected??0)>1000)throw new ArgumentException("Settlement source provenance mismatch.");
    }
    public Task<bool> ProjectAsync(SettlementPublication value,CancellationToken ct)=>repository.ProjectAsync(Translate(value),ct);
    public async Task<SettlementReport> ReadAsync(SettlementReportRange range,string bearer,CancellationToken ct)
    {
        ValidateRange(range);using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));
        if(!await access.IsGrantedAsync(range.OrganizationId,range.BranchId,"reporting.sales.read",bearer,deadline.Token))throw new UnauthorizedAccessException();
        var manifest=await source.ReadAsync(range,bearer,deadline.Token);ValidateManifest(manifest,range);
        return await repository.CompareAsync(manifest,deadline.Token);
    }
}
