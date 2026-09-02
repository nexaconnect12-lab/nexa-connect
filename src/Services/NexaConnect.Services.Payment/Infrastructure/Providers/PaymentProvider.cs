using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Application.Intents;

namespace NexaConnect.Services.Payment.Infrastructure.Providers;

public enum ProviderAuthorizationOutcome
{
    Authorized,
    Declined,
    Failed,
    Unknown
}

public sealed record ProviderAuthorizationResult(bool Succeeded, string? ProviderTransactionId, string? FailureReason,
    ProviderAuthorizationOutcome? Outcome = null)
{
    public ProviderAuthorizationOutcome EffectiveOutcome => Outcome ??
        (Succeeded ? ProviderAuthorizationOutcome.Authorized : ProviderAuthorizationOutcome.Declined);
}

public sealed record ProviderAuthorizationStatus(ProviderAuthorizationOutcome Outcome, string? ProviderTransactionId,
    string? FailureReason);

public enum ProviderCaptureOutcome { Captured, Failed, Unknown }
public sealed record ProviderCaptureResult(ProviderCaptureOutcome Outcome, string? ProviderTransactionId, string? FailureReason);
public enum ProviderVoidOutcome { Voided, Failed, Unknown }
public sealed record ProviderVoidResult(ProviderVoidOutcome Outcome, string? ProviderTransactionId, string? FailureReason);

public interface IPaymentProvider
{
    Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken);
    Task<ProviderAuthorizationStatus> GetAuthorizationStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => Task.FromResult(new ProviderAuthorizationStatus(ProviderAuthorizationOutcome.Unknown, null, "provider_status_unavailable"));
    Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => Task.FromResult(new ProviderCaptureResult(ProviderCaptureOutcome.Unknown, null, "provider_capture_unavailable"));
    Task<ProviderCaptureResult> GetCaptureStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => Task.FromResult(new ProviderCaptureResult(ProviderCaptureOutcome.Unknown, null, "provider_capture_status_unavailable"));
    Task<ProviderVoidResult> VoidAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => Task.FromResult(new ProviderVoidResult(ProviderVoidOutcome.Unknown, null, "provider_void_unavailable"));
    Task<ProviderVoidResult> GetVoidStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => Task.FromResult(new ProviderVoidResult(ProviderVoidOutcome.Unknown, null, "provider_void_status_unavailable"));
}

public sealed class DisabledPaymentProvider : IPaymentProvider
{
    public Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderAuthorizationResult(false, null, "provider_not_configured", ProviderAuthorizationOutcome.Failed));

    public Task<ProviderAuthorizationStatus> GetAuthorizationStatusAsync(PaymentIntent intent, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderAuthorizationStatus(ProviderAuthorizationOutcome.Unknown, null, "provider_not_configured"));

    public Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderCaptureResult(ProviderCaptureOutcome.Failed, null, "provider_not_configured"));

    public Task<ProviderCaptureResult> GetCaptureStatusAsync(PaymentIntent intent, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderCaptureResult(ProviderCaptureOutcome.Unknown, null, "provider_not_configured"));

    public Task<ProviderVoidResult> VoidAsync(PaymentIntent intent, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderVoidResult(ProviderVoidOutcome.Failed, null, "provider_not_configured"));

    public Task<ProviderVoidResult> GetVoidStatusAsync(PaymentIntent intent, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderVoidResult(ProviderVoidOutcome.Unknown, null, "provider_not_configured"));
}

