namespace NexaConnect.Services.Payment.Application.Intents;

public sealed record CreatePaymentIntent(Guid RestaurantId, Guid BranchId, Guid OrderId, string IdempotencyKey,
    decimal Amount, string Currency, string PaymentMethod);
public sealed record PaymentIntent(Guid Id, Guid RestaurantId, Guid BranchId, Guid OrderId, decimal Amount,
    string Currency, string PaymentMethod, string Status, DateTimeOffset CreatedAtUtc);

public interface IPaymentIntents
{
    PaymentIntent Create(CreatePaymentIntent command);
    PaymentIntent? Get(Guid id);
}
