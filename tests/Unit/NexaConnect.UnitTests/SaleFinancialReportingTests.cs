using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.UnitTests;

public sealed class SaleFinancialReportingTests
{
    [Theory]
    [InlineData("card_omise_test", "payment_intent")]
    [InlineData("cash", "manual_settlement")]
    [InlineData("promptpay_manual", "manual_settlement")]
    public void Receipt_translates_original_evidence_with_stable_identity(string method, string origin)
    {
        var paidAt = DateTimeOffset.UtcNow;
        var receipt = new PaidOrderReceipt(1, "R-TEST", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "order", paidAt, "THB", method, [], null, 100m, 10m, 7.7m, 117.7m);
        Guid payment = Guid.NewGuid();
        var value = SaleCompletionEvents.Create(receipt, paidAt.AddMinutes(-1), payment, payment, "pos", "takeaway", null);
        var fact = SaleFinancialReporting.Translate(value);
        Assert.Equal(origin, fact.PaymentOrigin);
        Assert.Equal(payment, fact.PaymentId);
        Assert.Equal(117.7m, fact.TotalAmount);
        Assert.Equal(receipt.PaidAtUtc, fact.PaidAtUtc);
        Assert.Equal(value.EventId, SaleCompletionEvents.Create(receipt, paidAt.AddMinutes(-1), payment, payment, "pos", "takeaway", null).EventId);
        Assert.Throws<InvalidOperationException>(() => SaleCompletionEvents.Create(receipt, paidAt, null, null, "pos", "takeaway", null));
    }

    [Fact]
    public void Invalid_scope_tender_precision_and_bill_are_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var value = new OrderSaleCompletedV1(Guid.NewGuid(), Guid.NewGuid(), now, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "payment_intent", "card_omise_test", "THB", "pos", "takeaway",
            now.AddMinutes(-1), now, "R-TEST", 100, 10, 7.7m, 117.7m);
        Assert.Throws<ArgumentException>(() => SaleFinancialReporting.Translate(value with { BranchId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => SaleFinancialReporting.Translate(value with { Method = "cash" }));
        Assert.Throws<ArgumentException>(() => SaleFinancialReporting.Translate(value with { TotalAmount = 100 }));
        Assert.Throws<ArgumentException>(() => SaleFinancialReporting.Translate(value with { TaxAmount = 7.70001m, TotalAmount = 117.70001m }));
        Assert.Throws<ArgumentException>(() => SaleFinancialReporting.Translate(value with { Currency = "thb" }));
        Assert.Throws<ArgumentException>(() => SaleFinancialReporting.Translate(value with { OccurredAtUtc = now.AddMinutes(1) }));
    }
}
