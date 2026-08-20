using System.Collections.Concurrent;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Domain;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class InMemoryPaymentIntents : IPaymentIntents
{
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
            PaymentIntent authorizing = intent with { Status = "authorizing", ConcurrencyVersion = intent.ConcurrencyVersion + 1, FailureCode = null };
            intents[id] = authorizing;
            return new PaymentAuthorizationLease(authorizing, true);
        }
    }

    public PaymentIntent CompleteAuthorization(Guid organizationId, Guid id, long expectedVersion, bool succeeded,
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
            if (succeeded && string.IsNullOrWhiteSpace(providerAuthorizationId))
                throw new ArgumentException("A successful authorization requires a provider reference.");
            PaymentIntent completed = intent with
            {
                Status = succeeded ? "authorized" : "failed",
                ConcurrencyVersion = intent.ConcurrencyVersion + 1,
                ProviderAuthorizationId = succeeded ? providerAuthorizationId!.Trim() : null,
                FailureCode = succeeded ? null : failureCode ?? "provider_declined"
            };
            intents[id] = completed;
            return completed;
        }
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
