using NexaConnect.Services.POS.Domain.ManualRefunds;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Contracts.POS;

namespace NexaConnect.Services.POS.Application.ManualRefunds;

// The application must obtain this evidence from Order and authorize the caller before
// using the store. These records are not request models.
public sealed record OriginalManualSale(Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId,
    Guid SettlementId, ManualRefundTender Tender, decimal PaidAmount, string Currency, DateTimeOffset PaidAtUtc);

public sealed record ManualRefundWrite(ManualRefund Refund, bool Replayed);
public sealed record StoredManualRefund(OriginalManualSale Sale, ManualRefundApproval Approval,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc, DateTimeOffset? ReleasedAtUtc = null);

public interface IManualRefundStore
{
    Task<StoredManualRefund?> ReadAsync(RestaurantAuthorizationScope scope, Guid orderId, Guid operationId, CancellationToken ct);
    Task<IReadOnlyList<StoredManualRefund>> ListOutstandingAsync(RestaurantAuthorizationScope scope, string cashierSubject, int limit, CancellationToken ct);
    Task<IReadOnlyList<StoredManualRefund>> ListManagedAsync(RestaurantAuthorizationScope scope, Guid orderId, string manager, int limit, CancellationToken ct);
    Task<decimal> AvailableAsync(OriginalManualSale sale, CancellationToken ct);
    Task<ManualRefundWrite> ApproveAsync(OriginalManualSale sale, ManualRefundApproval approval, CancellationToken ct, Guid? cashierShiftId = null);
    Task<ManualRefundWrite> ReleaseAsync(OriginalManualSale sale, Guid operationId, ManualRefundRelease release, CancellationToken ct);
    Task<bool> RegisterCashierAsync(RestaurantAuthorizationScope scope, ManualRefundAvailabilityV1 terminal, string subject, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<ManualRefundCashierV1>> ListReadyCashiersAsync(RestaurantAuthorizationScope scope, DateTimeOffset now, CancellationToken ct);
    Task<ManualRefundWrite> BeginAsync(OriginalManualSale sale, Guid operationId, ManualRefundPayout payout, CancellationToken ct);
    Task<ManualRefundWrite> CompleteAsync(OriginalManualSale sale, Guid operationId, ManualRefundPayout custody,
        bool transferVerified, string? transferReference, DateTimeOffset now, CancellationToken ct);
}
