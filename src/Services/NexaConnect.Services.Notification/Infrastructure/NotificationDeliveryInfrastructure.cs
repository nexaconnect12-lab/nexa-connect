using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Notification.Application.Delivery;
using NexaConnect.Services.Notification.Domain;
using Npgsql;

namespace NexaConnect.Services.Notification.Infrastructure;

public sealed class NotificationProviderOptions
{
    public string BaseUrl { get; set; } = "";
    public string Path { get; set; } = "notifications";
    public string ReceiptPath { get; set; } = "notifications/{id}";
    public string ProviderCode { get; set; } = "configured";
    public string ApiToken { get; set; } = "";
}

public sealed class HttpNotificationProvider(HttpClient client, IOptions<NotificationProviderOptions> options)
    : INotificationProvider
{
    public Task<NotificationProviderResult> SubmitAsync(NotificationDeliveryWork work, CancellationToken token) =>
        SendAsync(HttpMethod.Post, options.Value.Path,
            new ProviderRequest(work.NotificationId, work.Channel, work.Recipient, work.Subject, work.Body),
            work.NotificationId, work.RequestCorrelationId, token);

    public Task<NotificationProviderResult> GetReceiptAsync(NotificationDeliveryWork work, CancellationToken token)
    {
        string receiptId = Uri.EscapeDataString(work.ProviderMessageId ?? work.NotificationId.ToString("D"));
        string path = options.Value.ReceiptPath.Replace("{id}", receiptId, StringComparison.Ordinal);
        return SendAsync(HttpMethod.Get, path, null, work.NotificationId, work.RequestCorrelationId, token);
    }

    private async Task<NotificationProviderResult> SendAsync(HttpMethod method, string path, object? body,
        Guid notificationId, string correlationId, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", options.Value.ApiToken);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", notificationId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        if (body is not null) request.Content = JsonContent.Create(body);

        using HttpResponseMessage response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
            || (int)response.StatusCode >= 500)
            return new(NotificationProviderOutcome.TransientFailure, options.Value.ProviderCode,
                ErrorCategory: $"http_{(int)response.StatusCode}", RetryAtUtc: response.Headers.RetryAfter?.Date);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            return new(NotificationProviderOutcome.PermanentFailure, options.Value.ProviderCode,
                ErrorCategory: $"http_{(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
            return new(NotificationProviderOutcome.PermanentFailure, options.Value.ProviderCode,
                ErrorCategory: "provider_authorization");

        ProviderResponse? value = await response.Content.ReadFromJsonAsync<ProviderResponse>(cancellationToken: token);
        if (value is null
            || !Enum.TryParse(value.Outcome, true, out NotificationProviderOutcome outcome)
            || !Enum.IsDefined(outcome)
            || InvalidOpaqueId(value.ProviderMessageId)
            || InvalidCategory(value.ErrorCategory))
            return new(NotificationProviderOutcome.TransientFailure, options.Value.ProviderCode,
                ErrorCategory: "invalid_response");
        return new(outcome, options.Value.ProviderCode, value.ProviderMessageId?.Trim(),
            value.ErrorCategory?.Trim().ToLowerInvariant(), value.RetryAtUtc);
    }

    private static bool InvalidOpaqueId(string? value) =>
        value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(char.IsControl));

    private static bool InvalidCategory(string? value) => value is not null
        && (value.Length is 0 or > 100 || value.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.'));

    private sealed record ProviderRequest(Guid ClientReference, string Channel, string Recipient, string Subject, string Body);
    private sealed record ProviderResponse(string Outcome, string? ProviderMessageId, string? ErrorCategory,
        DateTimeOffset? RetryAtUtc);
}

