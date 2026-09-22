using System.Text.Json;
using System.Text.RegularExpressions;
using NexaConnect.Services.Payment.Application.Intents;

namespace NexaConnect.Services.Payment.Application.Webhooks;

public sealed class OmiseWebhookOptions
{
    public bool Enabled { get; set; }
    public string? Secret { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    public int MaximumAttempts { get; set; } = 20;
}

public static class OmiseWebhookIdentity
{
    public static bool Valid(string? id) => Regex.IsMatch(id ?? "", @"\Aevnt_test_[a-z0-9]{10,64}\z");
    public static string Parse(ReadOnlyMemory<byte> body)
    {
        using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String || !Valid(id.GetString()))
            throw new ArgumentException("A valid test event ID is required.");
        // All other delivered fields are deliberately ignored. Only canonical GET data is trusted.
        return id.GetString()!;
    }
}

public sealed record WebhookClaim(string EventId, Guid Fence, Guid CorrelationId, int Attempts, string? TraceCorrelationId = null);
public sealed record VerifiedOmiseEvent(Guid OrganizationId, Guid IntentId, Guid OrderId,
    string ChargeId, long AmountSatang, string Currency);
public sealed record OmiseEventLookup(VerifiedOmiseEvent? Event, bool Retry);
public sealed record OmiseWebhookProcessResult(string Outcome, bool FinancialTransitionCommitted);
public interface IOmiseEventVerifier
{
    Task<OmiseEventLookup> VerifyAsync(string eventId, CancellationToken token);
}
public interface IOmiseWebhookInbox
{
    Task EnqueueAsync(string eventId, Guid correlationId, CancellationToken token, string? traceCorrelationId = null);
    Task<WebhookClaim?> ClaimAsync(TimeSpan lease, CancellationToken token);
    Task FinishAsync(WebhookClaim claim, string outcome, TimeSpan retryDelay, int maximumAttempts, CancellationToken token);
}
public interface IWebhookPaymentRecovery
{
    Task<bool> ReconcileAsync(PaymentIntent intent, PaymentMutationContext context, CancellationToken token);
}

public sealed class OmiseWebhookIngress(IOmiseWebhookInbox inbox)
{
    public Task ReceiveAsync(string eventId, Guid correlationId, string? traceCorrelationId, CancellationToken token)
    {
        if (!OmiseWebhookIdentity.Valid(eventId) || correlationId == Guid.Empty)
            throw new ArgumentException("Invalid webhook identity.");
        return inbox.EnqueueAsync(eventId, correlationId, token, traceCorrelationId);
    }
}

public sealed class OmiseWebhookProcessor(IOmiseEventVerifier verifier, IPaymentIntents intents, IWebhookPaymentRecovery recovery)
{
    public async Task<OmiseWebhookProcessResult> ProcessAsync(WebhookClaim claim, CancellationToken token)
    {
        OmiseEventLookup lookup = await verifier.VerifyAsync(claim.EventId, token);
        if (lookup.Event is not { } message) return new(lookup.Retry ? "retry" : "rejected", false);
        PaymentIntent? intent = intents.Get(message.OrganizationId, message.IntentId);
        if (intent is null || intent.OrganizationId != message.OrganizationId || intent.OrderId != message.OrderId
            || intent.PaymentMethod != "card" || intent.Currency != "THB" || message.Currency != "THB"
            || intent.Amount <= 0 || decimal.Truncate(intent.Amount * 100) != intent.Amount * 100
            || intent.Amount * 100 != message.AmountSatang
            || intent.ProviderAuthorizationId is { } reference && reference != message.ChargeId)
            return new("rejected", false);
        // Delayed notifications never regress terminal state or start a new financial command.
        if (intent.Status is not ("authorizing" or "unknown" or "capturing" or "capture_unknown" or "voiding" or "void_unknown"))
            return new("completed", false);
        bool reconciled = await recovery.ReconcileAsync(intent, new("omise-webhook-recovery", claim.CorrelationId), token);
        return new(reconciled ? "completed" : "retry", reconciled);
    }
}
