using System.Net.Http.Headers;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;

/// <summary>Translates Reporting's draft contract into POS-owned preparation evidence.</summary>
public sealed class HttpDayCloseEvidenceReader(HttpClient client, TimeProvider clock) : IDayCloseEvidenceReader
{
    private sealed record Calendar(Guid OrganizationId,Guid RestaurantId,Guid BranchId,string TimeZone,string Currency);
    private sealed record Projection(int CompletedOrders,decimal GrossSales,decimal Refunded,string? Currency);
    private sealed record Range(Guid OrganizationId,Guid BranchId,DateTimeOffset FromUtc,DateTimeOffset ToUtc);
    private sealed record Check(Guid CheckId,string Status,DateTimeOffset CheckedAtUtc,Range Range);
    private sealed record Draft(DateOnly BusinessDate,Calendar Branch,EndOfDayWindow Window,string Status,
        decimal GrossSales,decimal CompletedRefunds,decimal NetSales,decimal CashVariance,DayTender[] Tenders,
        OrderDaySummary Order,PaymentDaySummary Payment,PosDaySummary Pos,string[] Issues,Projection Projection,Check? RecordedCompleteness);
    public async Task<DayEvidence> ReadAsync(DayIdentity day,string token,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));ct=timeout.Token;
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/reporting/v1/customer/organizations/{day.OrganizationId:D}/reports/end-of-day?branchId={day.BranchId:D}&businessDate={day.BusinessDate:yyyy-MM-dd}");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var body=new MemoryStream();
        byte[] buffer=new byte[4096];int size;
        while((size=await stream.ReadAsync(buffer,ct))!=0){if(body.Length+size>131072)throw new InvalidOperationException("Draft too large.");body.Write(buffer,0,size);}
        var draft=JsonSerializer.Deserialize<Draft>(body.ToArray(),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidOperationException("Draft missing.");
        var b=draft.Branch;var w=draft.Window;var now=clock.GetUtcNow();
        if(b is null || w is null || draft.Order is null || draft.Payment is null || draft.Pos is null || draft.Tenders is null || draft.Issues is null || draft.Projection is null
            || draft.Status!="draft" || draft.BusinessDate!=day.BusinessDate
            || b.OrganizationId!=day.OrganizationId || b.RestaurantId!=day.RestaurantId || b.BranchId!=day.BranchId
            || w.OrganizationId!=day.OrganizationId || w.RestaurantId!=day.RestaurantId || w.BranchId!=day.BranchId
            || draft.Order.Window!=w || draft.Payment.Window!=w || draft.Pos.Window!=w
            || w.FromUtc==default || w.ToUtc>w.FromUtc.AddHours(27) || w.ToUtc<=w.FromUtc || w.ToUtc>now
            || draft.GrossSales<0 || draft.CompletedRefunds<0 || draft.NetSales!=draft.GrossSales-draft.CompletedRefunds
            || draft.GrossSales!=draft.Order.GrossSales || draft.CompletedRefunds!=draft.Payment.CompletedRefunds || draft.CashVariance!=draft.Pos.CashVariance
            || new[]{draft.Order.CompletedOrders,draft.Order.UnresolvedOrders,draft.Order.EvidenceGaps,draft.Payment.UnresolvedPayments,draft.Payment.UnresolvedRefunds,draft.Payment.EvidenceGaps,draft.Pos.OpenShifts,draft.Pos.OpenCashSessions,draft.Pos.PendingCashReviews}.Any(n=>n<0)
            || new[]{draft.Order.ObservedAtUtc,draft.Payment.ObservedAtUtc,draft.Pos.ObservedAtUtc}.Any(t=>t<w.ToUtc || t>now)
            || b.Currency is null || b.Currency.Length!=3 || b.Currency.Any(c=>c is <'A' or >'Z')
            || draft.Tenders.Any(t=>t.Amount<0 || string.IsNullOrWhiteSpace(t.Method) || t.Currency!=b.Currency)
            || draft.Order.Currencies is null || draft.Payment.Currencies is null || draft.Pos.Currencies is null
            || draft.Order.Currencies.Concat(draft.Payment.Currencies).Concat(draft.Pos.Currencies).Any(c=>c!=b.Currency)
            || draft.Issues.Length>32 || draft.Issues.Any(x=>string.IsNullOrWhiteSpace(x) || x.Length>100))
            throw new InvalidOperationException("Draft scope or evidence invalid.");
        var check=draft.RecordedCompleteness;
        if(check is not null && (check.CheckId==Guid.Empty || check.CheckedAtUtc<w.ToUtc || check.CheckedAtUtc>now || check.Range is null
            || check.Range.OrganizationId!=day.OrganizationId || check.Range.BranchId!=day.BranchId || check.Range.FromUtc!=w.FromUtc || check.Range.ToUtc!=w.ToUtc))throw new InvalidOperationException("Historical check scope invalid.");
        TimeZoneInfo zone;
        try {zone=TimeZoneInfo.FindSystemTimeZoneById(b.TimeZone);}catch(Exception e)when(e is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException){throw new InvalidOperationException("Calendar invalid.");}
        if(TimeZoneInfo.ConvertTime(w.FromUtc,zone).DateTime!=day.BusinessDate.ToDateTime(TimeOnly.MinValue)
            || TimeZoneInfo.ConvertTime(w.ToUtc,zone).DateTime!=day.BusinessDate.AddDays(1).ToDateTime(TimeOnly.MinValue))throw new InvalidOperationException("Calendar boundaries invalid.");
        var issues=draft.Issues.ToList();
        if(draft.Projection.GrossSales!=draft.GrossSales || draft.Projection.Refunded!=draft.CompletedRefunds || draft.Projection.CompletedOrders!=draft.Order.CompletedOrders)issues.Add("projection_totals_differ");
        if(draft.Projection.Currency is not null && draft.Projection.Currency!=b.Currency)throw new InvalidOperationException("Projection currency invalid.");
        if(draft.RecordedCompleteness is null)issues.Add("financial_evidence_not_checked");
        else if(draft.RecordedCompleteness.Status!="observed_complete")issues.Add("recorded_financial_gaps");
        if(draft.Order.EvidenceGaps+draft.Payment.EvidenceGaps>0)issues.Add("missing_source_evidence");
        return new(b.TimeZone,b.Currency,w.FromUtc,w.ToUtc,draft.GrossSales,draft.CompletedRefunds,draft.NetSales,draft.CashVariance,
            draft.Tenders.OrderBy(t=>t.Method,StringComparer.Ordinal).ThenBy(t=>t.Currency,StringComparer.Ordinal).ToArray(),draft.Order.EvidenceVersion,draft.Payment.EvidenceVersion,draft.Pos.EvidenceVersion,
            draft.Order.UnresolvedOrders,draft.Payment.UnresolvedPayments,draft.Payment.UnresolvedRefunds,draft.Pos.OpenShifts,draft.Pos.OpenCashSessions,draft.Pos.PendingCashReviews,issues.Distinct().Order().ToArray(),now,draft.Order.ObservedAtUtc,draft.Payment.ObservedAtUtc,draft.Pos.ObservedAtUtc,check?.CheckId,check?.CheckedAtUtc);
    }
}
