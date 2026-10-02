using System.Security.Cryptography;
using System.Text;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Application.Orders;

public static class SaleCompletionEvents
{
    public static OrderSaleCompletedV1 Create(PaidOrderReceipt receipt, DateTimeOffset orderedAtUtc,
        Guid? paymentIntentId, Guid? settlementId, string channel, string serviceType, Guid? correlationId)
    {
        if (receipt.Version != 1 || receipt.OrderId == Guid.Empty || orderedAtUtc == default)
            throw new InvalidOperationException("Supported immutable receipt evidence is required.");
        bool manual = receipt.Tender is "cash" or "promptpay_manual";
        Guid paymentId = (manual ? settlementId : paymentIntentId)
            ?? throw new InvalidOperationException("Original payment identity is required for sale publication.");
        if (paymentId == Guid.Empty || receipt.SubtotalAmount + receipt.ServiceChargeAmount + receipt.TaxAmount != receipt.TotalAmount)
            throw new InvalidOperationException("Sale financial evidence is inconsistent.");
        Guid eventId = new(SHA256.HashData(Encoding.UTF8.GetBytes($"NexaConnect.Order.SaleCompleted.v1:{receipt.OrderId:D}"))[..16]);
        return new(eventId, correlationId is { } id && id != Guid.Empty ? id : eventId, receipt.PaidAtUtc,
            receipt.OrganizationId, receipt.RestaurantId, receipt.BranchId, receipt.OrderId, paymentId,
            manual ? "manual_settlement" : "payment_intent", receipt.Tender, receipt.Currency,
            channel, serviceType, orderedAtUtc.ToUniversalTime(), receipt.PaidAtUtc.ToUniversalTime(),
            receipt.ReceiptNumber, receipt.SubtotalAmount, receipt.ServiceChargeAmount, receipt.TaxAmount, receipt.TotalAmount);
    }
}
