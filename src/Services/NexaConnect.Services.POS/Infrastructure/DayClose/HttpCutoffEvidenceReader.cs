using System.Net;
using System.Net.Http.Headers;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Domain.DayClose;

namespace NexaConnect.Services.POS.Infrastructure.DayClose;

public sealed class HttpCutoffEvidenceReader(IHttpClientFactory clients,ICutoffPreparationStore store,
    IDayCloseEvidenceReader calendar,TimeProvider clock):ICutoffEvidenceReader
{
    public async Task<DayEvidence> ReadAsync(DayIdentity day,string token,CancellationToken ct)
    {
        var state=await store.ReadAsync(day,ct)??throw new InvalidOperationException("Cutoff operation missing.");
        var context=await calendar.ReadAsync(day,token,ct);
        if(context.Currency!="THB")throw new InvalidOperationException("Cutoff supports THB only.");
        var w=new EndOfDayWindow(day.OrganizationId,day.RestaurantId,day.BranchId,context.FromUtc,context.ToUtc);
        var saved=state.Snapshot?.Cutoff;
        var operation=state.Status=="preparing"?state.OperationId:null;
        if(operation is null && saved is null)throw new InvalidOperationException("Cutoff evidence missing.");
        var order=await Source<OrderDaySummary>("Order",w,operation,saved?.Order.ManifestId,token,ct);
        var payment=await Source<PaymentDaySummary>("Payment",w,operation,saved?.Payment.ManifestId,token,ct);
        var pos=await Source<PosDaySummary>("POS",w,operation,saved?.Pos.ManifestId,token,ct);
        Validate(order,w);Validate(payment,w);Validate(pos,w);
        var o=order.Manifest.Summary;var p=payment.Manifest.Summary;var drawer=pos.Manifest.Summary;
        if(o.Window!=w || p.Window!=w || drawer.Window!=w || o.Currencies.Concat(p.Currencies).Concat(drawer.Currencies).Any(x=>x!="THB"))throw new InvalidOperationException("Cutoff scope or currency invalid.");
        var command=new CutoffReconciliationCommand(w,order.Manifest.ManifestId,payment.Manifest.ManifestId);
        var check=await Send<CutoffReconciliation>("Reporting",HttpMethod.Post,"api/reporting/v1/customer/day-cutoff-reconciliation",command,token,ct);
        if(check.Window!=w || check.OrderManifestId!=command.OrderManifestId || check.PaymentManifestId!=command.PaymentManifestId
            || check.CheckId==Guid.Empty || check.Status is not ("observed_complete" or "gaps_detected" or "sources_changed")
            || check.CheckedAtUtc<w.ToUtc || check.CheckedAtUtc>clock.GetUtcNow()
            || check.EvidenceProtocolVersion!=2 || check.OrderRevision!=order.Manifest.SourceRevision
            || check.PaymentRevision!=payment.Manifest.SourceRevision)throw new InvalidOperationException("Cutoff reconciliation invalid.");
        int gaps=0;
        if(check.Sales.Expected!=order.Manifest.Sales.Count || check.Payments.Expected!=order.Manifest.Sales.Count
            || check.Refunds.Expected!=payment.Manifest.Refunds.Count)throw new InvalidOperationException("Cutoff inventory count invalid.");
        foreach(var counts in new[]{check.Sales,check.Payments,check.Refunds})
        {
            if(counts is null || new[]{counts.Expected,counts.Matched,counts.Missing,counts.Conflicting,counts.Unexpected}.Any(x=>x<0 || x>20000)
                || counts.Expected!=counts.Matched+counts.Missing+counts.Conflicting)throw new InvalidOperationException("Cutoff counts invalid.");
            gaps=checked(gaps+counts.Missing+counts.Conflicting+counts.Unexpected);
        }
        gaps=checked(gaps+o.EvidenceGaps+p.EvidenceGaps);
        if(check.Status=="observed_complete" && gaps!=0)throw new InvalidOperationException("Cutoff completeness invalid.");
        if(check.DeliveryComplete && (check.Status!="observed_complete" || !check.SourcesCurrent || gaps!=0))
            throw new InvalidOperationException("Cutoff delivery evidence invalid.");
        // A final POS read catches cash delivered while Reporting was taking its snapshot.
        var finalPos=await Source<PosDaySummary>("POS",w,null,pos.Manifest.ManifestId,token,ct);Validate(finalPos,w);
        if(finalPos.Manifest.ManifestId!=pos.Manifest.ManifestId || finalPos.Manifest.EvidenceVersion!=pos.Manifest.EvidenceVersion
            || finalPos.Manifest.SourceRevision!=pos.Manifest.SourceRevision
            || finalPos.Manifest.EvidenceProtocolVersion!=pos.Manifest.EvidenceProtocolVersion)
            throw new InvalidOperationException("Cutoff POS identity changed.");
        bool current=order.Current&&payment.Current&&pos.Current&&finalPos.Current&&check.SourcesCurrent&&check.Status!="sources_changed";
        static CutoffReference Ref<T>(SourceCutoff<T> value)=>new(value.ManifestId,value.Generation,value.EvidenceVersion,
            value.SourceRevision?.Epoch,value.SourceRevision?.Revision);
        string[] issues=check.Status=="gaps_detected"?["cutoff_financial_gaps"]:[];
        return new(context.TimeZone,"THB",w.FromUtc,w.ToUtc,o.GrossSales,p.CompletedRefunds,o.GrossSales-p.CompletedRefunds,drawer.CashVariance,
            o.Tenders.Select(x=>new DayTender(x.Method,x.Currency,x.Amount)).OrderBy(x=>x.Method,StringComparer.Ordinal).ThenBy(x=>x.Currency,StringComparer.Ordinal).ToArray(),
            order.Manifest.EvidenceVersion,payment.Manifest.EvidenceVersion,pos.Manifest.EvidenceVersion,o.UnresolvedOrders,p.UnresolvedPayments,p.UnresolvedRefunds,
            drawer.OpenShifts,drawer.OpenCashSessions,drawer.PendingCashReviews,issues,clock.GetUtcNow(),order.Manifest.CapturedAtUtc,payment.Manifest.CapturedAtUtc,pos.Manifest.CapturedAtUtc,
            Cutoff:new(Ref(order.Manifest),Ref(payment.Manifest),Ref(pos.Manifest),check.CheckId,current,gaps,check.DeliveryComplete,2));
    }
    private static void Validate<T>(SourceCutoffRead<T> value,EndOfDayWindow w)
    {
        if(value is null || value.Manifest is null || value.Manifest.Window!=w || value.Manifest.ManifestId==Guid.Empty
            || value.Manifest.OperationId==Guid.Empty || value.Manifest.Generation<=0 || value.Manifest.EvidenceVersion.Length!=64
            || value.Manifest.EvidenceVersion.Any(x=>!char.IsAsciiHexDigit(x)) || value.Manifest.CapturedAtUtc<w.ToUtc || value.Manifest.CapturedAtUtc>DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Cutoff manifest invalid.");
        if(value.Manifest.EvidenceProtocolVersion is not (1 or 2)
            || value.Manifest.EvidenceProtocolVersion==2 && (value.Manifest.SourceRevision is not {Epoch:var epoch,Revision:>=0} || epoch==Guid.Empty))
            throw new InvalidOperationException("Cutoff revision invalid.");
    }
    private Task<SourceCutoffRead<T>> Source<T>(string service,EndOfDayWindow w,Guid? operation,Guid? id,string token,CancellationToken ct)
    {
        string query=$"organizationId={w.OrganizationId:D}&restaurantId={w.RestaurantId:D}&branchId={w.BranchId:D}&fromUtc={Uri.EscapeDataString(w.FromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(w.ToUtc.ToString("O"))}";
        return Send<SourceCutoffRead<T>>(service,operation is null?HttpMethod.Get:HttpMethod.Post,
            $"api/{service.ToLowerInvariant()}/v1/customer/day-cutoffs"+(operation is null?$"/{id:D}?{query}":""),
            operation is null?null:new SourceCutoffCommand(operation.Value,w),token,ct);
    }
    private async Task<T> Send<T>(string service,HttpMethod method,string path,object? body,string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(method,path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(body is not null)request.Content=JsonContent.Create(body);
        using var response=await clients.CreateClient("DayCutoff"+service).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new UnauthorizedAccessException();
        response.EnsureSuccessStatusCode();return await BoundedJson.ReadAsync<T>(response,16*1024*1024,ct);
    }
}
