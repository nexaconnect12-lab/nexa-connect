using NexaConnect.Services.Payment.Infrastructure.Providers;
using System.Text.Json.Serialization;

namespace NexaConnect.Services.Payment.Application.Intents;

public sealed record CreatePaymentIntent(Guid RestaurantId, Guid BranchId, Guid OrderId, string IdempotencyKey,
    decimal Amount, string Currency, string PaymentMethod);
public sealed record PaymentMutationContext(string ActorSubjectId, Guid CorrelationId);
public sealed record PaymentIntent(Guid Id, Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId, decimal Amount,
    string Currency, string PaymentMethod, string Status, DateTimeOffset CreatedAtUtc, long ConcurrencyVersion = 1,
    string? ProviderAuthorizationId = null, string? FailureCode = null,
    [property: JsonIgnore] string? LeaseOwner = null,
    [property: JsonIgnore] DateTimeOffset? LeaseExpiresAtUtc = null,
    [property: JsonIgnore] int AuthorizationAttemptCount = 0,
    [property: JsonIgnore] DateTimeOffset? LastReconciledAtUtc = null);
public sealed record PaymentAuthorizationLease(PaymentIntent Intent, bool Acquired);
public sealed record PaymentAuthorizationClaim(PaymentIntent Intent, bool Claimed);

public sealed class PaymentIdempotencyConflictException(string message) : InvalidOperationException(message);
public sealed class PaymentConcurrencyException(string message) : InvalidOperationException(message);

public interface IPaymentIntents
{
    PaymentIntent Create(Guid organizationId, CreatePaymentIntent command, PaymentMutationContext context);
    PaymentIntent? Get(Guid organizationId, Guid id);
    PaymentAuthorizationLease BeginAuthorization(Guid organizationId, Guid id, PaymentMutationContext context);
    PaymentIntent CompleteAuthorization(Guid organizationId, Guid id, long expectedVersion,
        bool succeeded, string? providerAuthorizationId, string? failureCode, PaymentMutationContext context);
    PaymentIntent CompleteAuthorization(Guid organizationId, Guid id, long expectedVersion,
        ProviderAuthorizationOutcome outcome, string? providerAuthorizationId, string? failureCode, PaymentMutationContext context)
        => CompleteAuthorization(organizationId, id, expectedVersion, outcome == ProviderAuthorizationOutcome.Authorized,
            providerAuthorizationId, failureCode, context);
    PaymentAuthorizationLease ClaimExpiredAuthorization(Guid organizationId, Guid id, PaymentMutationContext context)
        => new(Get(organizationId, id) ?? throw new KeyNotFoundException("Payment intent was not found."), false);
    PaymentIntent ReconcileAuthorization(Guid organizationId, Guid id, long expectedVersion,
        ProviderAuthorizationOutcome outcome, string? providerAuthorizationId, string? failureCode, PaymentMutationContext context)
        => throw new NotSupportedException("Payment authorization reconciliation is not supported by this store.");
    IReadOnlyCollection<PaymentIntent> FindExpiredAuthorizations() => [];
}

public interface IPaymentAuthorizationService
{
    Task<PaymentIntent?> AuthorizeAsync(Guid organizationId, Guid id, PaymentMutationContext context,
        CancellationToken cancellationToken);
}