public sealed class PostgresNotificationDeliveryRepository(NpgsqlDataSource dataSource) : INotificationDeliveryRepository
{
    public async Task<NotificationDeliveryWork?> ClaimDueAsync(TimeSpan lease, CancellationToken token)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        Guid leaseId = Guid.NewGuid();
        const string statement = """
            WITH due AS
            (
                SELECT id,status
                FROM notifications
                WHERE organization_id IS NOT NULL
                  AND ((status IN ('queued','retry_scheduled','submitting') AND next_delivery_attempt_at_utc<=now())
                    OR (status IN ('provider_accepted','reconciling') AND next_receipt_attempt_at_utc<=now()))
                  AND (delivery_locked_until_utc IS NULL OR delivery_locked_until_utc<now())
                ORDER BY COALESCE(next_delivery_attempt_at_utc,next_receipt_attempt_at_utc),created_at_utc
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE notifications n
            SET status=CASE WHEN due.status IN ('provider_accepted','reconciling') THEN 'reconciling' ELSE 'submitting' END,
                delivery_lease_id=$1,delivery_locked_until_utc=now()+$2::interval,
                delivery_attempts=delivery_attempts+CASE WHEN due.status IN ('provider_accepted','reconciling') THEN 0 ELSE 1 END,
                receipt_attempts=receipt_attempts+CASE WHEN due.status IN ('provider_accepted','reconciling') THEN 1 ELSE 0 END,
                updated_at_utc=now(),concurrency_version=concurrency_version+1
            FROM due
            WHERE n.id=due.id
            RETURNING n.id,n.organization_id,n.channel,n.recipient,n.subject,n.body,due.status,
                n.delivery_attempts,n.receipt_attempts,n.provider_message_id,n.correlation_id;
            """;
        await using var command = new NpgsqlCommand(statement, connection, transaction);
        command.Parameters.AddWithValue(leaseId);
        command.Parameters.AddWithValue(lease);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
        {
            await reader.CloseAsync();
            await transaction.CommitAsync(token);
            return null;
        }

