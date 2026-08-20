using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Application.Intents;

public sealed class PaymentAuthorizationService(IPaymentIntents intents, IPaymentProvider provider) : IPaymentAuthorizationService
{
    public async Task<PaymentIntent?> AuthorizeAsync(Guid organizationId, Guid id, PaymentMutationContext context,
        CancellationToken cancellationToken)
    {
        PaymentAuthorizationLease lease = intents.BeginAuthorization(organizationId, id, context);
        if (!lease.Acquired || lease.Intent.Status == "authorized")
            return lease.Intent;

        ProviderAuthorizationResult result;
        try
        {
            result = await provider.AuthorizeAsync(lease.Intent, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new ProviderAuthorizationResult(false, null, "provider_timeout");
        }
        catch (HttpRequestException)
        {
            result = new ProviderAuthorizationResult(false, null, "provider_unavailable");
        }

        return intents.CompleteAuthorization(organizationId, id, lease.Intent.ConcurrencyVersion,
            result.Succeeded, result.ProviderTransactionId, NormalizeFailureCode(result.FailureReason), context);
    }

    private static string? NormalizeFailureCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "provider_declined";
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized is "provider_timeout" or "provider_unavailable" or "provider_declined") return normalized;
        if (normalized.StartsWith("provider_http_", StringComparison.Ordinal)
            && int.TryParse(normalized[14..], out int statusCode) && statusCode is >= 400 and <= 599)
            return normalized;
        return "provider_declined";
    }
}
