using System.Collections.Concurrent;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Domain;
using NexaConnect.Services.Payment.Infrastructure.Providers;
using Microsoft.Extensions.Options;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class InMemoryPaymentIntents(IOptions<PaymentProviderOptions>? options = null) : IPaymentIntents
{
    private readonly TimeSpan leaseDuration = options?.Value.LeaseDuration > TimeSpan.Zero ? options.Value.LeaseDuration : TimeSpan.FromMinutes(2);
    private readonly Dictionary<Guid, PaymentIntent> intents = [];
    private readonly Dictionary<(Guid OrganizationId, Guid RestaurantId, string Key), Guid> idempotency = [];
    private readonly object gate = new();

    public PaymentIntent Create(Guid organizationId, CreatePaymentIntent command, PaymentMutationContext context)
    {
        ValidateContext(context);
        PaymentIntentAggregate candidate = PaymentIntentAggregate.Create(organizationId, command.RestaurantId, command.BranchId,
            command.OrderId, command.IdempotencyKey, command.Amount, command.Currency, command.PaymentMethod, DateTimeOffset.UtcNow);
        var key = (organizationId, command.RestaurantId, candidate.IdempotencyKey);
        lock (gate)
        {
            if (idempotency.TryGetValue(key, out Guid existingId))
            {
                PaymentIntent existing = intents[existingId];
                EnsureSameRequest(existing, candidate);
                return existing;
            }
            PaymentIntent intent = ToResult(candidate);
            intents[intent.Id] = intent;
            idempotency[key] = intent.Id;
            return intent;
        }
    }

    public PaymentIntent? Get(Guid organizationId, Guid id)
    {
        lock (gate) return intents.TryGetValue(id, out PaymentIntent? intent) && intent.OrganizationId == organizationId ? intent : null;
    }

    public PaymentAuthorizationLease BeginAuthorization(Guid organizationId, Guid id, PaymentMutationContext context)
    {
        ValidateContext(context);
        lock (gate)
        {
            if (!intents.TryGetValue(id, out PaymentIntent? intent) || intent.OrganizationId != organizationId)
                throw new KeyNotFoundException("Payment intent was not found.");
            if (intent.Status == "authorized") return new PaymentAuthorizationLease(intent, false);
            if (intent.Status == "authorizing") return new PaymentAuthorizationLease(intent, false);
            if (intent.Status != "pending") throw new InvalidOperationException("Only a pending payment intent can be authorized.");
            if (intent.AuthorizationAttemptCount >= 3)
            {
                PaymentIntent exhausted = intent with { Status = "requires_action", FailureCode = "authorization_attempts_exhausted",
                    ConcurrencyVersion = intent.ConcurrencyVersion + 1 };
                intents[id] = exhausted;
                return new PaymentAuthorizationLease(exhausted, false);
            }
            PaymentIntent authorizing = intent with { Status = "authorizing", LeaseOwner = context.ActorSubjectId.Trim(),
                LeaseExpiresAtUtc = DateTimeOffset.UtcNow.Add(leaseDuration), AuthorizationAttemptCount = intent.AuthorizationAttemptCount + 1,
                ConcurrencyVersion = intent.ConcurrencyVersion + 1, FailureCode = null };
            intents[id] = authorizing;
            return new PaymentAuthorizationLease(authorizing, true);
        }
    }

    public PaymentIntent CompleteAuthorization(Guid organizationId, Guid id, long expectedVersion, ProviderAuthorizationOutcome outcome,
        string? providerAuthorizationId, string? failureCode, PaymentMutationContext context)
    {
        ValidateContext(context);
        lock (gate)
        {
            if (!intents.TryGetValue(id, out PaymentIntent? intent) || intent.OrganizationId != organizationId)
                throw new KeyNotFoundException("Payment intent was not found.");
            if (intent.Status == "authorized") return intent;
            if (intent.Status != "authorizing" || intent.ConcurrencyVersion != expectedVersion)
                throw new PaymentConcurrencyException("The payment intent changed while authorization was in progress.");
            if (outcome == ProviderAuthorizationOutcome.Authorized && string.IsNullOrWhiteSpace(providerAuthorizationId))
                throw new ArgumentException("A successful authorization requires a provider reference.");
            PaymentIntent completed = intent with
            {
                Status = outcome switch { ProviderAuthorizationOutcome.Authorized => "authorized", ProviderAuthorizationOutcome.Declined or ProviderAuthorizationOutcome.Failed => "failed", _ => "unknown" },
                ConcurrencyVersion = intent.ConcurrencyVersion + 1,
                ProviderAuthorizationId = outcome == ProviderAuthorizationOutcome.Authorized ? providerAuthorizationId?.Trim() : null,
                FailureCode = outcome == ProviderAuthorizationOutcome.Authorized ? null : failureCode ?? (outcome == ProviderAuthorizationOutcome.Declined ? "provider_declined" : "provider_failed")
            };
            intents[id] = completed;
            return completed;
        }
    }

    public PaymentIntent CompleteAuthorization(Guid organizationId, Guid id, long expectedVersion, bool succeeded,
        string? providerAuthorizationId, string? failureCode, PaymentMutationContext context) =>
        CompleteAuthorization(organizationId, id, expectedVersion,
            succeeded ? ProviderAuthorizationOutcome.Authorized : ProviderAuthorizationOutcome.Declined,
            providerAuthorizationId, failureCode, context);

    public PaymentAuthorizationLease ClaimExpiredAuthorization(Guid organizationId, Guid id, PaymentMutationContext context)
    {
        ValidateContext(context);
        lock (gate)
        {
            if (!intents.TryGetValue(id, out PaymentIntent? intent) || intent.OrganizationId != organizationId)
                throw new KeyNotFoundException("Payment intent was not found.");
            if (intent.Status == "unknown")
            {
                if (intent.AuthorizationAttemptCount >= 3)
                {
                    PaymentIntent exhausted = intent with { Status = "requires_action", FailureCode = "authorization_attempts_exhausted",
                        ConcurrencyVersion = intent.ConcurrencyVersion + 1 };
                    intents[id] = exhausted;
                    return new PaymentAuthorizationLease(exhausted, false);
                }
                PaymentIntent retry = intent with { Status = "authorizing", LeaseOwner = context.ActorSubjectId.Trim(),
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.Add(leaseDuration), AuthorizationAttemptCount = intent.AuthorizationAttemptCount + 1,
                    ConcurrencyVersion = intent.ConcurrencyVersion + 1 };
                intents[id] = retry;
                return new PaymentAuthorizationLease(retry, true);
            }
            if (intent.Status != "authorizing" || intent.LeaseExpiresAtUtc > DateTimeOffset.UtcNow)
                return new PaymentAuthorizationLease(intent, false);
            PaymentIntent reclaimed = intent with { Status = "unknown", LeaseOwner = null, LeaseExpiresAtUtc = null,
                ConcurrencyVersion = intent.ConcurrencyVersion + 1 };
            intents[id] = reclaimed;
            return new PaymentAuthorizationLease(reclaimed, true);
        }
    }

    public PaymentIntent ReconcileAuthorization(Guid organizationId, Guid id, long expectedVersion,
        ProviderAuthorizationOutcome outcome, string? providerAuthorizationId, string? failureCode, PaymentMutationContext context)
    {
        ValidateContext(context);
        lock (gate)
        {
            if (!intents.TryGetValue(id, out PaymentIntent? intent) || intent.OrganizationId != organizationId)
                throw new KeyNotFoundException("Payment intent was not found.");
            if (intent.ConcurrencyVersion != expectedVersion) throw new PaymentConcurrencyException("The payment intent changed while reconciliation was in progress.");
            string status = outcome switch { ProviderAuthorizationOutcome.Authorized => "authorized", ProviderAuthorizationOutcome.Declined or ProviderAuthorizationOutcome.Failed => "failed", _ => "requires_action" };
            PaymentIntent result = intent with { Status = status, ProviderAuthorizationId = outcome == ProviderAuthorizationOutcome.Authorized ? providerAuthorizationId : intent.ProviderAuthorizationId,
                FailureCode = outcome == ProviderAuthorizationOutcome.Authorized ? null : failureCode ?? "provider_status_unknown",
                LeaseOwner = null, LeaseExpiresAtUtc = null,
                LastReconciledAtUtc = DateTimeOffset.UtcNow, ConcurrencyVersion = intent.ConcurrencyVersion + 1 };
            intents[id] = result;
            return result;
        }
    }

    public IReadOnlyCollection<PaymentIntent> FindExpiredAuthorizations()
    {
        lock (gate) return intents.Values.Where(intent => intent.Status == "unknown" || (intent.Status == "authorizing" && intent.LeaseExpiresAtUtc <= DateTimeOffset.UtcNow)).ToArray();
    }

    private static void ValidateContext(PaymentMutationContext context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.ActorSubjectId) || context.ActorSubjectId.Length > 200
            || context.ActorSubjectId.Any(char.IsControl) || context.CorrelationId == Guid.Empty)
            throw new ArgumentException("A valid mutation actor and correlation identifier are required.");
    }

    private static void EnsureSameRequest(PaymentIntent existing, PaymentIntentAggregate candidate)
    {
        if (existing.BranchId != candidate.BranchId || existing.OrderId != candidate.OrderId || existing.Amount != candidate.Amount
            || !string.Equals(existing.Currency, candidate.Currency, StringComparison.Ordinal)
            || !string.Equals(existing.PaymentMethod, candidate.PaymentMethod, StringComparison.Ordinal))
            throw new PaymentIdempotencyConflictException("The idempotency key is already associated with a different payment request.");
    }

    private static PaymentIntent ToResult(PaymentIntentAggregate intent) => new(intent.Id, intent.OrganizationId,
        intent.RestaurantId, intent.BranchId, intent.OrderId, intent.Amount, intent.Currency, intent.PaymentMethod,
        intent.Status, intent.CreatedAtUtc);
}