        Guid notificationId = reader.GetGuid(0);
        Guid organizationId = reader.GetGuid(1);
        string priorStatus = reader.GetString(6);
        NotificationDeliveryOperation operation = priorStatus is "provider_accepted" or "reconciling"
            ? NotificationDeliveryOperation.Reconcile
            : NotificationDeliveryOperation.Submit;
        int attempt = operation == NotificationDeliveryOperation.Submit ? reader.GetInt32(7) : reader.GetInt32(8);
        string? providerMessageId = reader.IsDBNull(9) ? null : reader.GetString(9);
        string correlationId = reader.GetString(10);
        var work = new NotificationDeliveryWork(leaseId, notificationId, organizationId, reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), operation, attempt, providerMessageId,
            Correlation(correlationId), correlationId);
        await reader.CloseAsync();
        await transaction.CommitAsync(token);
        return work;
    }

    public async Task RecordAsync(NotificationDeliveryWork work, NotificationProviderResult result,
        NotificationDeliveryDecision decision, CancellationToken token)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        string status = Status(decision.Status);
        string? errorCategory = Bound(decision.ErrorCategory, 100);

        await InsertAttemptAsync(connection, transaction, work, result, errorCategory, occurredAt, token);
        const string statement = """
            UPDATE notifications
            SET status=$1,provider_code=COALESCE($2,provider_code),
                provider_message_id=COALESCE($3,provider_message_id),
                next_delivery_attempt_at_utc=CASE WHEN $1='retry_scheduled' THEN $4::timestamptz ELSE NULL END,
                next_receipt_attempt_at_utc=CASE WHEN $1='provider_accepted' THEN $4::timestamptz ELSE NULL END,
                provider_accepted_at_utc=CASE WHEN $1='provider_accepted' THEN COALESCE(provider_accepted_at_utc,$5) ELSE provider_accepted_at_utc END,
                delivered_at_utc=CASE WHEN $1='delivered' THEN $5 ELSE delivered_at_utc END,
                delivery_failed_at_utc=CASE WHEN $1='delivery_failed' THEN $5 ELSE delivery_failed_at_utc END,
                last_error_category=$6,delivery_lease_id=NULL,delivery_locked_until_utc=NULL,
                updated_at_utc=$5,concurrency_version=concurrency_version+1
            WHERE id=$7 AND organization_id=$8 AND delivery_lease_id=$9;
            """;
        await using (var command = new NpgsqlCommand(statement, connection, transaction))
        {
            command.Parameters.AddWithValue(status);
            command.Parameters.AddWithValue((object?)Bound(result.ProviderCode, 64) ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)Bound(result.ProviderMessageId, 200) ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)decision.NextAttemptAtUtc ?? DBNull.Value);
            command.Parameters.AddWithValue(occurredAt);
            command.Parameters.AddWithValue((object?)errorCategory ?? DBNull.Value);
            command.Parameters.AddWithValue(work.NotificationId);
            command.Parameters.AddWithValue(work.OrganizationId);
            command.Parameters.AddWithValue(work.LeaseId);
            if (await command.ExecuteNonQueryAsync(token) != 1)
                throw new InvalidOperationException("Notification delivery lease is stale.");
        }

        if (decision.PublishLifecycleEvent)
            await AppendLifecycleAsync(connection, transaction, work, status, result.ProviderCode, occurredAt, token);
        await transaction.CommitAsync(token);
    }

    private static async Task InsertAttemptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        NotificationDeliveryWork work, NotificationProviderResult result, string? errorCategory,
        DateTimeOffset occurredAt, CancellationToken token)
    {
        const string statement = """
            INSERT INTO notification_delivery_attempts
                (id,notification_id,organization_id,operation,attempt_number,provider_code,outcome,error_category,occurred_at_utc)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9);
            """;
        await using var command = new NpgsqlCommand(statement, connection, transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(work.NotificationId);
        command.Parameters.AddWithValue(work.OrganizationId);
        command.Parameters.AddWithValue(work.Operation == NotificationDeliveryOperation.Submit ? "submit" : "reconcile");
        command.Parameters.AddWithValue(work.AttemptNumber);
        command.Parameters.AddWithValue(Bound(result.ProviderCode, 64) ?? "configured");
        command.Parameters.AddWithValue(result.Outcome.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue((object?)errorCategory ?? DBNull.Value);
        command.Parameters.AddWithValue(occurredAt);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task AppendLifecycleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        NotificationDeliveryWork work, string status, string providerCode, DateTimeOffset occurredAt,
        CancellationToken token)
    {
        string action = status switch
        {
            "provider_accepted" => "notification.delivery.accepted",
            "delivered" => "notification.delivered",
            _ => "notification.delivery.failed"
        };
        var changed = new NotificationDeliveryStatusChangedV1(Guid.NewGuid(), work.CorrelationId, occurredAt,
            work.NotificationId, work.OrganizationId, work.Channel, status, Bound(providerCode, 64) ?? "configured",
            work.RequestCorrelationId);
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), work.CorrelationId, occurredAt,
            "service:notification-delivery", work.OrganizationId, action, "notification",
            work.NotificationId.ToString("D"), status == "delivery_failed" ? "failed" : "succeeded");
        const string statement = """
            INSERT INTO notification_audit_records
                (id,organization_id,notification_id,action,actor_subject_id,occurred_at_utc)
            VALUES($1,$2,$3,$4,$5,$6);
            """;
        await using (var command = new NpgsqlCommand(statement, connection, transaction))
        {
            command.Parameters.AddWithValue(audit.EventId);
            command.Parameters.AddWithValue(work.OrganizationId);
            command.Parameters.AddWithValue(work.NotificationId);
            command.Parameters.AddWithValue(action);
            command.Parameters.AddWithValue(audit.SubjectId);
            command.Parameters.AddWithValue(occurredAt);
            await command.ExecuteNonQueryAsync(token);
        }
        PostgresNotificationSender.Enqueue(connection, transaction, changed.EventId,
            "notification.delivery-status-changed.v1", work.NotificationId, changed,
            work.RequestCorrelationId, occurredAt);
        PostgresNotificationSender.Enqueue(connection, transaction, audit.EventId,
            "notification.audit.v1", work.NotificationId, audit, work.RequestCorrelationId, occurredAt);
    }

    private static string Status(NotificationDeliveryStatus status) => status switch
    {
        NotificationDeliveryStatus.RetryScheduled => "retry_scheduled",
        NotificationDeliveryStatus.ProviderAccepted => "provider_accepted",
        NotificationDeliveryStatus.Delivered => "delivered",
        _ => "delivery_failed"
    };

    private static Guid Correlation(string value) => Guid.TryParse(value, out Guid id)
        ? id
        : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);

    private static string? Bound(string? value, int maximum) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim()[..Math.Min(maximum, value.Trim().Length)];
}

public sealed class NotificationDeliveryWorker(NotificationDeliveryProcessor processor,
    IOptions<NotificationDeliveryOptions> options, ILogger<NotificationDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await processor.ProcessOneAsync(token))
                    await Task.Delay(options.Value.PollInterval, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification delivery processing failed.");
                await Task.Delay(options.Value.PollInterval, token);
            }
        }
    }
}
