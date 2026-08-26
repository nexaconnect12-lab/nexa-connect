using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Application.Intents;

public sealed class PaymentCaptureRecoveryService(IPaymentIntents intents, IPaymentProvider provider)
{
    public async Task<PaymentIntent> ReconcileAsync(Guid organizationId, Guid id, PaymentMutationContext context,
        CancellationToken cancellationToken)
    {
        PaymentIntent intent = intents.Get(organizationId, id)
            ?? throw new KeyNotFoundException("Payment intent was not found.");
        ProviderCaptureResult status;
        try
        {
            status = await provider.GetCaptureStatusAsync(intent, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            status = new(ProviderCaptureOutcome.Unknown, null, "provider_timeout");
        }
        catch (HttpRequestException)
        {
            status = new(ProviderCaptureOutcome.Unknown, null, "provider_unavailable");
        }

        return intents.ReconcileCapture(organizationId, id, intent.ConcurrencyVersion, status.Outcome,
            status.ProviderTransactionId, NormalizeFailureCode(status.FailureReason), context);
    }

    private static string? NormalizeFailureCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "provider_capture_status_unknown";
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized is "provider_timeout" or "provider_unavailable" or "provider_capture_failed"
            or "provider_capture_status_missing" or "provider_capture_status_unknown"
            or "capture_attempts_exhausted") return normalized;
        if (normalized.StartsWith("provider_http_", StringComparison.Ordinal)
            && int.TryParse(normalized[14..], out int code) && code is >= 400 and <= 599) return normalized;
        return "provider_capture_status_unknown";
    }
}