public sealed class HttpPaymentProvider(
    HttpClient client,
    IOptions<PaymentProviderOptions> options,
    ILogger<HttpPaymentProvider>? logger = null) : IPaymentProvider
{
    private readonly ILogger<HttpPaymentProvider> logger = logger ?? NullLogger<HttpPaymentProvider>.Instance;

    public async Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        try
        {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, options.Value.AuthorizationPath,
            new ProviderAuthorizationRequest(intent.Id, intent.OrderId, intent.Amount, intent.Currency, intent.PaymentMethod));
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ProviderAuthorizationResult(false, null, $"provider_http_{(int)response.StatusCode}",
                IsUncertain(response.StatusCode) ? ProviderAuthorizationOutcome.Unknown : ProviderAuthorizationOutcome.Failed);
        ProviderAuthorizationResponse? result = await response.Content.ReadFromJsonAsync<ProviderAuthorizationResponse>(cancellationToken);
        if (result is { Succeeded: true } && !string.IsNullOrWhiteSpace(result.ProviderTransactionId))
            return new ProviderAuthorizationResult(true, result.ProviderTransactionId.Trim(), null);
        if (result is null) return new(false, null, "provider_response_invalid", ProviderAuthorizationOutcome.Unknown);
        return new(false, null, "provider_declined", ProviderAuthorizationOutcome.Declined);
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            LogUncertain("authorize", exception);
            return new(false, null, FailureCode(exception), ProviderAuthorizationOutcome.Unknown);
        }
    }

    public async Task<ProviderAuthorizationStatus> GetAuthorizationStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        try
        {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get,
            $"{options.Value.AuthorizationStatusPath.TrimEnd('/')}/{intent.Id:D}");
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new ProviderAuthorizationStatus(ProviderAuthorizationOutcome.Unknown, null, "provider_status_missing");
        if (!response.IsSuccessStatusCode)
            return new ProviderAuthorizationStatus(ProviderAuthorizationOutcome.Unknown, null, $"provider_http_{(int)response.StatusCode}");
        ProviderAuthorizationStatusResponse? result = await response.Content.ReadFromJsonAsync<ProviderAuthorizationStatusResponse>(cancellationToken);
        if (result is null) return new ProviderAuthorizationStatus(ProviderAuthorizationOutcome.Unknown, null, "provider_status_unknown");
        ProviderAuthorizationOutcome outcome = result.Status?.Trim().ToLowerInvariant() switch
        {
            "authorized" => ProviderAuthorizationOutcome.Authorized,
            "declined" => ProviderAuthorizationOutcome.Declined,
            "failed" => ProviderAuthorizationOutcome.Failed,
            _ => ProviderAuthorizationOutcome.Unknown
        };
        string? failureReason = outcome switch
        {
            ProviderAuthorizationOutcome.Declined => "provider_declined",
            ProviderAuthorizationOutcome.Failed => "provider_failed",
            ProviderAuthorizationOutcome.Unknown => "provider_status_unknown",
            _ => null
        };
        return new ProviderAuthorizationStatus(outcome, result.ProviderTransactionId?.Trim(), failureReason);
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            LogUncertain("authorization_status", exception);
            return new(ProviderAuthorizationOutcome.Unknown, null, FailureCode(exception));
        }
    }

    public async Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        try
        {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, options.Value.CapturePath,
            new ProviderCaptureRequest(intent.Id, intent.ProviderAuthorizationId!, intent.Amount, intent.Currency));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", intent.Id.ToString("D"));
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ProviderCaptureResult(IsUncertain(response.StatusCode)
                ? ProviderCaptureOutcome.Unknown : ProviderCaptureOutcome.Failed, null, $"provider_http_{(int)response.StatusCode}");
        ProviderCaptureResponse? result = await response.Content.ReadFromJsonAsync<ProviderCaptureResponse>(cancellationToken);
        return result is { Succeeded: true } && !string.IsNullOrWhiteSpace(result.ProviderTransactionId)
            ? new ProviderCaptureResult(ProviderCaptureOutcome.Captured, result.ProviderTransactionId, null)
            : result is null
                ? new ProviderCaptureResult(ProviderCaptureOutcome.Unknown, null, "provider_response_invalid")
                : new ProviderCaptureResult(ProviderCaptureOutcome.Failed, null, "provider_capture_failed");
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            LogUncertain("capture", exception);
            return new(ProviderCaptureOutcome.Unknown, null, FailureCode(exception));
        }
    }

    public async Task<ProviderCaptureResult> GetCaptureStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        try
        {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get,
            $"{options.Value.CaptureStatusPath.TrimEnd('/')}/{intent.Id:D}");
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new(ProviderCaptureOutcome.Unknown, null, "provider_capture_status_missing");
        if (!response.IsSuccessStatusCode)
            return new(ProviderCaptureOutcome.Unknown, null, $"provider_http_{(int)response.StatusCode}");
        ProviderCaptureStatusResponse? result = await response.Content.ReadFromJsonAsync<ProviderCaptureStatusResponse>(cancellationToken);
        ProviderCaptureOutcome outcome = result?.Status?.Trim().ToLowerInvariant() switch
        {
            "captured" when !string.IsNullOrWhiteSpace(result.ProviderTransactionId) => ProviderCaptureOutcome.Captured,
            "failed" => ProviderCaptureOutcome.Failed,
            _ => ProviderCaptureOutcome.Unknown
        };
        string? failureReason = outcome == ProviderCaptureOutcome.Unknown
            ? result?.FailureReason ?? "provider_capture_status_unknown"
            : result?.FailureReason;
        return new(outcome, outcome == ProviderCaptureOutcome.Captured ? result!.ProviderTransactionId!.Trim() : null,
            outcome == ProviderCaptureOutcome.Failed ? "provider_capture_failed" : failureReason);
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            LogUncertain("capture_status", exception);
            return new(ProviderCaptureOutcome.Unknown, null, FailureCode(exception));
        }
    }

    public async Task<ProviderVoidResult> VoidAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        try
        {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, options.Value.VoidPath,
            new ProviderVoidRequest(intent.Id, intent.ProviderAuthorizationId!));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"void:{intent.Id:D}");
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new(IsUncertain(response.StatusCode)
                ? ProviderVoidOutcome.Unknown : ProviderVoidOutcome.Failed, null, $"provider_http_{(int)response.StatusCode}");
        ProviderVoidResponse? result = await response.Content.ReadFromJsonAsync<ProviderVoidResponse>(cancellationToken);
        return result is { Succeeded: true } && !string.IsNullOrWhiteSpace(result.ProviderTransactionId)
            ? new(ProviderVoidOutcome.Voided, result.ProviderTransactionId.Trim(), null)
            : result is null
                ? new(ProviderVoidOutcome.Unknown, null, "provider_response_invalid")
                : new(ProviderVoidOutcome.Failed, null, "provider_void_failed");
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            LogUncertain("void", exception);
            return new(ProviderVoidOutcome.Unknown, null, FailureCode(exception));
        }
    }

    public async Task<ProviderVoidResult> GetVoidStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        try
        {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get,
            $"{options.Value.VoidStatusPath.TrimEnd('/')}/{intent.Id:D}");
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new(ProviderVoidOutcome.Unknown, null, "provider_void_status_missing");
        if (!response.IsSuccessStatusCode)
            return new(ProviderVoidOutcome.Unknown, null, $"provider_http_{(int)response.StatusCode}");
        ProviderVoidStatusResponse? result = await response.Content.ReadFromJsonAsync<ProviderVoidStatusResponse>(cancellationToken);
        ProviderVoidOutcome outcome = result?.Status?.Trim().ToLowerInvariant() switch
        {
            "voided" when !string.IsNullOrWhiteSpace(result.ProviderTransactionId) => ProviderVoidOutcome.Voided,
            "failed" => ProviderVoidOutcome.Failed,
            _ => ProviderVoidOutcome.Unknown
        };
        return new(outcome, outcome == ProviderVoidOutcome.Voided ? result!.ProviderTransactionId!.Trim() : null,
            outcome == ProviderVoidOutcome.Unknown ? "provider_void_status_unknown" : outcome == ProviderVoidOutcome.Failed ? "provider_void_failed" : null);
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            LogUncertain("void_status", exception);
            return new(ProviderVoidOutcome.Unknown, null, FailureCode(exception));
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(options.Value.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        return request;
    }

    private static bool IsUncertain(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests
        || statusCode >= System.Net.HttpStatusCode.InternalServerError;

    private static bool IsRecoverable(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or NotSupportedException
        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static string FailureCode(Exception exception) => exception switch
    {
        JsonException or NotSupportedException => "provider_response_invalid",
        OperationCanceledException => "provider_timeout",
        _ => "provider_transport_failure"
    };

    private void LogUncertain(string operation, Exception exception) => logger.LogWarning(
        "Payment provider {Operation} produced an uncertain outcome ({FailureCategory}); reconciliation is required",
        operation, FailureCode(exception));

    private sealed record ProviderAuthorizationRequest(Guid PaymentIntentId, Guid OrderId, decimal Amount, string Currency, string PaymentMethod);
    private sealed record ProviderAuthorizationResponse(bool Succeeded, string? ProviderTransactionId, string? FailureReason);
    private sealed record ProviderAuthorizationStatusResponse(string? Status, string? ProviderTransactionId, string? FailureReason);
    private sealed record ProviderCaptureRequest(Guid PaymentIntentId, string ProviderAuthorizationId, decimal Amount, string Currency);
    private sealed record ProviderCaptureResponse(bool Succeeded, string? ProviderTransactionId, string? FailureReason);
    private sealed record ProviderCaptureStatusResponse(string? Status, string? ProviderTransactionId, string? FailureReason);
    private sealed record ProviderVoidRequest(Guid PaymentIntentId, string ProviderAuthorizationId);
    private sealed record ProviderVoidResponse(bool Succeeded, string? ProviderTransactionId, string? FailureReason);
    private sealed record ProviderVoidStatusResponse(string? Status, string? ProviderTransactionId, string? FailureReason);
}

public sealed class PaymentProviderOptions
{
    public string Adapter { get; set; } = "Disabled";
    public string BaseUrl { get; set; } = "https://payment-provider.invalid/";
    public string AuthorizationPath { get; set; } = "v1/authorizations";
    public string AuthorizationStatusPath { get; set; } = "v1/authorizations";
    public string CapturePath { get; set; } = "v1/captures";
    public string CaptureStatusPath { get; set; } = "v1/captures";
    public string VoidPath { get; set; } = "v1/voids";
    public string VoidStatusPath { get; set; } = "v1/voids";
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
    public int MaximumAuthorizationAttempts { get; set; } = 3;
    public int MaximumCaptureRecoveryAttempts { get; set; } = 3;
    public int MaximumVoidRecoveryAttempts { get; set; } = 3;
    public TimeSpan RecoveryInterval { get; set; } = TimeSpan.FromSeconds(30);
    public bool CaptureRecoveryEnabled { get; set; } = true;
    public bool VoidRecoveryEnabled { get; set; } = true;
    public string? ApiKey { get; set; }
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
