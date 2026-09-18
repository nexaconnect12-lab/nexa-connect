using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Application.Intents;

namespace NexaConnect.Services.Payment.Infrastructure.Providers;

/// <summary>Omise test-account card adapter. Provider payloads and credentials never enter logs or persistence.</summary>
public sealed class OmisePaymentProvider : IPaymentProvider
{
    private readonly HttpClient client;
    private readonly string secret;
    private readonly ILogger<OmisePaymentProvider> logger;
    private const int MaximumResponseBytes = 65536;
    private const string FullCaptureMode = "final_auth_v1";

    public OmisePaymentProvider(HttpClient client, IOptions<PaymentProviderOptions> options,
        ILogger<OmisePaymentProvider> logger)
    {
        if (client.BaseAddress?.AbsoluteUri != "https://api.omise.co/" || !IsTestSecret(options.Value.OmiseSecretKey))
            throw new InvalidOperationException("Omise requires its official HTTPS endpoint and a test secret key.");
        this.client = client;
        secret = options.Value.OmiseSecretKey!;
        this.logger = logger;
    }

    public static bool IsTestSecret(string? value) => Regex.IsMatch(value ?? "", "^skey_test_[a-z0-9]{10,64}$");

    /// <summary>One GET for operator diagnostics; this neither mutates nor reconciles a local intent.</summary>
    public async Task<OmiseChargeInspection> InspectTestChargeAsync(string chargeId, decimal expectedAmount,
        CancellationToken cancellationToken)
    {
        if (!ValidChargeId(chargeId)) throw new ArgumentException("A valid charge reference is required.");
        if (expectedAmount <= 0 || expectedAmount > 10000 || decimal.Truncate(expectedAmount * 100) != expectedAmount * 100)
            throw new ArgumentException("An exact two-decimal THB amount between 0.01 and 10000 is required.");
        WireResult wire = await SendAsync(HttpMethod.Get, $"charges/{chargeId}", null, cancellationToken);
        if (wire.Charge is not { Object: "charge", LiveMode: false } charge || charge.Id != chargeId)
            return new() { FailureCategory = wire.Failure ?? "provider_charge_mismatch" };
        bool metadataPresent = new[] { "nexa_intent_id", "nexa_organization_id", "nexa_order_id" }
            .All(name => Guid.TryParse(charge.Metadata?.GetValueOrDefault(name), out Guid id) && id != Guid.Empty);
        long expectedMinor = (long)(expectedAmount * 100);
        bool currencyMatches = string.Equals(charge.Currency, "THB", StringComparison.OrdinalIgnoreCase);
        bool knownStatus = charge.Status is "pending" or "successful" or "failed" or "expired" or "reversed";
        return new()
        {
            ReadSucceeded = true, Status = knownStatus ? charge.Status : null,
            AuthorizationType = charge.AuthorizationType is "final_auth" or "pre_auth" ? charge.AuthorizationType : null,
            TrustedFullCaptureContextVerified = HasTrustedFullCaptureContext(charge),
            Paid = charge.Paid, Authorized = charge.Authorized, Reversed = charge.Reversed,
            Capturable = charge.Capturable, Capture = charge.Capture, Reversible = charge.Reversible,
            AmountSatang = charge.Amount is >= 0 ? charge.Amount : null,
            CapturedAmountSatang = charge.CapturedAmount is >= 0 ? charge.CapturedAmount : null,
            RefundedAmountSatang = charge.RefundedAmount is >= 0 ? charge.RefundedAmount : null,
            ExpectedAmountMatches = charge.Amount == expectedMinor,
            CurrencyIsThb = currencyMatches, MetadataUuidFieldsPresent = metadataPresent,
            CaptureFinancialFieldsConfirmed = FullCaptureConfirmed(charge)
                && charge.Authorized is not null && charge.Amount == expectedMinor && currencyMatches && knownStatus
        };
    }
    public void ValidateAuthorizationInput(string? cardToken)
    {
        if (!Regex.IsMatch(cardToken ?? "", "^tokn_test_[a-z0-9]{10,64}$"))
            throw new ArgumentException("A valid Omise test card token is required.");
    }

    public Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => AuthorizeAsync(intent, null, cancellationToken);

