namespace NexaConnect.Services.POS.Domain.CashReviews;

public sealed record CashReviewDecision
{
    private CashReviewDecision(string code, string reason)
    {
        Code = code;
        Reason = reason;
    }

    public string Code { get; }
    public string Reason { get; }

    public static CashReviewDecision Create(string decision, string reason)
    {
        string normalizedDecision = decision?.Trim().ToLowerInvariant() ?? "";
        string normalizedReason = reason?.Trim() ?? "";
        if (normalizedDecision is not ("approve" or "investigate"))
            throw new ArgumentException("Decision must be approve or investigate.");
        if (normalizedReason.Length is < 1 or > 200)
            throw new ArgumentException("A review reason from 1 to 200 characters is required.");
        return new CashReviewDecision(normalizedDecision, normalizedReason);
    }
}
