namespace NexaConnect.Services.Payment.Domain;

/// <summary>Source-owned financial selection state and exact before/after window policy.</summary>
public sealed record FinancialRecord(Guid Id,string Status,long Version,DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FinancialAtUtc=null,bool HasReceipt=false);
public sealed record FinancialImpact(bool? AffectsWindow,string Reason);

public static class FinancialChange
{
    public static FinancialImpact Classify(DateTimeOffset from,DateTimeOffset to,FinancialRecord? before,FinancialRecord? after,bool uncertain,string kind)
    {
        if(uncertain || before is not null && after is not null && before.Id!=after.Id)return new(null,"ownership_uncertain");
        if(kind is not ("payment_intents" or "refunds" or "refund_financial_publications"))return new(null,"unknown");
        if(from==default || to<=from || before is null && after is null)return new(null,"unknown");
        bool intent=kind=="payment_intents";
        foreach(var state in new[]{before,after}.OfType<FinancialRecord>())
            if(!Valid(state,intent))return new(null,"unknown");
        foreach(var state in new[]{before,after}.OfType<FinancialRecord>())
        {
            if(intent)
            {
                if(state.CreatedAtUtc<to && state.Status is not ("captured" or "failed" or "cancelled" or "expired" or "voided"))return new(true,"unresolved_payment");
            }
            else
            {
                if(state.CreatedAtUtc<to && state.Status is not ("completed" or "failed"))return new(true,"unresolved_refund");
                if(state.Status=="completed")
                {
                    if(state.FinancialAtUtc is null || state.FinancialAtUtc==default(DateTimeOffset) || !state.HasReceipt)return new(null,"missing_financial_history");
                    if(state.FinancialAtUtc>=from && state.FinancialAtUtc<to)return new(true,"refund_date");
                }
            }
        }
        return new(false,"unrelated");
    }
    private static bool Valid(FinancialRecord state,bool intent)=>state.Id!=Guid.Empty && state.Version>0 && state.CreatedAtUtc!=default
        && (intent ? state.Status is "pending" or "authorizing" or "unknown" or "requires_action" or "authorized" or "capturing" or "capture_unknown" or "captured" or "failed" or "cancelled" or "expired" or "voiding" or "void_unknown" or "voided" or "void_failed"
        : state.Status is "processing" or "refund_unknown" or "review_required" or "completed" or "failed");

    public static bool? AffectsWindow(DateTimeOffset toUtc, DateTimeOffset[]? before, DateTimeOffset[]? after)
    {
        if (toUtc == default || before is null && after is null
            || before is { Length: 0 } || after is { Length: 0 }
            || (before ?? []).Concat(after ?? []).Any(t => t == default)) return null;
        return (before ?? []).Concat(after ?? []).Any(t => t < toUtc);
    }
}
