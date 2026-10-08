namespace NexaConnect.Services.Order.Domain;

/// <summary>Source-owned financial selection state and exact before/after window policy.</summary>
public sealed record FinancialRecord(Guid Id,string Status,long Version,DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FinancialAtUtc=null,bool HasReceipt=false);
public sealed record FinancialImpact(bool? AffectsWindow,string Reason);

public static class FinancialChange
{
    public static FinancialImpact Classify(DateTimeOffset from,DateTimeOffset to,FinancialRecord? before,FinancialRecord? after,bool uncertain,string kind)
    {
        if(uncertain || before is not null && after is not null && before.Id!=after.Id)return new(null,"ownership_uncertain");
        if(kind is not ("orders" or "order_manual_tender_settlements" or "order_sale_publications"))return new(null,"unknown");
        if(from==default || to<=from || before is null && after is null)return new(null,"unknown");
        foreach(var state in new[]{before,after}.OfType<FinancialRecord>())
            if(!Valid(state))return new(null,"unknown");
        foreach(var state in new[]{before,after}.OfType<FinancialRecord>())
        {
            if(state.Status is not ("completed" or "cancelled") && state.CreatedAtUtc<to)return new(true,"unresolved_order");
            if(state.Status!="completed")continue;
            if(!state.HasReceipt || state.FinancialAtUtc is null || state.FinancialAtUtc==default(DateTimeOffset))return new(null,"missing_financial_history");
            if(state.CreatedAtUtc>=from && state.CreatedAtUtc<to)return new(true,"sales_date");
            if(state.FinancialAtUtc>=from && state.FinancialAtUtc<to)return new(true,"tender_date");
        }
        return new(false,"unrelated");
    }
    private static bool Valid(FinancialRecord state)=>state.Id!=Guid.Empty && state.Version>0 && state.CreatedAtUtc!=default
        && state.Status is "draft" or "submitted" or "accepted" or "inventory_reserved" or "payment_pending" or "payment_review" or "preparing" or "ready" or "completed" or "cancelled" or "kitchen_accepted" or "cancellation_pending" or "cancellation_review";

    public static bool? AffectsWindow(DateTimeOffset toUtc, DateTimeOffset[]? before, DateTimeOffset[]? after)
    {
        if (toUtc == default || before is null && after is null
            || before is { Length: 0 } || after is { Length: 0 }
            || (before ?? []).Concat(after ?? []).Any(t => t == default)) return null;
        return (before ?? []).Concat(after ?? []).Any(t => t < toUtc);
    }
}
