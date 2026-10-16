using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.Services.Payment.Application.Refunds;

public static class RefundFinancialEvidence
{
    public static void Validate(PaymentRefund refund, PaymentRefundedV1 value)
    {
        var receipt = refund.Receipt ?? throw new InvalidOperationException("Completed refund receipt evidence is required.");
        if (refund.Status != "completed" || value.EventId == Guid.Empty || value.CorrelationId == Guid.Empty
            || value.OrganizationId != refund.OrganizationId || value.RestaurantId != refund.RestaurantId
            || value.BranchId != refund.BranchId || value.OrderId != refund.OrderId
            || value.PaymentIntentId != refund.PaymentIntentId || value.RefundId != refund.Id
            || value.Amount != refund.Amount || value.Currency != refund.Currency || value.ReasonCode != refund.ReasonCode
            || value.ReceiptNumber != receipt.ReceiptNumber || value.OccurredAtUtc != receipt.RefundedAtUtc
            || value.CapturedAmount != receipt.CapturedAmount || value.CumulativeRefundedAmount != receipt.CumulativeRefundedAmount
            || receipt.RefundId != refund.Id || receipt.PaymentIntentId != refund.PaymentIntentId || receipt.OrderId != refund.OrderId
            || receipt.Amount != refund.Amount || receipt.Currency != refund.Currency || receipt.ReasonCode != refund.ReasonCode
            || receipt.Amount <= 0 || receipt.CumulativeRefundedAmount < receipt.Amount || receipt.CumulativeRefundedAmount > receipt.CapturedAmount)
            throw new InvalidOperationException("Refund financial event does not match original completed evidence.");
    }
}
