using System.Net;
using System.Net.Http.Headers;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;

public sealed class HttpDaySealEvidenceReader(IHttpClientFactory clients,IDaySealStore store,
    IDayCloseEvidenceReader calendar,TimeProvider clock):IDaySealEvidenceReader
{
    public async Task<DayEvidence> ReadAsync(DayIdentity day,string token,CancellationToken ct)
    {
        var state=await store.ReadAsync(day,ct)??throw new InvalidOperationException("Seal coordination missing.");
        var basis=state.Snapshot??throw new InvalidOperationException("Reviewed cutoff missing.");
        var cutoff=basis.Cutoff??throw new InvalidOperationException("Reviewed cutoff missing.");
        var context=await calendar.ReadAsync(day,token,ct);
        if(context.Currency!="THB" || context.TimeZone!=basis.TimeZone || context.FromUtc!=basis.FromUtc || context.ToUtc!=basis.ToUtc)
            throw new InvalidOperationException("Reviewed calendar changed.");
        var w=new EndOfDayWindow(day.OrganizationId,day.RestaurantId,day.BranchId,basis.FromUtc,basis.ToUtc);
        var operation=state.Status=="preparing"?state.OperationId:null;
        var retained=state.Snapshot.Seals;
        if(operation is null && retained is null)throw new InvalidOperationException("Source seals missing.");
        var order=await Source<OrderDaySummary>("Order",w,operation,cutoff.Order,retained?.Order.SealId,token,ct);
        var payment=await Source<PaymentDaySummary>("Payment",w,operation,cutoff.Payment,retained?.Payment.SealId,token,ct);
        var pos=await Source<PosDaySummary>("POS",w,operation,cutoff.Pos,retained?.Pos.SealId,token,ct);
        Validate(order,w,cutoff.Order);Validate(payment,w,cutoff.Payment);Validate(pos,w,cutoff.Pos);
        var check=await Send<SealedReconciliation>("Reporting",HttpMethod.Post,"api/reporting/v1/customer/day-seal-reconciliation",
            new SealedReconciliationCommand(w,order.Seal.SealId,payment.Seal.SealId),token,ct);
        if(check.Window!=w || check.OrderSealId!=order.Seal.SealId || check.PaymentSealId!=payment.Seal.SealId
            || check.OrderManifestId!=cutoff.Order.ManifestId || check.PaymentManifestId!=cutoff.Payment.ManifestId
            || check.CheckId==Guid.Empty || check.CheckedAtUtc<w.ToUtc || check.CheckedAtUtc>clock.GetUtcNow())
            throw new InvalidOperationException("Sealed reconciliation invalid.");
        long gaps=0;
        if(check.Sales.Expected!=order.Manifest.Sales.Count || check.Payments.Expected!=order.Manifest.Sales.Count || check.Refunds.Expected!=payment.Manifest.Refunds.Count)
            throw new InvalidOperationException("Sealed inventories invalid.");
        foreach(var count in new[]{check.Sales,check.Payments,check.Refunds})
        {
            if(count is null || new[]{count.Expected,count.Matched,count.Missing,count.Conflicting,count.Unexpected}.Any(n=>n<0||n>20000)
                || count.Expected!=count.Matched+count.Missing+count.Conflicting)throw new InvalidOperationException("Sealed counts invalid.");
            gaps+=count.Missing+count.Conflicting+count.Unexpected;
        }
        if(check.DeliveryComplete && gaps!=0)throw new InvalidOperationException("Sealed delivery invalid.");
        // Observe journals again after Reporting. No source lock spans these HTTP calls.
        var orderAfter=await Source<OrderDaySummary>("Order",w,null,cutoff.Order,order.Seal.SealId,token,ct);
        var paymentAfter=await Source<PaymentDaySummary>("Payment",w,null,cutoff.Payment,payment.Seal.SealId,token,ct);
        var posAfter=await Source<PosDaySummary>("POS",w,null,cutoff.Pos,pos.Seal.SealId,token,ct);
        Validate(orderAfter,w,cutoff.Order);Validate(paymentAfter,w,cutoff.Payment);Validate(posAfter,w,cutoff.Pos);
        if(orderAfter.CurrentSummary!.Window!=w || paymentAfter.CurrentSummary!.Window!=w || posAfter.CurrentSummary!.Window!=w
            || new[]{orderAfter.CurrentSummary.EvidenceVersion,paymentAfter.CurrentSummary.EvidenceVersion,posAfter.CurrentSummary.EvidenceVersion}.Any(v=>v is null || v.Length!=64 || v.Any(c=>!char.IsAsciiHexDigit(c)))
            || orderAfter.CurrentSummary.GrossSales<0 || paymentAfter.CurrentSummary.CompletedRefunds<0
            || orderAfter.CurrentSummary.Tenders.Any(t=>t.Currency!="THB" || t.Amount<0)
            || orderAfter.CurrentSummary.Currencies.Concat(paymentAfter.CurrentSummary.Currencies).Concat(posAfter.CurrentSummary.Currencies).Any(c=>c!="THB"))
            throw new InvalidOperationException("Current source comparison invalid.");
        if(orderAfter.Seal!=order.Seal || paymentAfter.Seal!=payment.Seal || posAfter.Seal!=pos.Seal
            || orderAfter.PendingChanges<order.PendingChanges || paymentAfter.PendingChanges<payment.PendingChanges || posAfter.PendingChanges<pos.PendingChanges
            || orderAfter.ObservedChanges<order.ObservedChanges || paymentAfter.ObservedChanges<payment.ObservedChanges || posAfter.ObservedChanges<pos.ObservedChanges)
            throw new InvalidOperationException("Source seal identity changed.");
        static DaySealReference Ref(SourceDaySeal s)=>new(s.SealId,s.ManifestId,s.SourceRevision.Epoch,s.SourceRevision.Revision);
        long pending=checked(orderAfter.PendingChanges+paymentAfter.PendingChanges+posAfter.PendingChanges);
        static IEnumerable<DaySealRecordChange> Details(string source,IReadOnlyList<SourceSealChange> changes)=>changes.Select(c=>
            new DaySealRecordChange(source,c.Revision,c.Attribution,c.RecordKind,c.RecordId,c.ParentId,c.BeforeStatus,c.AfterStatus,
                c.BeforeFinancialVersion,c.AfterFinancialVersion,c.BeforeFinancialAtUtc,c.AfterFinancialAtUtc));
        var details=Details("Order",orderAfter.Changes).Concat(Details("Payment",paymentAfter.Changes)).Concat(Details("POS",posAfter.Changes)).ToArray();
        return basis with{ObservedAtUtc=clock.GetUtcNow(),Cutoff=cutoff with{CheckId=check.CheckId,DeliveryComplete=check.DeliveryComplete,FinancialGaps=checked((int)gaps)},
            Seals=new(Ref(order.Seal),Ref(payment.Seal),Ref(pos.Seal),pending,
                order.JournalComplete&&payment.JournalComplete&&pos.JournalComplete&&orderAfter.JournalComplete&&paymentAfter.JournalComplete&&posAfter.JournalComplete
                && orderAfter.UnknownChanges==0&&paymentAfter.UnknownChanges==0&&posAfter.UnknownChanges==0,check.DeliveryComplete),
            SealComparison=new(orderAfter.CurrentSummary!.GrossSales,paymentAfter.CurrentSummary!.CompletedRefunds,
                orderAfter.CurrentSummary.GrossSales-paymentAfter.CurrentSummary.CompletedRefunds,posAfter.CurrentSummary!.CashVariance,
                orderAfter.CurrentSummary.Tenders.Select(t=>new DayTender(t.Method,t.Currency,t.Amount)).ToArray(),
                checked(orderAfter.UnknownChanges+paymentAfter.UnknownChanges+posAfter.UnknownChanges),clock.GetUtcNow(),details.Take(256).ToArray(),
                details.Length>256||orderAfter.ChangesTruncated||paymentAfter.ChangesTruncated||posAfter.ChangesTruncated)};
    }
    private void Validate<T>(SourceSealRead<T> s,EndOfDayWindow w,CutoffReference reviewed)
    {
        if(s.Seal.SealId==Guid.Empty || s.Seal.OperationId==Guid.Empty || s.Seal.Window!=w || s.Manifest.Window!=w
            || s.Seal.ManifestId!=reviewed.ManifestId || s.Manifest.ManifestId!=reviewed.ManifestId
            || s.Manifest.Generation!=reviewed.Generation || s.Manifest.EvidenceVersion!=reviewed.EvidenceVersion
            || s.Seal.SourceRevision!=s.Manifest.SourceRevision || s.Seal.SourceRevision.Epoch!=reviewed.RevisionEpoch
            || s.Seal.SourceRevision.Revision!=reviewed.SourceRevision || s.PendingChanges<0 || s.Manifest.EvidenceProtocolVersion!=2
            || s.AttributionProtocolVersion!=2 || s.UnknownChanges<0 || s.UnknownChanges>s.PendingChanges
            || s.Changes is null || s.Changes.Count>256 || s.Changes.Count>s.PendingChanges
            || s.ObservedChanges<s.PendingChanges || s.CurrentSummary is null
            || s.Seal.SealedAtUtc<s.Manifest.CapturedAtUtc || s.Seal.SealedAtUtc>clock.GetUtcNow())
            throw new InvalidOperationException("Source seal invalid.");
    }
    private Task<SourceSealRead<T>> Source<T>(string owner,EndOfDayWindow w,Guid? operation,CutoffReference reviewed,Guid? id,string token,CancellationToken ct)
    {
        string query=$"organizationId={w.OrganizationId:D}&restaurantId={w.RestaurantId:D}&branchId={w.BranchId:D}&fromUtc={Uri.EscapeDataString(w.FromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(w.ToUtc.ToString("O"))}";
        string path=$"api/{owner.ToLowerInvariant()}/v1/customer/day-cutoffs/seals";
        return Send<SourceSealRead<T>>(owner,operation is null?HttpMethod.Get:HttpMethod.Post,
            operation is null?$"{path}/{id:D}?{query}":path,
            operation is null?null:new SourceSealCommand(operation.Value,w,reviewed.ManifestId,new(reviewed.RevisionEpoch??Guid.Empty,reviewed.SourceRevision??-1)),token,ct);
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
