using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;

namespace NexaConnect.UnitTests;

public sealed class RefundFinancialReportingTests
{
    [Fact]
    public async Task Valid_refund_is_normalized_and_projected()
    {
        var repository = new Repository();
        var service = new RefundFinancialReporting(repository);
        PaymentRefundedV1 value = Event() with { Currency = "thb", ReasonCode = " CUSTOMER_REQUEST " };

        Assert.True(await service.ProjectAsync(value, default));
        Assert.Equal("THB", repository.Fact!.Currency);
        Assert.Equal("customer_request", repository.Fact.ReasonCode);
        Assert.Equal(value.RefundId, repository.Fact.RefundId);
    }

    [Theory]
    [InlineData(0, 25, 100)]
    [InlineData(25, 20, 100)]
    [InlineData(25, 101, 100)]
    public void Invalid_financial_totals_fail_before_persistence(decimal amount, decimal cumulative, decimal captured)
    {
        PaymentRefundedV1 value = Event() with
        {
            Amount = amount,
            CumulativeRefundedAmount = cumulative,
            CapturedAmount = captured
        };
        Assert.Throws<ArgumentException>(() => RefundFinancialReporting.Translate(value));
    }

    private static PaymentRefundedV1 Event() => new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        25m, "THB", "customer_request", 25m, 100m, $"RF-{Guid.NewGuid():N}".ToUpperInvariant());

    [Fact]
    public void Missing_json_fields_are_permanent_validation_failures()
    {
        Assert.Throws<ArgumentException>(() => RefundFinancialReporting.Translate(Event() with { Currency = null! }));
        Assert.Throws<ArgumentException>(() => RefundFinancialReporting.Translate(Event() with { ReasonCode = null! }));
        Assert.Throws<ArgumentException>(() => RefundFinancialReporting.Translate(Event() with { ReceiptNumber = null! }));
        Assert.Throws<ArgumentException>(() => RefundFinancialReporting.Translate(Event() with { CapturedAmount = decimal.MaxValue }));
    }

    private sealed class Repository : IRefundFinancialFactRepository
    {
        public RefundFinancialFact? Fact { get; private set; }
        public Task<bool> ProjectAsync(RefundFinancialFact fact, CancellationToken cancellationToken)
        {
            Fact = fact;
            return Task.FromResult(true);
        }
    }
}
