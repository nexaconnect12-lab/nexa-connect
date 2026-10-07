using NexaConnect.Contracts.Reporting;
using NexaConnect.Contracts.Platform;
namespace NexaConnect.Services.Reporting.Application;

public interface ISealedSources
{
    Task<SourceSealRead<OrderDaySummary>> OrderSealAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct);
    Task<SourceSealRead<PaymentDaySummary>> PaymentSealAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct);
}
public sealed class SealedDayReconciliation(ISealedSources sources,IReportingCustomerAuthorizer authorization,IFinancialCompletenessRepository repository)
{
    public async Task<SealedReconciliation> CheckAsync(SealedReconciliationCommand command,string bearer,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));ct=deadline.Token;
        var w=command.Window;
        if(w.OrganizationId==Guid.Empty || w.RestaurantId==Guid.Empty || w.BranchId==Guid.Empty || w.FromUtc==default
            || w.ToUtc<=w.FromUtc || w.ToUtc-w.FromUtc>TimeSpan.FromHours(27) || w.ToUtc>DateTimeOffset.UtcNow
            || command.OrderSealId==Guid.Empty || command.PaymentSealId==Guid.Empty)throw new ArgumentException();
        if(!await authorization.IsGrantedAsync(w.OrganizationId,w.BranchId,ProductPermissions.ReportingSalesRead,bearer,ct))throw new UnauthorizedAccessException();
        var order=await sources.OrderSealAsync(w,command.OrderSealId,bearer,ct);
        var payment=await sources.PaymentSealAsync(w,command.PaymentSealId,bearer,ct);
        Validate(order,w,command.OrderSealId);Validate(payment,w,command.PaymentSealId);
        // Reuse the exact financial inventory policy, translating immutable seals to source observations.
        // Subsequent financial changes are reported by the journal; they cannot rewrite this selected set.
        var adapter=new SealedAdapter(sources,order,payment);
        var check=await new DayCutoffReconciliation(adapter,authorization,repository).CheckAsync(new(w,order.Manifest.ManifestId,payment.Manifest.ManifestId),bearer,ct);
        return new(check.CheckId,w,command.OrderSealId,command.PaymentSealId,order.Manifest.ManifestId,payment.Manifest.ManifestId,
            check.DeliveryComplete,check.CheckedAtUtc,check.Sales,check.Payments,check.Refunds);
    }
    private static void Validate<T>(SourceSealRead<T> value,EndOfDayWindow w,Guid id)
    {
        if(value.Seal.SealId!=id || value.Seal.Window!=w || value.Manifest.Window!=w || value.Seal.ManifestId!=value.Manifest.ManifestId
            || value.Seal.SourceRevision!=value.Manifest.SourceRevision || value.Manifest.EvidenceProtocolVersion!=2 || value.PendingChanges<0)
            throw new InvalidOperationException("Sealed source identity invalid.");
    }
    private sealed class SealedAdapter(ISealedSources sources,SourceSealRead<OrderDaySummary> order,SourceSealRead<PaymentDaySummary> payment):ICutoffSources
    {
        public async Task<SourceCutoffRead<OrderDaySummary>> OrderAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)
        {
            if(id!=order.Manifest.ManifestId)throw new InvalidOperationException();
            var fresh=await sources.OrderSealAsync(w,order.Seal.SealId,bearer,ct);Validate(fresh,w,order.Seal.SealId);
            if(fresh.Seal!=order.Seal)throw new InvalidOperationException("Sealed source identity changed.");
            return new(fresh.Manifest,fresh.JournalComplete);
        }
        public async Task<SourceCutoffRead<PaymentDaySummary>> PaymentAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)
        {
            if(id!=payment.Manifest.ManifestId)throw new InvalidOperationException();
            var fresh=await sources.PaymentSealAsync(w,payment.Seal.SealId,bearer,ct);Validate(fresh,w,payment.Seal.SealId);
            if(fresh.Seal!=payment.Seal)throw new InvalidOperationException("Sealed source identity changed.");
            return new(fresh.Manifest,fresh.JournalComplete);
        }
    }
}
