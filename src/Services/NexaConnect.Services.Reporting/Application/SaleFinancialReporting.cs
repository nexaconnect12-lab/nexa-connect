using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Reporting.Domain;

namespace NexaConnect.Services.Reporting.Application;

public interface ISaleFinancialFactRepository
{
    Task<bool> ProjectAsync(SaleFinancialFact fact, CancellationToken cancellationToken);
}

public sealed class SaleFinancialReporting(ISaleFinancialFactRepository repository)
{
    public static SaleFinancialFact Translate(OrderSaleCompletedV1 value)
    {
        if (value.CorrelationId == Guid.Empty || value.OccurredAtUtc != value.PaidAtUtc)
            throw new ArgumentException("Sale envelope is invalid.");
        var fact = new SaleFinancialFact(value.EventId, value.OrganizationId, value.RestaurantId, value.BranchId,
            value.OrderId, value.PaymentId, value.PaymentOrigin ?? "", value.Method ?? "", value.Currency ?? "",
            value.Channel ?? "", value.ServiceType ?? "", value.OrderedAtUtc.ToUniversalTime(), value.PaidAtUtc.ToUniversalTime(),
            value.ReceiptNumber ?? "", value.SubtotalAmount, value.ServiceChargeAmount, value.TaxAmount, value.TotalAmount);
        return fact.Canonicalize();
    }

    public Task<bool> ProjectAsync(OrderSaleCompletedV1 value, CancellationToken cancellationToken) =>
        repository.ProjectAsync(Translate(value), cancellationToken);
}
