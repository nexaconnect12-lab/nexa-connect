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