    public async Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, string? cardToken, CancellationToken cancellationToken)
    {
        ValidateAuthorizationInput(cardToken);
        long? amount = MinorAmount(intent);
        if (amount is null) return new(false, null, "provider_request_invalid", ProviderAuthorizationOutcome.Failed);
        var fields = new Dictionary<string, string>
        {
            ["amount"] = amount.Value.ToString(CultureInfo.InvariantCulture), ["currency"] = "thb",
            ["capture"] = "false", ["authorization_type"] = "final_auth", ["card"] = cardToken!,
            ["metadata[nexa_intent_id]"] = intent.Id.ToString("D"),
            ["metadata[nexa_organization_id]"] = intent.OrganizationId.ToString("D"),
            ["metadata[nexa_order_id]"] = intent.OrderId.ToString("D"),
            ["metadata[nexa_capture_mode]"] = FullCaptureMode,
            ["metadata[nexa_capture_proof]"] = Convert.ToHexStringLower(FullCaptureProof(intent.Id, intent.OrganizationId, intent.OrderId, amount.Value))
        };
        WireResult wire = await SendAsync(HttpMethod.Post, "charges", fields, cancellationToken);
        ProviderAuthorizationStatus status = Authorization(wire.Charge, intent, wire.Failure);
        return new(status.Outcome == ProviderAuthorizationOutcome.Authorized, status.ProviderTransactionId, status.FailureReason, status.Outcome);
    }

    public async Task<ProviderAuthorizationStatus> GetAuthorizationStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        WireResult wire = await ResolveAsync(intent, cancellationToken);
        return Authorization(wire.Charge, intent, wire.Failure);
    }

    public Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => CaptureAsync(intent, true, cancellationToken);
    public Task<ProviderCaptureResult> GetCaptureStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => CaptureAsync(intent, false, cancellationToken);
    private async Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent, bool command, CancellationToken cancellationToken)
    {
        WireResult wire = await ResolveAsync(intent, cancellationToken);
        if (!Matches(wire.Charge, intent)) return new(ProviderCaptureOutcome.Unknown, null, wire.Failure ?? "provider_charge_mismatch");
        Charge charge = wire.Charge!;
        if (FullCaptureConfirmed(charge))
            return new(ProviderCaptureOutcome.Captured, charge.Id, null);
        if (charge.Paid == true) return new(ProviderCaptureOutcome.Unknown, null, "provider_capture_status_unknown");
        if (charge.Reversed == true || charge.Status is "failed" or "expired")
            return new(ProviderCaptureOutcome.Failed, null, "provider_capture_failed");
        if (!command || charge.Authorized != true || charge.Capturable != true)
            return new(ProviderCaptureOutcome.Unknown, null, "provider_capture_status_unknown");
        wire = await SendAsync(HttpMethod.Post, $"charges/{charge.Id}/capture",
            new() { ["capture_amount"] = MinorAmount(intent)!.Value.ToString(CultureInfo.InvariantCulture) }, cancellationToken);
        if (!Matches(wire.Charge, intent) || wire.Charge!.Id != charge.Id) return new(ProviderCaptureOutcome.Unknown, null, wire.Failure ?? "provider_charge_mismatch");
        return FullCaptureConfirmed(wire.Charge!)
            ? new(ProviderCaptureOutcome.Captured, wire.Charge.Id, null)
            : wire.Charge.Paid == false && wire.Charge.Status is "failed" or "expired" ? new(ProviderCaptureOutcome.Failed, null, "provider_capture_failed")
            : new(ProviderCaptureOutcome.Unknown, null, "provider_capture_status_unknown");
    }

    private bool FullCaptureConfirmed(Charge charge)
    {
        if (charge.Paid != true || charge.Reversed != false || charge.RefundedAmount != 0 || charge.Amount is not > 0)
            return false;
        if (charge.CapturedAmount is not null) return charge.CapturedAmount == charge.Amount;
        // Omise's final_auth contract permits exactly one full capture. Never infer this
        // from omitted authorization type, nor override an explicit partial captured amount.
        bool finalAuthorization = charge.AuthorizationType == "final_auth"
            || charge.AuthorizationType is null && HasTrustedFullCaptureContext(charge);
        return finalAuthorization && charge.Status == "successful"
            && charge.Authorized == true && charge.Capture == false
            && charge.Capturable == false && charge.Reversible == false;
    }

    private byte[] FullCaptureProof(Guid intent, Guid organization, Guid order, long amount)
    {
        string context = $"NexaConnect/Omise/FullCapture/v1\n{intent:D}\n{organization:D}\n{order:D}\n{amount.ToString(CultureInfo.InvariantCulture)}\nthb\n{FullCaptureMode}";
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(context));
    }

    private bool HasTrustedFullCaptureContext(Charge charge)
    {
        if (charge.Metadata?.GetValueOrDefault("nexa_capture_mode") != FullCaptureMode
            || charge.Amount is not > 0 || !string.Equals(charge.Currency, "THB", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(charge.Metadata.GetValueOrDefault("nexa_intent_id"), "D", out Guid intent) || intent == Guid.Empty
            || !Guid.TryParseExact(charge.Metadata.GetValueOrDefault("nexa_organization_id"), "D", out Guid organization) || organization == Guid.Empty
            || !Guid.TryParseExact(charge.Metadata.GetValueOrDefault("nexa_order_id"), "D", out Guid order) || order == Guid.Empty)
            return false;
        string? proof = charge.Metadata.GetValueOrDefault("nexa_capture_proof");
        if (!Regex.IsMatch(proof ?? "", "\\A[a-f0-9]{64}\\z")) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(proof!),
            FullCaptureProof(intent, organization, order, charge.Amount.Value));
    }

    public Task<ProviderVoidResult> VoidAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => VoidAsync(intent, true, cancellationToken);
    public Task<ProviderVoidResult> GetVoidStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        => VoidAsync(intent, false, cancellationToken);
    private async Task<ProviderVoidResult> VoidAsync(PaymentIntent intent, bool command, CancellationToken cancellationToken)
    {
        WireResult wire = await ResolveAsync(intent, cancellationToken);
        if (!Matches(wire.Charge, intent)) return new(ProviderVoidOutcome.Unknown, null, wire.Failure ?? "provider_charge_mismatch");
        Charge charge = wire.Charge!;
        if (charge.Paid == true) return new(ProviderVoidOutcome.Failed, null, "provider_void_requires_refund");
        if (charge.Reversed == true) return new(ProviderVoidOutcome.Voided, charge.Id, null);
        if (!command || charge.Authorized != true || charge.Reversible != true)
            return new(ProviderVoidOutcome.Unknown, null, "provider_void_status_unknown");
        wire = await SendAsync(HttpMethod.Post, $"charges/{charge.Id}/reverse", null, cancellationToken);
        if (!Matches(wire.Charge, intent) || wire.Charge!.Id != charge.Id) return new(ProviderVoidOutcome.Unknown, null, wire.Failure ?? "provider_charge_mismatch");
        return wire.Charge!.Reversed == true && wire.Charge.Paid == false
            ? new(ProviderVoidOutcome.Voided, wire.Charge.Id, null)
            : new(ProviderVoidOutcome.Unknown, null, "provider_void_status_unknown");
    }

    private async Task<WireResult> ResolveAsync(PaymentIntent intent, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(intent.ProviderAuthorizationId))
        {
            if (!ValidChargeId(intent.ProviderAuthorizationId)) return new(null, "provider_charge_mismatch");
            WireResult result = await SendAsync(HttpMethod.Get, $"charges/{intent.ProviderAuthorizationId}", null, cancellationToken);
            return result.Charge?.Id == intent.ProviderAuthorizationId ? result : new(null, result.Failure ?? "provider_charge_mismatch");
        }
        // Search is eventually consistent. A missing or ambiguous result stays unknown;
        // recovery never creates a replacement charge or repeats authorization.
        WireResult search = await SendAsync(HttpMethod.Get,
            $"search?scope=charge&query={intent.Id:D}&per_page=100&page=1", null, cancellationToken, search: true);
        if (search.Failure is not null) return search;
        if (search.Search?.TotalPages != 1 || search.Search.Data is null)
            return new(null, "provider_status_unknown");
        if (search.Search.Data.Any(charge => charge is null)) return new(null, "provider_response_invalid");
        Charge[] candidates = search.Search.Data.Where(charge => charge.Metadata?.GetValueOrDefault("nexa_intent_id") == intent.Id.ToString("D")).ToArray();
        if (candidates.Length != 1 || !Matches(candidates[0], intent)) return new(null, "provider_status_unknown");
        WireResult fresh = await SendAsync(HttpMethod.Get, $"charges/{candidates[0].Id}", null, cancellationToken);
        return fresh.Charge?.Id == candidates[0].Id ? fresh : new(null, "provider_charge_mismatch");
    }

    private static ProviderAuthorizationStatus Authorization(Charge? charge, PaymentIntent intent, string? failure)
    {
        if (!Matches(charge, intent)) return new(ProviderAuthorizationOutcome.Unknown, null, failure ?? "provider_charge_mismatch");
        if (charge!.Paid == true) return new(ProviderAuthorizationOutcome.Unknown, null, "provider_status_unknown");
        if (charge!.Status is "failed" or "expired" || charge.Reversed == true)
            return new(ProviderAuthorizationOutcome.Failed, null, "provider_declined");
        // Already-paid or 3DS/pending charges are not an uncaptured authorization.
        if (charge.Authorized == true && charge.Paid == false && charge.Reversed == false && charge.Capturable == true && charge.Capture == false)
            return new(ProviderAuthorizationOutcome.Authorized, charge.Id, null);
        return new(ProviderAuthorizationOutcome.Unknown, null, "provider_status_unknown");
    }

    private static long? MinorAmount(PaymentIntent intent)
    {
        if (intent.Id == Guid.Empty || intent.OrganizationId == Guid.Empty || intent.OrderId == Guid.Empty
            || intent.PaymentMethod != "card" || intent.Currency != "THB" || intent.Amount <= 0
            || intent.Amount > long.MaxValue / 100m || decimal.Truncate(intent.Amount * 100m) != intent.Amount * 100m) return null;
        return (long)(intent.Amount * 100m);
    }
    private static bool ValidChargeId(string? id) => Regex.IsMatch(id ?? "", "^chrg_(test_)?[a-z0-9]{10,64}$");
    private static bool Matches(Charge? charge, PaymentIntent intent) => charge is { Object: "charge", LiveMode: false }
        && charge.Paid is not null && charge.Authorized is not null && charge.Reversed is not null
        && charge.Status is "pending" or "successful" or "failed" or "expired" or "reversed"
        && ValidChargeId(charge.Id) && MinorAmount(intent) is long minor && charge.Amount == minor
        && string.Equals(charge.Currency, "THB", StringComparison.OrdinalIgnoreCase) && charge.Metadata?.GetValueOrDefault("nexa_intent_id") == intent.Id.ToString("D")
        && charge.Metadata.GetValueOrDefault("nexa_organization_id") == intent.OrganizationId.ToString("D")
        && charge.Metadata.GetValueOrDefault("nexa_order_id") == intent.OrderId.ToString("D");

    private async Task<WireResult> SendAsync(HttpMethod method, string path, Dictionary<string, string>? fields,
        CancellationToken cancellationToken, bool search = false)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(secret + ":")));
            request.Headers.Add("Omise-Version", "2019-05-29");
            if (method == HttpMethod.Post) request.Content = new FormUrlEncodedContent(fields ?? []);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // HTTP errors cannot establish whether a financial operation committed.
                string category = $"provider_http_{(int)response.StatusCode}";
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    // Retain only known diagnostic categories, never provider messages or payloads.
                    // A rejection code does not prove that a financial operation did not commit.
                    try
                    {
                        byte[] body = await ReadBoundedBodyAsync(response, cancellationToken);
                        ProviderError? error = JsonSerializer.Deserialize<ProviderError>(body);
                        if (error?.Object == "error") category = error.Code switch
                        {
                            "used_token" => "provider_token_used",
                            "invalid_card_token" => "provider_card_token_invalid",
                            "invalid_charge" => "provider_charge_invalid",
                            "invalid_amount" => "provider_amount_invalid",
                            "feature_not_supported" => "provider_feature_unsupported",
                            "brand_not_supported" => "provider_brand_unsupported",
                            "failed_multi_currency" => "provider_multicurrency_failed",
                            "backend_error" => "provider_backend_error",
                            "bad_request" => "provider_bad_request",
                            "expired_charge" => "provider_charge_expired",
                            "failed_capture" => "provider_capture_failed",
                            "failed_reverse" => "provider_void_failed",
                            _ => category
                        };
                    }
                    catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
                        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                    {
                        // Malformed/oversized/unreadable error bodies retain the HTTP category.
                    }
                }
                logger.LogWarning("Omise {Operation} boundary returned {FailureCategory}; reconciliation required", method.Method, category);
                return new(null, category);
            }
            byte[] bytes = await ReadBoundedBodyAsync(response, cancellationToken);
            return search
                ? new(null, null, JsonSerializer.Deserialize<SearchResult>(bytes))
                : new(JsonSerializer.Deserialize<Charge>(bytes), null);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            string category = exception is JsonException ? "provider_response_invalid" : exception is OperationCanceledException ? "provider_timeout" : "provider_transport_failure";
            logger.LogWarning("Omise {Operation} boundary returned {FailureCategory}; reconciliation required", method.Method, category);
            return new(null, category);
        }
    }

    private async Task<byte[]> ReadBoundedBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(client.Timeout);
        await using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        byte[] bytes = new byte[MaximumResponseBytes + 1];
        int count = 0, read;
        while (count < bytes.Length && (read = await stream.ReadAsync(bytes.AsMemory(count), deadline.Token)) > 0) count += read;
        if (count > MaximumResponseBytes) throw new JsonException();
        return bytes.AsSpan(0, count).ToArray();
    }

    private sealed class ProviderError
    {
        [JsonPropertyName("object")] public string? Object { get; init; }
        [JsonPropertyName("code")] public string? Code { get; init; }
    }

    private sealed record WireResult(Charge? Charge, string? Failure, SearchResult? Search = null);
    private sealed class SearchResult
    {
        [JsonPropertyName("total_pages")] public int? TotalPages { get; init; }
        [JsonPropertyName("data")] public Charge[]? Data { get; init; }
    }
    private sealed class Charge
    {
        [JsonPropertyName("object")] public string? Object { get; init; }
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("livemode")] public bool? LiveMode { get; init; }
        [JsonPropertyName("amount")] public long? Amount { get; init; }
        [JsonPropertyName("currency")] public string? Currency { get; init; }
        [JsonPropertyName("status")] public string? Status { get; init; }
        [JsonPropertyName("authorized")] public bool? Authorized { get; init; }
        [JsonPropertyName("paid")] public bool? Paid { get; init; }
        [JsonPropertyName("reversed")] public bool? Reversed { get; init; }
        [JsonPropertyName("capturable")] public bool? Capturable { get; init; }
        [JsonPropertyName("capture")] public bool? Capture { get; init; }
        [JsonPropertyName("authorization_type")] public string? AuthorizationType { get; init; }
        [JsonPropertyName("reversible")] public bool? Reversible { get; init; }
        [JsonPropertyName("refunded_amount")] public long? RefundedAmount { get; init; }
        [JsonPropertyName("captured_amount")] public long? CapturedAmount { get; init; }
        [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; init; }
    }
}

/// <summary>Explicit operator report, not telemetry or proof of local Payment ownership. Null fields remain unknown.</summary>
public sealed record OmiseChargeInspection
{
    public bool ReadSucceeded { get; init; }
    public string? FailureCategory { get; init; }
    public string? Status { get; init; }
    public string? AuthorizationType { get; init; }
    public bool TrustedFullCaptureContextVerified { get; init; }
    public bool? Paid { get; init; }
    public bool? Authorized { get; init; }
    public bool? Reversed { get; init; }
    public bool? Capturable { get; init; }
    public bool? Capture { get; init; }
    public bool? Reversible { get; init; }
    public long? AmountSatang { get; init; }
    public long? CapturedAmountSatang { get; init; }
    public long? RefundedAmountSatang { get; init; }
    public bool ExpectedAmountMatches { get; init; }
    public bool CurrencyIsThb { get; init; }
    public bool MetadataUuidFieldsPresent { get; init; }
    public bool CaptureFinancialFieldsConfirmed { get; init; }
}
