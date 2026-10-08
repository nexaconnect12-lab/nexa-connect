namespace NexaConnect.Services.POS.Domain;

/// <summary>Source-owned financial selection state and exact before/after window policy.</summary>
public sealed record FinancialRecord(Guid Id,string Status,long Version,DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FinancialAtUtc=null,bool? HasVariance=null,string? ReviewStatus=null,long? ReviewedVersion=null);
public sealed record FinancialImpact(bool? AffectsWindow,string Reason);

public static class FinancialChange
{
    public static FinancialImpact Classify(DateTimeOffset from,DateTimeOffset to,FinancialRecord? before,FinancialRecord? after,bool uncertain,string kind)
    {
        if(uncertain || before is not null && after is not null && before.Id!=after.Id)return new(null,"ownership_uncertain");
        if(kind is not ("stores" or "shifts" or "cash_sessions" or "cash_movements" or "cash_session_review_states"))return new(null,"unknown");
        if(from==default || to<=from || before is null && after is null)return new(null,"unknown");
        if(kind=="stores")return before is not null && after is not null && before.Id==after.Id && before.Id!=Guid.Empty
            ?new(false,"unrelated"):new(null,"ownership_uncertain");
        bool shift=kind=="shifts";
        foreach(var state in new[]{before,after}.OfType<FinancialRecord>())
            if(!Valid(state,shift))return new(null,"unknown");
        foreach(var state in new[]{before,after}.OfType<FinancialRecord>())
        {
            if(state.CreatedAtUtc>=to)continue;
            if(shift)
            {
                if(state.Status is "open" or "closing")return new(true,"unresolved_shift");
            }
            else
            {
                if(state.Status is "open" or "counting")return new(true,"unresolved_drawer");
                if(state.FinancialAtUtc is null || state.FinancialAtUtc==default(DateTimeOffset) || state.HasVariance is null)return new(null,"missing_financial_history");
                if(state.FinancialAtUtc>=from && state.FinancialAtUtc<to)return new(true,"cash_close_date");
                if(state.FinancialAtUtc<to && state.HasVariance==true && (state.ReviewStatus!="approved" || state.ReviewedVersion!=state.Version))return new(true,"cash_review");
            }
        }
        return new(false,"unrelated");
    }
    private static bool Valid(FinancialRecord state,bool shift)=>state.Id!=Guid.Empty && state.Version>0 && state.CreatedAtUtc!=default
        && (shift?state.Status is "open" or "closing" or "closed" or "cancelled":state.Status is "open" or "counting" or "closed")
        && (state.ReviewStatus is null or "approved" or "investigating" or "review_required");

    public static bool? AffectsWindow(DateTimeOffset toUtc, DateTimeOffset[]? before, DateTimeOffset[]? after)
    {
        if (toUtc == default || before is null && after is null
            || before is { Length: 0 } || after is { Length: 0 }
            || (before ?? []).Concat(after ?? []).Any(t => t == default)) return null;
        return (before ?? []).Concat(after ?? []).Any(t => t < toUtc);
    }
}
