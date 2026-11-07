using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.Services.Reporting.Application;

public interface IManualRefundFinancialFacts
{
    Task<bool> ProjectAsync(PosManualRefundCompletedV1 value, CancellationToken ct);
}

public sealed class ManualRefundFinancialReporting(IManualRefundFinancialFacts facts)
{
    public static void Validate(PosManualRefundCompletedV1 value)
    {
        if (new[] { value.EventId, value.CorrelationId, value.OrganizationId, value.RestaurantId, value.BranchId,
                value.OrderId, value.SettlementId, value.OperationId }.Any(id => id == Guid.Empty)
            || value.Currency != "THB" || value.Method is not ("cash" or "promptpay_manual")
            || value.Amount is <= 0 or >= 1_000_000_000_000_000m || decimal.Round(value.Amount, 4) != value.Amount
            || value.OccurredAtUtc == default || value.OccurredAtUtc.UtcTicks % 10 != 0
            || value.CashMovementId == Guid.Empty || (value.Method == "cash") != (value.CashMovementId is not null))
            throw new ArgumentException("Invalid manual refund completion event.");
    }

    public Task<bool> ProjectAsync(PosManualRefundCompletedV1 value, CancellationToken ct)
    {
        Validate(value);
        return facts.ProjectAsync(value, ct);
    }
}
