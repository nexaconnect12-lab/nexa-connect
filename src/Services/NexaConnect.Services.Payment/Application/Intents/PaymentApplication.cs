namespace NexaConnect.Services.Payment.Application.Intents;

public sealed record CreatePaymentIntent(Guid RestaurantId, Guid BranchId, Guid OrderId, string IdempotencyKey,
    decimal Amount, string Currency, string PaymentMethod);
public sealed record PaymentMutationContext(string ActorSubjectId, Guid CorrelationId);
public sealed record PaymentIntent(Guid Id, Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId, decimal Amount,
    string Currency, string PaymentMethod, string Status, DateTimeOffset CreatedAtUtc);

public sealed class PaymentIdempotencyConflictException(string message) : InvalidOperationException(message);

public interface IPaymentIntents
{
    PaymentIntent Create(Guid organizationId, CreatePaymentIntent command, PaymentMutationContext context);
    PaymentIntent? Get(Guid organizationId, Guid id);
}
