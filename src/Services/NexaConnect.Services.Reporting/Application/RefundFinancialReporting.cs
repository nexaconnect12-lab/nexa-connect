using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Reporting.Domain;

namespace NexaConnect.Services.Reporting.Application;

public interface IRefundFinancialFactRepository
{
    Task<bool> ProjectAsync(RefundFinancialFact fact, CancellationToken cancellationToken);
}

public sealed class RefundFinancialReporting(IRefundFinancialFactRepository repository)
{
    public static RefundFinancialFact Translate(PaymentRefundedV1 value)
    {
        if (value.CorrelationId == Guid.Empty) throw new ArgumentException("Refund correlation identity is required.");
        if (string.IsNullOrWhiteSpace(value.Currency) || string.IsNullOrWhiteSpace(value.ReasonCode)
            || string.IsNullOrWhiteSpace(value.ReceiptNumber))
            throw new ArgumentException("Refund currency, reason and receipt are required.");
        var fact = new RefundFinancialFact(value.EventId, value.OrganizationId, value.RestaurantId, value.BranchId,
            value.OrderId, value.PaymentIntentId, value.RefundId, value.Amount,
            value.Currency.Trim().ToUpperInvariant(), value.ReasonCode.Trim().ToLowerInvariant(), value.OccurredAtUtc,
            value.CumulativeRefundedAmount, value.CapturedAmount, value.ReceiptNumber.Trim());
        fact.Validate();
        return fact;
    }

    public Task<bool> ProjectAsync(PaymentRefundedV1 value, CancellationToken cancellationToken) =>
        repository.ProjectAsync(Translate(value), cancellationToken);
}
