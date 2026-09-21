using NexaConnect.Services.Payment.Application.Intents;

namespace NexaConnect.Services.Payment.Application.Webhooks;

public sealed class WebhookPaymentRecovery(IPaymentIntents intents, PaymentAuthorizationService authorization,
    PaymentCaptureRecoveryService capture, PaymentVoidRecoveryService reversal) : IWebhookPaymentRecovery
{
    public async Task<bool> ReconcileAsync(PaymentIntent intent, PaymentMutationContext context, CancellationToken token)
    {
        // Existing persistence claims fence against foreground commands and polling workers.
        PaymentAuthorizationLease claim = intent.Status switch
        {
            "authorizing" or "unknown" => intents.ClaimExpiredAuthorization(intent.OrganizationId, intent.Id, context),
            "capturing" or "capture_unknown" => intents.ClaimExpiredCapture(intent.OrganizationId, intent.Id, context),
            "voiding" or "void_unknown" => intents.ClaimExpiredVoid(intent.OrganizationId, intent.Id, context),
            _ => new(intent, false)
        };
        if (!claim.Acquired) return false;
        PaymentIntent? result = intent.Status switch
        {
            "authorizing" or "unknown" => await authorization.ReconcileAsync(intent.OrganizationId, intent.Id, context, token),
            "capturing" or "capture_unknown" => await capture.ReconcileAsync(intent.OrganizationId, intent.Id, context, token),
            _ => await reversal.ReconcileAsync(intent.OrganizationId, intent.Id, context, token)
        };
        return result?.Status is not (null or "unknown" or "authorizing" or "capturing" or "capture_unknown" or "voiding" or "void_unknown");
    }
}
