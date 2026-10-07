namespace NexaConnect.Services.Payment.Domain;

/// <summary>Historical or uncertain changes require review. Only proven later-day changes are excluded.</summary>
public static class FinancialChange
{
    public static bool? AffectsWindow(DateTimeOffset toUtc, DateTimeOffset[]? before, DateTimeOffset[]? after)
    {
        if (toUtc == default || before is null && after is null
            || before is { Length: 0 } || after is { Length: 0 }
            || (before ?? []).Concat(after ?? []).Any(t => t == default)) return null;
        return (before ?? []).Concat(after ?? []).Any(t => t < toUtc);
    }
}
