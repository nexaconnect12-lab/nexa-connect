using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.Services.POS.Application.OrderSettlements;

public enum OrderSettlementProjectionStatus { Applied, Replayed }
public sealed class OrderSettlementProjectionConflictException(string message) : Exception(message);

public interface IOrderSettlementProjectionStore
{
    Task<OrderSettlementProjectionStatus> ProjectAsync(OrderManualTenderSettledV1 settlement, CancellationToken cancellationToken);
}

public sealed class OrderSettlementProjectionService(IOrderSettlementProjectionStore store)
{
    public Task<OrderSettlementProjectionStatus> ProjectAsync(OrderManualTenderSettledV1 settlement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        if (settlement.EventId == Guid.Empty || settlement.CorrelationId == Guid.Empty || settlement.OrganizationId == Guid.Empty
            || settlement.RestaurantId == Guid.Empty || settlement.BranchId == Guid.Empty || settlement.OrderId == Guid.Empty
            || settlement.SettlementId == Guid.Empty || settlement.TerminalId == Guid.Empty || settlement.Amount <= 0
            || !string.Equals(settlement.Currency, "THB", StringComparison.Ordinal)
            || settlement.Method is not ("cash" or "promptpay_manual") || settlement.OccurredAtUtc == default)
            throw new ArgumentException("The manual-tender event is incomplete or unsupported.", nameof(settlement));
        return store.ProjectAsync(settlement, cancellationToken);
    }
}
