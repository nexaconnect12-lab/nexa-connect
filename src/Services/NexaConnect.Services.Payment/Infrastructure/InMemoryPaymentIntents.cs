using System.Collections.Concurrent;
using NexaConnect.Services.Payment.Application.Intents;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class InMemoryPaymentIntents : IPaymentIntents
{
    private readonly ConcurrentDictionary<Guid, PaymentIntent> intents = new();
    private readonly ConcurrentDictionary<(Guid RestaurantId, string Key), Guid> idempotency = new();

    public PaymentIntent Create(CreatePaymentIntent command)
    {
        if (command.RestaurantId == Guid.Empty || command.BranchId == Guid.Empty || command.OrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.Amount <= 0 || string.IsNullOrWhiteSpace(command.Currency))
            throw new ArgumentException("Restaurant, branch, order, idempotency key, amount, and currency are required.");
        if (!Enum.TryParse<PaymentMethod>(command.PaymentMethod, true, out _))
            throw new ArgumentException("Payment method must be cash, card, wallet, bank_transfer, or other.");
        if (idempotency.TryGetValue((command.RestaurantId, command.IdempotencyKey), out Guid existingId))
            return intents[existingId];
        var intent = new PaymentIntent(Guid.NewGuid(), command.RestaurantId, command.BranchId, command.OrderId,
            command.Amount, command.Currency.Trim().ToUpperInvariant(), command.PaymentMethod.ToLowerInvariant(), "pending", DateTimeOffset.UtcNow);
        if (idempotency.TryAdd((command.RestaurantId, command.IdempotencyKey), intent.Id)) intents[intent.Id] = intent;
        return intents[idempotency[(command.RestaurantId, command.IdempotencyKey)]];
    }

    public PaymentIntent? Get(Guid id) => intents.GetValueOrDefault(id);

    private enum PaymentMethod { cash, card, wallet, bank_transfer, other }
}
