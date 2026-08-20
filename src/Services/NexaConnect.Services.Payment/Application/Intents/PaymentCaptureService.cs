using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Application.Intents;

public sealed class PaymentCaptureService(IPaymentIntents intents, IPaymentProvider provider) : IPaymentCaptureService
{
    public async Task<PaymentIntent?> CaptureAsync(Guid organizationId, Guid id, PaymentMutationContext context,
        CancellationToken cancellationToken)
    {
        PaymentAuthorizationLease lease = intents.BeginCapture(organizationId, id, context);
        if (!lease.Acquired) return lease.Intent;

        ProviderCaptureResult result;
        try
        {
            result = await provider.CaptureAsync(lease.Intent, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new ProviderCaptureResult(ProviderCaptureOutcome.Unknown, null, "provider_timeout");
        }
        catch (HttpRequestException)
        {
            result = new ProviderCaptureResult(ProviderCaptureOutcome.Unknown, null, "provider_unavailable");
        }

        return intents.CompleteCapture(organizationId, id, lease.Intent.ConcurrencyVersion, result.Outcome,
            result.ProviderTransactionId, NormalizeFailureCode(result.FailureReason), context);
    }

    private static string? NormalizeFailureCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "provider_capture_failed";
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized is "provider_timeout" or "provider_unavailable" or "provider_capture_failed") return normalized;
        if (normalized.StartsWith("provider_http_", StringComparison.Ordinal)
            && int.TryParse(normalized[14..], out int statusCode) && statusCode is >= 400 and <= 599) return normalized;
        return "provider_capture_failed";
    }
}
