using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.UnitTests;

public sealed class ReportingQueriesTests
{
    [Fact]
    public async Task Default_range_is_thirty_days_and_tenant_is_forwarded()
    {
        var repository = new Repository(); var queries = new ReportingQueries(repository); Guid organizationId = Guid.NewGuid();
        Guid branchId=Guid.NewGuid(); await queries.DashboardAsync(organizationId, branchId, null, null, default);
        Assert.Equal(organizationId, repository.Range!.OrganizationId); Assert.InRange(repository.Range.ToUtc - repository.Range.FromUtc, TimeSpan.FromDays(29.99), TimeSpan.FromDays(30.01));
    }

    [Fact]
    public async Task Range_rejects_more_than_366_days()
    {
        var queries = new ReportingQueries(new Repository()); DateTimeOffset end = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<ArgumentException>(() => queries.SalesAsync(Guid.NewGuid(), Guid.NewGuid(), end.AddDays(-367), end, default));
    }

    private sealed class Repository : IReportingReadRepository
    {
        public ReportingRange? Range;
        public Task<DashboardSummary> DashboardAsync(ReportingRange range, CancellationToken c) { Range = range; return Task.FromResult(new DashboardSummary(0, 0, 0, 0, null, null)); }
        public Task<SalesReport> SalesAsync(ReportingRange range, CancellationToken c) { Range = range; return Task.FromResult(new SalesReport(range, [], 0, null, null)); }
    }
}
