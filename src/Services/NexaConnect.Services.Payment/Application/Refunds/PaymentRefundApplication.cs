using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure.Providers;
using System.Text.Json.Serialization;

namespace NexaConnect.Services.Payment.Application.Refunds;

public sealed record CreatePaymentRefund(Guid OperationId, decimal Amount, string Currency, string ReasonCode,
    Guid AuthorizationDecisionId);
public sealed record PaymentRefundReceipt(string ReceiptNumber, Guid RefundId, Guid PaymentIntentId, Guid OrderId,
    decimal Amount, string Currency, string ReasonCode, DateTimeOffset RefundedAtUtc,
    decimal CapturedAmount, decimal CumulativeRefundedAmount);
public sealed record PaymentRefund(Guid Id, Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId,
    Guid PaymentIntentId, Guid OperationId, decimal Amount, string Currency, string ReasonCode, string Status,
    [property: JsonIgnore] string RequestedBy, [property: JsonIgnore] Guid AuthorizationDecisionId,
    DateTimeOffset RequestedAtUtc, DateTimeOffset? CompletedAtUtc,
    [property: JsonIgnore] string? ProviderRefundId, string? FailureCode, PaymentRefundReceipt? Receipt,
    [property: JsonIgnore] long ConcurrencyVersion = 1, [property: JsonIgnore] string? LeaseOwner = null,
    [property: JsonIgnore] DateTimeOffset? LeaseExpiresAtUtc = null,
    [property: JsonIgnore] int RecoveryAttemptCount = 0);
public sealed record PaymentRefundLease(PaymentRefund Refund, PaymentIntent Intent, bool Acquired);
public sealed class PaymentRefundConflictException(string message) : InvalidOperationException(message);

public interface IPaymentRefunds
{
    PaymentRefundLease Begin(Guid organizationId, Guid paymentIntentId, CreatePaymentRefund command,
        PaymentMutationContext context);
    PaymentRefund Complete(Guid organizationId, Guid refundId, long expectedVersion, ProviderRefundOutcome outcome,
        string? providerRefundId, string? failureCode, PaymentMutationContext context);
    PaymentRefund? Get(Guid organizationId, Guid refundId);
    PaymentRefund? GetByOperation(Guid organizationId, Guid paymentIntentId, Guid operationId);
    IReadOnlyCollection<PaymentRefund> List(Guid organizationId, Guid paymentIntentId);
    PaymentRefundLease ClaimExpired(Guid organizationId, Guid refundId, PaymentMutationContext context);
    PaymentRefund Reconcile(Guid organizationId, Guid refundId, long expectedVersion, ProviderRefundOutcome outcome,
        string? providerRefundId, string? failureCode, PaymentMutationContext context);
    IReadOnlyCollection<PaymentRefund> FindRecoverable();
}

public interface IPaymentRefundService
{
    Task<PaymentRefund> RefundAsync(Guid organizationId, Guid paymentIntentId, CreatePaymentRefund command,
        PaymentMutationContext context, CancellationToken cancellationToken);
}

public sealed class PaymentRefundService(IPaymentRefunds refunds, IPaymentProvider provider) : IPaymentRefundService
{
    public async Task<PaymentRefund> RefundAsync(Guid organizationId, Guid paymentIntentId,
        CreatePaymentRefund command, PaymentMutationContext context, CancellationToken cancellationToken)
    {
        PaymentRefundLease lease = refunds.Begin(organizationId, paymentIntentId, command, context);
        if (!lease.Acquired) return lease.Refund;
        ProviderRefundResult result;
        try { result = await provider.RefundAsync(lease.Intent, lease.Refund, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        { result = new(ProviderRefundOutcome.Unknown, null, "provider_transport_failure"); }
        return refunds.Complete(organizationId, lease.Refund.Id, lease.Refund.ConcurrencyVersion, result.Outcome,
            result.ProviderTransactionId, result.FailureReason, context);
    }
}
