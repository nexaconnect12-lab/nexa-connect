using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Application.Refunds;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class InMemoryPaymentRefunds(IPaymentIntents intents, IOptions<PaymentProviderOptions>? options = null) : IPaymentRefunds
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, PaymentRefund> values = [];
    private readonly TimeSpan leaseDuration = options?.Value.LeaseDuration ?? TimeSpan.FromMinutes(2);
    private readonly int maximumAttempts = options?.Value.MaximumRefundRecoveryAttempts ?? 3;

    public PaymentRefundLease Begin(Guid organizationId, Guid paymentIntentId, CreatePaymentRefund command, PaymentMutationContext context)
    {
        Validate(command, context);
        lock (gate)
        {
            PaymentIntent intent = intents.Get(organizationId, paymentIntentId)
                ?? throw new KeyNotFoundException("Payment intent was not found.");
            if (intent.Status != "captured") throw new InvalidOperationException("Only a captured payment can be refunded.");
            if (!string.Equals(intent.Currency, command.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The refund currency must match the captured payment.");
            PaymentRefund? replay = values.Values.SingleOrDefault(x => x.PaymentIntentId == paymentIntentId && x.OperationId == command.OperationId);
            if (replay is not null)
            {
                if (replay.Amount != command.Amount || replay.Currency != command.Currency.Trim().ToUpperInvariant()
                    || replay.ReasonCode != NormalizeReason(command.ReasonCode))
                    throw new PaymentRefundConflictException("The operation identifier is already associated with a different refund request.");
                return new(replay, intent, false);
            }
            decimal reserved = values.Values.Where(x => x.PaymentIntentId == paymentIntentId
                && x.Status is "processing" or "refund_unknown" or "review_required" or "completed").Sum(x => x.Amount);
            if (reserved + command.Amount > intent.Amount)
                throw new InvalidOperationException("The refund would exceed the captured payment amount.");
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var refund = new PaymentRefund(Guid.NewGuid(), organizationId, intent.RestaurantId, intent.BranchId, intent.OrderId,
                intent.Id, command.OperationId, command.Amount, command.Currency.Trim().ToUpperInvariant(), NormalizeReason(command.ReasonCode),
                "processing", context.ActorSubjectId.Trim(), command.AuthorizationDecisionId, now, null, null, null, null,
                1, context.ActorSubjectId.Trim(), now.Add(leaseDuration), 0);
            values.Add(refund.Id, refund);
            return new(refund, intent, true);
        }
    }

    public PaymentRefund Complete(Guid organizationId, Guid refundId, long expectedVersion, ProviderRefundOutcome outcome,
        string? providerRefundId, string? failureCode, PaymentMutationContext context) =>
        Transition(organizationId, refundId, expectedVersion, outcome, providerRefundId, failureCode, context, false);

    public PaymentRefund Reconcile(Guid organizationId, Guid refundId, long expectedVersion, ProviderRefundOutcome outcome,
        string? providerRefundId, string? failureCode, PaymentMutationContext context) =>
        Transition(organizationId, refundId, expectedVersion, outcome, providerRefundId, failureCode, context, true);

    public PaymentRefund? Get(Guid organizationId, Guid refundId)
    { lock (gate) return values.GetValueOrDefault(refundId) is { } r && r.OrganizationId == organizationId ? r : null; }
    public PaymentRefund? GetByOperation(Guid organizationId, Guid paymentIntentId, Guid operationId)
    { lock (gate) return values.Values.SingleOrDefault(x => x.OrganizationId == organizationId
        && x.PaymentIntentId == paymentIntentId && x.OperationId == operationId); }

    public IReadOnlyCollection<PaymentRefund> List(Guid organizationId, Guid paymentIntentId)
    { lock (gate) return values.Values.Where(x => x.OrganizationId == organizationId && x.PaymentIntentId == paymentIntentId)
        .OrderByDescending(x => x.RequestedAtUtc).ToArray(); }

    public PaymentRefundLease ClaimExpired(Guid organizationId, Guid refundId, PaymentMutationContext context)
    {
        lock (gate)
        {
            PaymentRefund refund = GetInternal(organizationId, refundId);
            PaymentIntent intent = intents.Get(organizationId, refund.PaymentIntentId)!;
            if (refund.Status == "processing" && refund.LeaseExpiresAtUtc > DateTimeOffset.UtcNow) return new(refund, intent, false);
            if (refund.Status is not ("processing" or "refund_unknown")) return new(refund, intent, false);
            int attempts = refund.RecoveryAttemptCount + 1;
            if (attempts > maximumAttempts)
            {
                PaymentRefund exhausted = refund with { Status = "review_required", FailureCode = "refund_recovery_exhausted",
                    LeaseOwner = null, LeaseExpiresAtUtc = null, RecoveryAttemptCount = attempts, ConcurrencyVersion = refund.ConcurrencyVersion + 1 };
                values[refundId] = exhausted; return new(exhausted, intent, false);
            }
            PaymentRefund claimed = refund with { Status = "processing", LeaseOwner = context.ActorSubjectId.Trim(),
                LeaseExpiresAtUtc = DateTimeOffset.UtcNow.Add(leaseDuration), RecoveryAttemptCount = attempts,
                ConcurrencyVersion = refund.ConcurrencyVersion + 1 };
            values[refundId] = claimed; return new(claimed, intent, true);
        }
    }

    public IReadOnlyCollection<PaymentRefund> FindRecoverable()
    { lock (gate) return values.Values.Where(x => x.Status == "refund_unknown"
        || x.Status == "processing" && x.LeaseExpiresAtUtc <= DateTimeOffset.UtcNow).ToArray(); }

    private PaymentRefund Transition(Guid organizationId, Guid refundId, long expectedVersion, ProviderRefundOutcome outcome,
        string? providerRefundId, string? failureCode, PaymentMutationContext context, bool recovery)
    {
        ValidateContext(context);
        lock (gate)
        {
            PaymentRefund refund = GetInternal(organizationId, refundId);
            if (refund.Status == "completed") return refund;
            if (refund.Status != "processing" || refund.ConcurrencyVersion != expectedVersion)
                throw new PaymentConcurrencyException("The refund changed while provider processing was in progress.");
            if (outcome == ProviderRefundOutcome.Refunded && !SafeReference(providerRefundId))
                throw new ArgumentException("A completed refund requires a valid provider reference.");
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string status = outcome == ProviderRefundOutcome.Refunded ? "completed"
                : outcome == ProviderRefundOutcome.Failed ? "failed" : "refund_unknown";
            decimal cumulative = outcome == ProviderRefundOutcome.Refunded
                ? values.Values.Where(x => x.PaymentIntentId == refund.PaymentIntentId && x.Status == "completed").Sum(x => x.Amount) + refund.Amount : 0;
            PaymentRefundReceipt? receipt = outcome == ProviderRefundOutcome.Refunded
                ? new($"RF-{refund.Id:N}".ToUpperInvariant(), refund.Id, refund.PaymentIntentId, refund.OrderId, refund.Amount,
                    refund.Currency, refund.ReasonCode, now, intents.Get(organizationId, refund.PaymentIntentId)!.Amount, cumulative) : null;
            PaymentRefund result = refund with { Status = status, ProviderRefundId = outcome == ProviderRefundOutcome.Refunded ? providerRefundId!.Trim() : null,
                FailureCode = outcome == ProviderRefundOutcome.Refunded ? null : failureCode ?? (recovery ? "provider_refund_status_unknown" : "provider_refund_failed"),
                CompletedAtUtc = outcome == ProviderRefundOutcome.Refunded ? now : null, Receipt = receipt,
                LeaseOwner = null, LeaseExpiresAtUtc = null, ConcurrencyVersion = refund.ConcurrencyVersion + 1 };
            values[refundId] = result; return result;
        }
    }

    private PaymentRefund GetInternal(Guid organizationId, Guid id) =>
        values.TryGetValue(id, out PaymentRefund? value) && value.OrganizationId == organizationId
            ? value : throw new KeyNotFoundException("Refund was not found.");
    private static void Validate(CreatePaymentRefund command, PaymentMutationContext context)
    {
        ValidateContext(context);
        if (command.OperationId == Guid.Empty || command.AuthorizationDecisionId == Guid.Empty || command.Amount <= 0
            || decimal.Round(command.Amount, 4) != command.Amount || command.Currency?.Trim().Length != 3
            || !command.Currency.Trim().All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            throw new ArgumentException("A valid operation, authorization decision, amount and currency are required.");
        _ = NormalizeReason(command.ReasonCode);
    }
    private static void ValidateContext(PaymentMutationContext context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.ActorSubjectId) || context.ActorSubjectId.Length > 200
            || context.ActorSubjectId.Any(char.IsControl) || context.CorrelationId == Guid.Empty)
            throw new ArgumentException("A valid mutation actor and correlation identifier are required.");
    }
    internal static string NormalizeReason(string value) => value?.Trim().ToLowerInvariant() switch
    { "customer_request" => "customer_request", "duplicate_charge" => "duplicate_charge", "item_unavailable" => "item_unavailable",
      "service_issue" => "service_issue", "other" => "other", _ => throw new ArgumentException("The refund reason code is invalid.") };
    private static bool SafeReference(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 200 && !value.Any(char.IsControl);
}
