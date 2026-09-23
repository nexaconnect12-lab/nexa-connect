namespace NexaConnect.Services.Reporting.Domain;

public sealed record CashCloseSnapshot(Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid StoreId,
    Guid SessionId, Guid ShiftId, DateTimeOffset ClosedAtUtc, string Currency,
    decimal ExpectedAmount, decimal CountedAmount, decimal VarianceAmount,
    long SnapshotVersion, long FinancialVersion, long ReviewVersion, string ReviewStatus, DateTimeOffset CapturedAtUtc)
{
    public void Validate()
    {
        if (new[] { OrganizationId, RestaurantId, BranchId, StoreId, SessionId, ShiftId }.Any(x => x == Guid.Empty) ||
            SnapshotVersion < 1 || FinancialVersion < 1 || ReviewVersion < 0 || ClosedAtUtc == default || CapturedAtUtc < ClosedAtUtc ||
            Currency is null || Currency.Length != 3 || !Currency.All(c => c is >= 'A' and <= 'Z') ||
            CountedAmount < 0 ||
            new[] { ExpectedAmount, CountedAmount, VarianceAmount }.Any(x => decimal.Round(x, 4) != x || Math.Abs(x) >= 1_000_000_000_000_000m) ||
            VarianceAmount != CountedAmount - ExpectedAmount ||
            ReviewStatus is not ("balanced" or "review_required" or "investigating" or "approved") ||
            (VarianceAmount == 0) != (ReviewStatus == "balanced") ||
            (ReviewStatus is "approved" or "investigating" && ReviewVersion == 0))
            throw new ArgumentException("Invalid cash-close snapshot.");
    }

    public bool ShouldReplace(CashCloseSnapshot current)
    {
        if (OrganizationId != current.OrganizationId || RestaurantId != current.RestaurantId || BranchId != current.BranchId ||
            StoreId != current.StoreId || SessionId != current.SessionId || ShiftId != current.ShiftId || Currency != current.Currency || ClosedAtUtc != current.ClosedAtUtc)
            throw new ArgumentException("Cash-close snapshot ownership conflict.");
        if (SnapshotVersion == current.SnapshotVersion && this != current)
            throw new ArgumentException("Cash-close snapshot version conflict.");
        if (SnapshotVersion > current.SnapshotVersion && (FinancialVersion < current.FinancialVersion || ReviewVersion < current.ReviewVersion))
            throw new ArgumentException("Cash-close source version regression.");
        return SnapshotVersion > current.SnapshotVersion;
    }
    public override string ToString() => "CashCloseSnapshot [redacted]";
}
