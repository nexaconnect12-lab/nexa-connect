using NexaConnect.Services.Notification.Domain;

namespace NexaConnect.Services.Notification.Application.Delivery;

public enum NotificationDeliveryOperation { Submit, Reconcile }
public enum NotificationProviderOutcome { Accepted, Delivered, Pending, TransientFailure, PermanentFailure }

public sealed record NotificationDeliveryWork(Guid LeaseId, Guid NotificationId, Guid OrganizationId, string Channel,
    string Recipient, string Subject, string Body, NotificationDeliveryOperation Operation, int AttemptNumber,
    string? ProviderMessageId, Guid CorrelationId, string RequestCorrelationId);

public sealed record NotificationProviderResult(NotificationProviderOutcome Outcome, string ProviderCode,
    string? ProviderMessageId = null, string? ErrorCategory = null, DateTimeOffset? RetryAtUtc = null);

public sealed record NotificationDeliveryDecision(NotificationDeliveryStatus Status, DateTimeOffset? NextAttemptAtUtc,
    string? ErrorCategory, bool PublishLifecycleEvent);

public interface INotificationProvider
{
    Task<NotificationProviderResult> SubmitAsync(NotificationDeliveryWork work, CancellationToken cancellationToken);
    Task<NotificationProviderResult> GetReceiptAsync(NotificationDeliveryWork work, CancellationToken cancellationToken);
}

public interface INotificationDeliveryRepository
{
    Task<NotificationDeliveryWork?> ClaimDueAsync(TimeSpan lease, CancellationToken cancellationToken);
    Task RecordAsync(NotificationDeliveryWork work, NotificationProviderResult result, NotificationDeliveryDecision decision,
        CancellationToken cancellationToken);
}

public sealed class NotificationDeliveryProcessor(
    INotificationDeliveryRepository repository,
    INotificationProvider provider,
    Microsoft.Extensions.Options.IOptions<NotificationDeliveryOptions> options)
{
    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        NotificationDeliveryWork? work = await repository.ClaimDueAsync(options.Value.Lease, cancellationToken);
        if (work is null) return false;
        NotificationProviderResult result;
        try
        {
            result = work.Operation == NotificationDeliveryOperation.Submit
                ? await provider.SubmitAsync(work, cancellationToken)
                : await provider.GetReceiptAsync(work, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = new NotificationProviderResult(NotificationProviderOutcome.TransientFailure, "configured",
                ErrorCategory: exception.GetType().Name);
        }
        NotificationDeliveryDecision decision = NotificationDeliveryPolicy.Decide(work, result,
            options.Value.MaximumAttempts, DateTimeOffset.UtcNow);
        await repository.RecordAsync(work, result, decision, cancellationToken);
        return true;
    }
}

public static class NotificationDeliveryPolicy
{
    public static NotificationDeliveryDecision Decide(NotificationDeliveryWork work, NotificationProviderResult result,
        int maximumAttempts, DateTimeOffset now)
    {
        bool receiptExhausted = work.Operation == NotificationDeliveryOperation.Reconcile
            && work.AttemptNumber >= maximumAttempts
            && result.Outcome is NotificationProviderOutcome.Accepted
                or NotificationProviderOutcome.Pending
                or NotificationProviderOutcome.TransientFailure;
        if (receiptExhausted)
            return new(NotificationDeliveryStatus.DeliveryFailed, null,
                result.ErrorCategory ?? "receipt_reconciliation_exhausted", true);

        return result.Outcome switch
        {
            NotificationProviderOutcome.Delivered => new(NotificationDeliveryStatus.Delivered, null,
                result.ErrorCategory, true),
            NotificationProviderOutcome.PermanentFailure => new(NotificationDeliveryStatus.DeliveryFailed, null,
                result.ErrorCategory, true),
            NotificationProviderOutcome.Accepted => new(NotificationDeliveryStatus.ProviderAccepted,
                now.AddSeconds(30), result.ErrorCategory, work.Operation == NotificationDeliveryOperation.Submit),
            NotificationProviderOutcome.Pending => new(NotificationDeliveryStatus.ProviderAccepted,
                result.RetryAtUtc ?? now.AddSeconds(30), result.ErrorCategory,
                work.Operation == NotificationDeliveryOperation.Submit),
            _ when work.AttemptNumber >= maximumAttempts => new(NotificationDeliveryStatus.DeliveryFailed, null,
                result.ErrorCategory ?? "retry_exhausted", true),
            _ => new(work.Operation == NotificationDeliveryOperation.Submit
                    ? NotificationDeliveryStatus.RetryScheduled
                    : NotificationDeliveryStatus.ProviderAccepted,
                result.RetryAtUtc ?? now.AddSeconds(Math.Min(300, Math.Pow(2, work.AttemptNumber))),
                result.ErrorCategory, false)
        };
    }
}

public sealed class NotificationDeliveryOptions
{
    public bool Enabled { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(2);
    public int MaximumAttempts { get; set; } = 8;
}
