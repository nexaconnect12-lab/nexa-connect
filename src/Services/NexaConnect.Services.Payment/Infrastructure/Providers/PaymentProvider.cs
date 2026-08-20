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

public interface IPaymentProvider
{
    Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken);
    Task<ProviderAuthorizationStatus> GetAuthorizationStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => Task.FromResult(new ProviderAuthorizationStatus(ProviderAuthorizationOutcome.Unknown, null, "provider_status_unavailable"));
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

    private sealed record ProviderAuthorizationRequest(Guid PaymentIntentId, Guid OrderId, decimal Amount, string Currency, string PaymentMethod);
    private sealed record ProviderAuthorizationResponse(bool Succeeded, string? ProviderTransactionId, string? FailureReason);
    private sealed record ProviderAuthorizationStatusResponse(string? Status, string? ProviderTransactionId, string? FailureReason);
}

public sealed class PaymentProviderOptions
{
    public string BaseUrl { get; set; } = "https://payment-provider.invalid/";
    public string AuthorizationPath { get; set; } = "v1/authorizations";
    public string AuthorizationStatusPath { get; set; } = "v1/authorizations";
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
    public int MaximumAuthorizationAttempts { get; set; } = 3;
    public TimeSpan RecoveryInterval { get; set; } = TimeSpan.FromSeconds(30);
}
