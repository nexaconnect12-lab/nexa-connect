using NexaConnect.Services.Order.Domain;
using System.Text.Json;

namespace NexaConnect.UnitTests;
public sealed class PaidOrderReceiptTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Receipt_freezes_accepted_bill_and_repeated_issuance_preserves_identity_and_time(bool inclusive)
    {
        OrderLine[] lines = [new(Guid.NewGuid(), "Rice", 107m, 2, "kitchen")];
        var pricing = OrderPricing.Calculate(lines, "THB", new(1, 7, inclusive, 10));
        var order = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), lines, "THB",
            pricing: pricing, pricingFingerprint: new string('A',64));
        Assert.Throws<InvalidOperationException>(() => order.IssueReceipt(DateTimeOffset.UtcNow, "cash"));
        order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted();
        Assert.Null(order.Receipt);
        order.MarkPaid();
        var time = DateTimeOffset.UtcNow;
        order.IssueReceipt(time, "cash_manual");
        var receipt = order.Receipt!;
        order.IssueReceipt(time.AddDays(1), "another-tender");
        lines[0] = lines[0] with { Name = "Changed", UnitPrice = 999 };
        Assert.Same(receipt, order.Receipt);
        Assert.Equal(time, receipt.PaidAtUtc);
        Assert.Equal("cash", receipt.Tender);
        Assert.Equal("Rice", receipt.Lines.Single().Name);
        Assert.Equal(pricing.TotalAmount, receipt.TotalAmount);
        Assert.Equal(receipt.TotalAmount, receipt.SubtotalAmount + receipt.TaxAmount + receipt.ServiceChargeAmount);
        Assert.Equal($"R-{order.Id:N}".ToUpperInvariant(), receipt.ReceiptNumber);
        var roundtrip = JsonSerializer.Deserialize<PaidOrderReceipt>(JsonSerializer.Serialize(receipt))!;
        Assert.Equal(receipt.ReceiptNumber, roundtrip.ReceiptNumber);
        Assert.Equal(receipt.Lines.Single(), roundtrip.Lines.Single());
    }
}
