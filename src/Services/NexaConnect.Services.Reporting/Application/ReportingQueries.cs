namespace NexaConnect.Services.Reporting.Application;

public sealed record ReportingRange(Guid OrganizationId, Guid BranchId, DateTimeOffset FromUtc, DateTimeOffset ToUtc);
public sealed record DashboardSummary(int CompletedOrders, decimal GrossSales, decimal NetPaid, decimal Refunded, string? Currency, DateTimeOffset? LatestGlobalCheckpointUpdatedAtUtc);
public sealed record SalesReportRow(Guid OrderId, Guid BranchId, string Channel, string ServiceType, string Currency, decimal SubtotalAmount, decimal DiscountAmount, decimal ServiceChargeAmount, decimal TaxAmount, decimal TotalAmount, string OrderStatus, DateTimeOffset OrderedAtUtc, DateTimeOffset? CompletedAtUtc);
public sealed record SalesReport(ReportingRange Range, IReadOnlyCollection<SalesReportRow> Items, decimal TotalSales, string? Currency, DateTimeOffset? LatestGlobalCheckpointUpdatedAtUtc);

public interface IReportingReadRepository
{
    Task<DashboardSummary> DashboardAsync(ReportingRange range, CancellationToken cancellationToken);
    Task<SalesReport> SalesAsync(ReportingRange range, CancellationToken cancellationToken);
}

public interface IReportingCustomerAuthorizer
{
    Task<bool> IsGrantedAsync(Guid organizationId, Guid? branchId, string permission, string authorizationHeader, CancellationToken cancellationToken);
}

public sealed class ReportingQueries(IReportingReadRepository repository)
{
    public Task<DashboardSummary> DashboardAsync(Guid organizationId, Guid? branchId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken) => repository.DashboardAsync(Range(organizationId, branchId, fromUtc, toUtc), cancellationToken);
    public Task<SalesReport> SalesAsync(Guid organizationId, Guid? branchId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken) => repository.SalesAsync(Range(organizationId, branchId, fromUtc, toUtc), cancellationToken);

    private static ReportingRange Range(Guid organizationId, Guid? branchId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        if (organizationId == Guid.Empty || branchId is null || branchId == Guid.Empty) throw new ArgumentException("Organization and branch identifiers are required.");
        DateTimeOffset end = toUtc ?? DateTimeOffset.UtcNow;
        DateTimeOffset start = fromUtc ?? end.AddDays(-30);
        if (start >= end || end - start > TimeSpan.FromDays(366)) throw new ArgumentException("Reporting range must be positive and no longer than 366 days.");
        return new(organizationId, branchId.Value, start, end);
    }
}
