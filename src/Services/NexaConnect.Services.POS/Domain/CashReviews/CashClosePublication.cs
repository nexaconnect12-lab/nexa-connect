namespace NexaConnect.Services.POS.Domain.CashReviews;

public static class CashClosePublication
{
    public static string ReviewStatus(decimal variance, long financialVersion, long? reviewedVersion, string? status)
    {
        if (financialVersion < 1) throw new ArgumentException("Invalid financial version.");
        if (variance == 0) return "balanced";
        if (reviewedVersion != financialVersion) return "review_required";
        return status is "investigating" or "approved" ? status : throw new InvalidOperationException("Invalid review state.");
    }
}
