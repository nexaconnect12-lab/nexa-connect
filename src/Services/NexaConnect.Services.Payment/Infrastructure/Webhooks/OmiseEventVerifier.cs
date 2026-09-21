using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Application.Webhooks;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.Services.Payment.Infrastructure.Webhooks;

public sealed class OmiseEventVerifier(HttpClient client, IOptions<PaymentProviderOptions> options, ILogger<OmiseEventVerifier>? logger = null) : IOmiseEventVerifier
{
    private OmiseEventLookup Failure(bool retry)
    {
        logger?.LogWarning("Omise event verification rejected or unavailable; retryable {Retryable}",retry);
        return new(null,retry);
    }
    public async Task<OmiseEventLookup> VerifyAsync(string eventId, CancellationToken token)
    {
        using var suppression = OpenTelemetry.SuppressInstrumentationScope.Begin();
        if (!OmiseWebhookIdentity.Valid(eventId) || client.BaseAddress?.AbsoluteUri != "https://api.omise.co/"
            || !OmisePaymentProvider.IsTestSecret(options.Value.OmiseSecretKey)) return Failure(false);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(options.Value.RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, "events/" + eventId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Value.OmiseSecretKey + ":")));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return Failure(response.StatusCode != HttpStatusCode.NotFound);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            byte[] bytes = new byte[65537]; int count = 0, read;
            while (count < bytes.Length && (read = await stream.ReadAsync(bytes.AsMemory(count), deadline.Token)) > 0) count += read;
            if (count > 65536) return Failure(false);
            using var json = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 32 });
            var root = json.RootElement;
            if (root.GetProperty("object").GetString() != "event" || root.GetProperty("id").GetString() != eventId
                || root.GetProperty("livemode").ValueKind != JsonValueKind.False
                || root.GetProperty("key").GetString() is not ("charge.create" or "charge.capture" or "charge.complete" or "charge.reverse" or "charge.update"))
                return Failure(false);
            var charge = root.GetProperty("data");
            if (charge.GetProperty("object").GetString() != "charge" || charge.GetProperty("livemode").ValueKind != JsonValueKind.False)
                return Failure(false);
            string? chargeId = charge.GetProperty("id").GetString();
            if (!System.Text.RegularExpressions.Regex.IsMatch(chargeId ?? "", @"\Achrg_(test_)?[a-z0-9]{10,64}\z")) return Failure(false);
            var metadata = charge.GetProperty("metadata");
            Guid org = Guid.Parse(metadata.GetProperty("nexa_organization_id").GetString()!);
            Guid intent = Guid.Parse(metadata.GetProperty("nexa_intent_id").GetString()!);
            Guid order = Guid.Parse(metadata.GetProperty("nexa_order_id").GetString()!);
            long amount = charge.GetProperty("amount").GetInt64();
            string? currencyValue = charge.GetProperty("currency").GetString();
            if (currencyValue is null) return Failure(false);
            string currency = currencyValue.ToUpperInvariant();
            return org == Guid.Empty || intent == Guid.Empty || order == Guid.Empty || amount <= 0 || currency != "THB"
                ? Failure(false) : new(new(org, intent, order, chargeId!, amount, currency), false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
            || exception is OperationCanceledException && !token.IsCancellationRequested) { return Failure(true); }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentNullException) { return Failure(false); }
    }
}
