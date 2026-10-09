using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;

namespace NexaConnect.UnitTests;

public sealed class CashCorrectionReportingTests
{
    private static PosLateCashCorrectionPostedV1 Event()
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-1);
        return new(Guid.NewGuid(), Guid.NewGuid(), start.AddHours(1), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            DateOnly.FromDateTime(start.DateTime), start, start.AddDays(1), "THB", -10);
    }
    [Fact]
    public void Projection_preserves_financial_provenance_and_normalizes_instant_and_decimal_representation()
    {
        var value = Event(); var fact = CashCorrectionReporting.Translate(value);
        Assert.Equal(value.CorrectionId, fact.CorrectionId); Assert.Equal(value.OriginalSettlementId, fact.OriginalSettlementId);
        Assert.Equal(fact, CashCorrectionReporting.Translate(value with { OccurredAtUtc = value.OccurredAtUtc.ToOffset(TimeSpan.FromHours(7)), CashVarianceAdjustment = -10.0000m }));
        Assert.Equal(FinancialCompleteness.Hash(fact), FinancialCompleteness.Hash(CashCorrectionReporting.Translate(value with { CashVarianceAdjustment = -10.0000m })));
    }
    [Theory]
    [InlineData(0)][InlineData(10)][InlineData(-0.00001)]
    public void Invalid_adjustments_are_rejected(decimal amount) => Assert.Throws<ArgumentException>(() => CashCorrectionReporting.Translate(Event() with { CashVarianceAdjustment = amount }));
    [Theory]
    [InlineData("event")][InlineData("correlation")][InlineData("scope")][InlineData("version")][InlineData("window")][InlineData("currency")]
    public void Invalid_identity_scope_and_posting_boundaries_are_rejected(string kind)
    {
        var e = Event(); e = kind switch { "event" => e with { EventId = Guid.Empty }, "correlation" => e with { CorrelationId = Guid.Empty },
            "scope" => e with { OrganizationId = Guid.Empty }, "version" => e with { ReviewedVersion = 0 },
            "window" => e with { OccurredAtUtc = e.PostingToUtc }, _ => e with { Currency = "USD" } };
        Assert.Throws<ArgumentException>(() => CashCorrectionReporting.Translate(e));
    }
    [Theory]
    [InlineData("branch")][InlineData("missing-scope")][InlineData("duplicate")][InlineData("outside")]
    public void Invalid_source_manifests_fail_closed(string kind)
    {
        var e = Event(); var range = new ReportingRange(e.OrganizationId, e.BranchId, e.PostingFromUtc, e.PostingToUtc);
        var manifest = new CashCorrectionManifest(e.OrganizationId, e.RestaurantId, e.BranchId, range.FromUtc, range.ToUtc, DateTimeOffset.UtcNow, [e]);
        manifest = kind switch { "branch" => manifest with { BranchId = Guid.NewGuid() }, "missing-scope" => manifest with { RestaurantId = Guid.Empty },
            "duplicate" => manifest with { Events = [e, e] }, _ => manifest with { Events = [e with { OccurredAtUtc = range.ToUtc }] } };
        Assert.Throws<ArgumentException>(() => CashCorrectionReporting.ValidateManifest(manifest, range));
    }
    [Fact]
    public async Task Denied_report_cannot_contact_source_or_read_financial_projection()
    {
        var ports = new Ports(); var app = new CashCorrectionReporting(ports, ports, ports); var e = Event();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => app.ReadAsync(new(e.OrganizationId, e.BranchId, e.PostingFromUtc, e.PostingToUtc), "Bearer token", default));
        Assert.Equal(0, ports.SourceReads); Assert.Equal(0, ports.Comparisons);
    }
    private sealed class Ports : ICashCorrectionFactRepository, ICashCorrectionSource, IReportingCustomerAuthorizer
    {
        public int SourceReads, Comparisons;
        public Task<bool> IsGrantedAsync(Guid org, Guid? branch, string permission, string authorization, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> ProjectAsync(CashCorrectionFact fact, CancellationToken ct) => throw new NotImplementedException();
        public Task<CashCorrectionManifest> ReadAsync(ReportingRange range, string authorization, CancellationToken ct) { SourceReads++; throw new NotImplementedException(); }
        public Task<CashCorrectionReport> CompareAsync(CashCorrectionManifest manifest, CancellationToken ct) { Comparisons++; throw new NotImplementedException(); }
    }
}
