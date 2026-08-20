using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Application.Intents;

namespace NexaConnect.Services.Payment.Infrastructure.Providers;

public sealed record ProviderAuthorizationResult(bool Succeeded, string? ProviderTransactionId, string? FailureReason);

public interface IPaymentProvider
{
    Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken);
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
            return new ProviderAuthorizationResult(false, null, $"provider_http_{(int)response.StatusCode}");
        ProviderAuthorizationResponse? result = await response.Content.ReadFromJsonAsync<ProviderAuthorizationResponse>(cancellationToken);
        return result is { Succeeded: true, ProviderTransactionId: not null }
            ? new ProviderAuthorizationResult(true, result.ProviderTransactionId, null)
            : new ProviderAuthorizationResult(false, null, result?.FailureReason ?? "Payment provider returned an unsuccessful response.");
    }

    private sealed record ProviderAuthorizationRequest(Guid PaymentIntentId, Guid OrderId, decimal Amount, string Currency, string PaymentMethod);
    private sealed record ProviderAuthorizationResponse(bool Succeeded, string? ProviderTransactionId, string? FailureReason);
}

public sealed class PaymentProviderOptions
{
    public string BaseUrl { get; set; } = "https://payment-provider.invalid/";
    public string AuthorizationPath { get; set; } = "v1/authorizations";
}
