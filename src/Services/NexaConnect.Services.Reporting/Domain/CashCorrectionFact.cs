namespace NexaConnect.Services.Reporting.Domain;

public sealed record CashCorrectionFact(Guid EventId, Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    Guid CorrectionId, Guid WorkId, Guid OriginalSettlementId, Guid OrderId, Guid TenderId, Guid DrawerId,
    long ReviewedVersion, DateOnly PostingDate, DateTimeOffset PostingFromUtc, DateTimeOffset PostingToUtc,
    DateTimeOffset PostedAtUtc, string Currency, decimal Adjustment)
{
    public void Validate()
    {
        if (new[] { EventId, OrganizationId, RestaurantId, BranchId, CorrectionId, WorkId, OriginalSettlementId,
                OrderId, TenderId, DrawerId }.Any(x => x == Guid.Empty) || ReviewedVersion <= 0
            || PostingDate == default || PostingFromUtc == default || PostedAtUtc == default
            || PostingToUtc <= PostingFromUtc || PostingToUtc - PostingFromUtc > TimeSpan.FromHours(26)
            || PostingToUtc - PostingFromUtc < TimeSpan.FromHours(22)
            || PostedAtUtc < PostingFromUtc || PostedAtUtc >= PostingToUtc
            || Currency != "THB" || Adjustment >= 0 || Adjustment < -999999999999999.9999m
            || decimal.Round(Adjustment, 4) != Adjustment)
            throw new ArgumentException("Invalid cash correction financial evidence.");
    }
}
