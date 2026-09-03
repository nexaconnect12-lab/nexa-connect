using NexaConnect.Services.Order.Domain;

namespace NexaConnect.UnitTests;

public sealed class ManualTenderSettlementTests
{
    [Theory]
    [InlineData("cash", false)]
    [InlineData("promptpay_manual", true)]
    public void Supported_manual_tender_settles_matching_thb_order(string method, bool receiptConfirmed)
    {
        OrderAggregate order = AcceptedOrder();
        ManualTenderSettlement settlement = ManualTenderSettlement.Create(order, order.OrganizationId, order.BranchId,
            Guid.NewGuid(), Guid.NewGuid(), method, order.TotalAmount, "thb", "cashier-subject", receiptConfirmed,
            method == "promptpay_manual" ? "bank-ref" : null, DateTimeOffset.UtcNow);

        order.MarkManuallyPaid(settlement);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Null(order.PaymentIntentId);
        Assert.Equal("THB", settlement.Currency);
    }

    [Fact]
    public void Promptpay_requires_explicit_receipt_confirmation()
    {
        OrderAggregate order = AcceptedOrder();
        Assert.Throws<ArgumentException>(() => ManualTenderSettlement.Create(order, order.OrganizationId, order.BranchId,
            Guid.NewGuid(), Guid.NewGuid(), "promptpay_manual", order.TotalAmount, "THB", "cashier", false, null,
            DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(9.99, "THB")]
    [InlineData(10.00, "USD")]
    public void Amount_and_currency_must_exactly_match_order(decimal amount, string currency)
    {
        OrderAggregate order = AcceptedOrder();
        Assert.Throws<ArgumentException>(() => ManualTenderSettlement.Create(order, order.OrganizationId, order.BranchId,
            Guid.NewGuid(), Guid.NewGuid(), "cash", amount, currency, "cashier", false, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Zero_total_cannot_be_manually_settled()
    {
        var order=OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(),"Complimentary",0m,1,"kitchen")],"THB");
        order.Submit();order.MarkInventoryReserved();order.MarkKitchenAccepted();
        Assert.Throws<ArgumentException>(()=>ManualTenderSettlement.Create(order,order.OrganizationId,order.BranchId,
            Guid.NewGuid(),Guid.NewGuid(),"cash",0m,"THB","cashier",false,null,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Provider_bound_order_cannot_be_manually_settled()
    {
        OrderAggregate order = AcceptedOrder();
        order.MarkPaymentPending(Guid.NewGuid());
        ManualTenderSettlement settlement = ManualTenderSettlement.Create(order, order.OrganizationId, order.BranchId,
            Guid.NewGuid(), Guid.NewGuid(), "cash", order.TotalAmount, "THB", "cashier", false, null, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => order.MarkManuallyPaid(settlement));
    }

    [Fact]
    public void Cross_tenant_or_cross_branch_settlement_is_rejected()
    {
        OrderAggregate order = AcceptedOrder();
        Assert.Throws<ArgumentException>(() => ManualTenderSettlement.Create(order, Guid.NewGuid(), order.BranchId,
            Guid.NewGuid(), Guid.NewGuid(), "cash", order.TotalAmount, "THB", "cashier", false, null, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => ManualTenderSettlement.Create(order, order.OrganizationId, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "cash", order.TotalAmount, "THB", "cashier", false, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Terminal_operator_idempotency_and_bank_reference_are_bounded()
    {
        OrderAggregate order = AcceptedOrder();
        Assert.Throws<ArgumentException>(() => ManualTenderSettlement.Create(order, order.OrganizationId, order.BranchId,
            Guid.Empty, Guid.NewGuid(), "cash", order.TotalAmount, "THB", "cashier", false, null, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => ManualTenderSettlement.Create(order, order.OrganizationId, order.BranchId,
            Guid.NewGuid(), Guid.Empty, "cash", order.TotalAmount, "THB", "cashier", false, null, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => ManualTenderSettlement.Create(order, order.OrganizationId, order.BranchId,
            Guid.NewGuid(), Guid.NewGuid(), "promptpay_manual", order.TotalAmount, "THB", "cashier", true,
            new string('x', 65), DateTimeOffset.UtcNow));
    }

    private static OrderAggregate AcceptedOrder()
    {
        var order = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(), "Meal", 10m, 1, "kitchen")], "THB");
        order.Submit();
        order.MarkInventoryReserved();
        order.MarkKitchenAccepted();
        return order;
    }
}
