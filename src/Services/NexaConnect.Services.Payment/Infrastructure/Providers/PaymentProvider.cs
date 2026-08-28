using System.Net.Http.Json;
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

public sealed class HttpPaymentProvider(HttpClient client, IOptions<PaymentProviderOptions> options) : IPaymentProvider
{
    public async Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            options.Value.AuthorizationPath,
            new ProviderAuthorizationRequest(intent.Id, intent.OrderId, intent.Amount, intent.Currency, intent.PaymentMethod),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ProviderAuthorizationResult(false, null, $"provider_http_{(int)response.StatusCode}",
                response.StatusCode is >= System.Net.HttpStatusCode.InternalServerError
                    ? ProviderAuthorizationOutcome.Unknown : ProviderAuthorizationOutcome.Declined);
        ProviderAuthorizationResponse? result = await response.Content.ReadFromJsonAsync<ProviderAuthorizationResponse>(cancellationToken);
        if (result is { Succeeded: true, ProviderTransactionId: not null })
            return new ProviderAuthorizationResult(true, result.ProviderTransactionId, null);
        string reason = result?.FailureReason ?? "Payment provider returned an unsuccessful response.";
        ProviderAuthorizationOutcome outcome = reason.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            ? ProviderAuthorizationOutcome.Unknown : ProviderAuthorizationOutcome.Declined;
        return new ProviderAuthorizationResult(false, null, reason, outcome);
    }

    public async Task<ProviderAuthorizationStatus> GetAuthorizationStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{options.Value.AuthorizationStatusPath.TrimEnd('/')}/{intent.Id:D}", cancellationToken);
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
        return new ProviderAuthorizationStatus(outcome, result.ProviderTransactionId, result.FailureReason);
    }

    public async Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, options.Value.CapturePath)
        {
            Content = JsonContent.Create(
                new ProviderCaptureRequest(intent.Id, intent.ProviderAuthorizationId!, intent.Amount, intent.Currency))
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", intent.Id.ToString("D"));
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ProviderCaptureResult(response.StatusCode is >= System.Net.HttpStatusCode.InternalServerError
                ? ProviderCaptureOutcome.Unknown : ProviderCaptureOutcome.Failed, null, $"provider_http_{(int)response.StatusCode}");
        ProviderCaptureResponse? result = await response.Content.ReadFromJsonAsync<ProviderCaptureResponse>(cancellationToken);
        return result is { Succeeded: true, ProviderTransactionId: not null }
            ? new ProviderCaptureResult(ProviderCaptureOutcome.Captured, result.ProviderTransactionId, null)
            : new ProviderCaptureResult(ProviderCaptureOutcome.Failed, null, result?.FailureReason ?? "provider_capture_failed");
    }

    public async Task<ProviderCaptureResult> GetCaptureStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{options.Value.CaptureStatusPath.TrimEnd('/')}/{intent.Id:D}", cancellationToken);
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
            failureReason);
    }

    public async Task<ProviderVoidResult> VoidAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, options.Value.VoidPath)
        {
            Content = JsonContent.Create(new ProviderVoidRequest(intent.Id, intent.ProviderAuthorizationId!))
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"void:{intent.Id:D}");
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new(response.StatusCode is >= System.Net.HttpStatusCode.InternalServerError
                ? ProviderVoidOutcome.Unknown : ProviderVoidOutcome.Failed, null, $"provider_http_{(int)response.StatusCode}");
        ProviderVoidResponse? result = await response.Content.ReadFromJsonAsync<ProviderVoidResponse>(cancellationToken);
        return result is { Succeeded: true, ProviderTransactionId: not null }
            ? new(ProviderVoidOutcome.Voided, result.ProviderTransactionId.Trim(), null)
            : new(ProviderVoidOutcome.Failed, null, result?.FailureReason ?? "provider_void_failed");
    }

    public async Task<ProviderVoidResult> GetVoidStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync($"{options.Value.VoidStatusPath.TrimEnd('/')}/{intent.Id:D}", cancellationToken);
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
            outcome == ProviderVoidOutcome.Unknown ? result?.FailureReason ?? "provider_void_status_unknown" : result?.FailureReason);
    }

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
}
