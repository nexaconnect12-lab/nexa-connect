using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.UnitTests;

public sealed class OmisePaymentProviderTests
{
    private const string Token = "tokn_test_abcdefghijklmnopqrstuvwxyz";
    private const string ChargeId = "chrg_test_abcdefghijklmnopqrstuvwxyz";
    private static PaymentIntent Intent() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), 20.50m, "THB", "card", "authorizing", DateTimeOffset.UtcNow);
    private static Dictionary<string, object?> Charge(PaymentIntent intent, bool paid = false, bool reversed = false) => new()
    {
        ["object"] = "charge", ["id"] = ChargeId, ["livemode"] = false, ["amount"] = 2050,
        ["currency"] = "THB", ["status"] = reversed ? "reversed" : paid ? "successful" : "pending",
        ["authorized"] = true, ["paid"] = paid, ["reversed"] = reversed,
        ["capturable"] = !paid && !reversed, ["reversible"] = !paid && !reversed,
        ["capture"] = false,
        ["refunded_amount"] = 0, ["captured_amount"] = paid ? 2050 : 0,
        ["metadata"] = new Dictionary<string, string>
        {
            ["nexa_intent_id"] = intent.Id.ToString("D"), ["nexa_organization_id"] = intent.OrganizationId.ToString("D"),
            ["nexa_order_id"] = intent.OrderId.ToString("D")
        }
    };
    private static (OmisePaymentProvider Provider, StubHandler Handler) Provider(params object[] responses)
    {
        var handler = new StubHandler(responses);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.omise.co/"), Timeout = TimeSpan.FromSeconds(1) };
        return (new OmisePaymentProvider(client, Options.Create(new PaymentProviderOptions
        { OmiseSecretKey = "skey_test_abcdefghijklmnopqrstuvwxyz" }), NullLogger<OmisePaymentProvider>.Instance), handler);
    }

    [Theory]
    [InlineData("THB")]
    [InlineData("thb")]
    public async Task Authorization_uses_basic_auth_minor_units_deferred_capture_and_exact_metadata(string currency)
    {
        PaymentIntent intent = Intent();
        var charge = Charge(intent);
        charge["currency"] = currency;
        var (provider, handler) = Provider(charge);
        ProviderAuthorizationResult result = await provider.AuthorizeAsync(intent, Token, default);
        Assert.True(result.Succeeded);
        Assert.Equal(ChargeId, result.ProviderTransactionId);
        Assert.Single(handler.Requests);
        Assert.Equal("POST /charges", handler.Requests[0]);
        Assert.True(handler.BasicAuthValidated); Assert.True(handler.VersionValidated);
        string form = Uri.UnescapeDataString(handler.Body!);
        Assert.Contains("amount=2050", form); Assert.Contains("currency=thb", form); Assert.Contains("capture=false", form);
        Assert.Contains("authorization_type=final_auth", form);
        Assert.Contains("metadata[nexa_capture_mode]=final_auth_v1", form);
        Assert.Contains("metadata[nexa_capture_proof]=", form);
        Assert.Contains("metadata[nexa_intent_id]=" + intent.Id.ToString("D"), form);
    }

    [Theory]
    [InlineData("live")]
    [InlineData("tenant")]
    [InlineData("order")]
    [InlineData("intent")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("id")]
    [InlineData("missing_flags")]
    [InlineData("unknown_status")]
    [InlineData("already_paid")]
    public async Task Authorization_does_not_accept_unowned_or_unsafe_provider_state(string mismatch)
    {
        PaymentIntent intent = Intent(); var charge = Charge(intent);
        switch (mismatch)
        {
            case "live": charge["livemode"] = true; break;
            case "tenant": ((Dictionary<string, string>)charge["metadata"]!)["nexa_organization_id"] = Guid.NewGuid().ToString("D"); break;
            case "order": ((Dictionary<string, string>)charge["metadata"]!)["nexa_order_id"] = Guid.NewGuid().ToString("D"); break;
            case "intent": ((Dictionary<string, string>)charge["metadata"]!)["nexa_intent_id"] = Guid.NewGuid().ToString("D"); break;
            case "amount": charge["amount"] = 1; break;
            case "currency": charge["currency"] = "usd"; break;
            case "id": charge["id"] = "../../bad"; break;
            case "missing_flags": charge.Remove("paid"); break;
            case "unknown_status": charge["status"] = "unexpected"; break;
            case "already_paid": charge["paid"] = true; break;
        }
        var (provider, _) = Provider(charge);
        Assert.Equal(ProviderAuthorizationOutcome.Unknown, (await provider.AuthorizeAsync(intent, Token, default)).EffectiveOutcome);
    }

    [Fact]
    public async Task Lost_authorization_response_is_recovered_by_search_then_fresh_charge_lookup_without_post()
    {
        PaymentIntent intent = Intent(); var charge = Charge(intent);
        var (provider, handler) = Provider(new { total_pages = 1, data = new[] { charge } }, charge);
        Assert.Equal(ProviderAuthorizationOutcome.Authorized, (await provider.GetAuthorizationStatusAsync(intent, default)).Outcome);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request));
        Assert.Contains("scope=charge", handler.Requests[0]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public async Task Missing_ambiguous_or_paginated_search_stays_unknown(int pages, int count)
    {
        PaymentIntent intent = Intent();
        var (provider, handler) = Provider(new { total_pages = pages, data = Enumerable.Range(0, count).Select(_ => Charge(intent)).ToArray() });
        Assert.Equal(ProviderAuthorizationOutcome.Unknown, (await provider.GetAuthorizationStatusAsync(intent, default)).Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Null_search_entries_stay_unknown_without_throwing_or_repeating_authorization()
    {
        PaymentIntent intent = Intent();
        var (provider, handler) = Provider(new { total_pages = 1, data = new object?[] { null } });
        Assert.Equal(ProviderAuthorizationOutcome.Unknown, (await provider.GetAuthorizationStatusAsync(intent, default)).Outcome);
        Assert.Single(handler.Requests);
        Assert.StartsWith("GET ", handler.Requests[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_and_void_use_real_charge_endpoints_and_lookup_replay_without_second_command(bool reversal)
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        var final = Charge(intent, paid: !reversal, reversed: reversal);
        var (provider, handler) = Provider(Charge(intent), final, final, final);
        if (reversal)
        {
            Assert.Equal(ProviderVoidOutcome.Voided, (await provider.VoidAsync(intent, default)).Outcome);
            Assert.Equal(ProviderVoidOutcome.Voided, (await provider.VoidAsync(intent, default)).Outcome);
            Assert.Equal(ProviderVoidOutcome.Voided, (await provider.GetVoidStatusAsync(intent, default)).Outcome);
        }
        else
        {
            Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.CaptureAsync(intent, default)).Outcome);
            Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.CaptureAsync(intent, default)).Outcome);
            Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.GetCaptureStatusAsync(intent, default)).Outcome);
        }
        Assert.Single(handler.Requests, request => request.StartsWith("POST "));
        Assert.Contains("POST /charges/" + ChargeId + (reversal ? "/reverse" : "/capture"), handler.Requests);
    }

    [Theory]
    [InlineData(401)] [InlineData(404)] [InlineData(408)] [InlineData(429)] [InlineData(500)] [InlineData(503)]
    public async Task Http_errors_are_uncertain_and_never_automatically_retried(int status)
    {
        PaymentIntent intent = Intent(); var (provider, handler) = Provider((HttpStatusCode)status);
        Assert.Equal(ProviderAuthorizationOutcome.Unknown, (await provider.AuthorizeAsync(intent, Token, default)).EffectiveOutcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Malformed_success_and_transport_loss_stay_unknown()
    {
        PaymentIntent intent = Intent();
        foreach (object response in new object[] { "{malformed", new HttpRequestException(), new TaskCanceledException(), new string('x', 65537) })
        {
            var (provider, handler) = Provider(response);
            Assert.Equal(ProviderAuthorizationOutcome.Unknown, (await provider.AuthorizeAsync(intent, Token, default)).EffectiveOutcome);
            Assert.Single(handler.Requests);
        }
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("refund")]
    public async Task Capture_does_not_confirm_partial_or_refunded_charge(string state)
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId }; var charge = Charge(intent, paid: true);
        charge[state == "partial" ? "captured_amount" : "refunded_amount"] = 100;
        var (provider, _) = Provider(charge);
        Assert.Equal(ProviderCaptureOutcome.Unknown, (await provider.GetCaptureStatusAsync(intent, default)).Outcome);
    }

    [Fact]
    public async Task Captured_payment_cannot_be_reversed()
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId }; var (provider, handler) = Provider(Charge(intent, paid: true));
        Assert.Equal(ProviderVoidOutcome.Failed, (await provider.VoidAsync(intent, default)).Outcome);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_capture_or_reverse_recovers_by_status_without_repeating_post(bool reversal)
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        var (provider, handler) = Provider(Charge(intent), new HttpRequestException(), Charge(intent, paid: !reversal, reversed: reversal));
        if (reversal)
        {
            Assert.Equal(ProviderVoidOutcome.Unknown, (await provider.VoidAsync(intent, default)).Outcome);
            Assert.Equal(ProviderVoidOutcome.Voided, (await provider.GetVoidStatusAsync(intent, default)).Outcome);
        }
        else
        {
            Assert.Equal(ProviderCaptureOutcome.Unknown, (await provider.CaptureAsync(intent, default)).Outcome);
            Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.GetCaptureStatusAsync(intent, default)).Outcome);
        }
        Assert.Single(handler.Requests, request => request.StartsWith("POST "));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_charge_reference_in_command_response_cannot_confirm_result(bool reversal)
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        var final = Charge(intent, paid: !reversal, reversed: reversal); final["id"] = "chrg_test_zyxwvutsrqponmlkjihgfedcba";
        var (provider, _) = Provider(Charge(intent), final);
        if (reversal) Assert.Equal(ProviderVoidOutcome.Unknown, (await provider.VoidAsync(intent, default)).Outcome);
        else Assert.Equal(ProviderCaptureOutcome.Unknown, (await provider.CaptureAsync(intent, default)).Outcome);
    }

    [Fact]
    public async Task Unsupported_amount_currency_and_method_do_not_reach_provider()
    {
        foreach (PaymentIntent intent in new[] { Intent() with { Amount = 20.505m }, Intent() with { Currency = "USD" }, Intent() with { PaymentMethod = "promptpay_manual" } })
        {
            var (provider, handler) = Provider();
            Assert.Equal(ProviderAuthorizationOutcome.Failed, (await provider.AuthorizeAsync(intent, Token, default)).EffectiveOutcome);
            Assert.Empty(handler.Requests);
        }
    }

    [Fact]
    public async Task Token_is_ephemeral_missing_input_does_not_acquire_lease_and_authorization_replay_needs_no_token()
    {
        var store = new InMemoryPaymentIntents(); Guid organization = Guid.NewGuid();
        var context = new PaymentMutationContext("nexaconnect-order-service", Guid.NewGuid());
        PaymentIntent intent = store.Create(organization, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "omise-test", 20.50m, "THB", "card"), context);
        var (provider, handler) = Provider(Charge(intent)); var service = new PaymentAuthorizationService(store, provider);
        await Assert.ThrowsAsync<ArgumentException>(() => service.AuthorizeAsync(organization, intent.Id, context, default));
        Assert.Equal("pending", store.Get(organization, intent.Id)!.Status);
        Assert.Empty(handler.Requests);
        PaymentIntent authorized = (await service.AuthorizeAsync(organization, intent.Id, context, Token, default))!;
        Assert.Equal("authorized", authorized.Status);
        Assert.Equal("authorized", (await service.AuthorizeAsync(organization, intent.Id, context, default))!.Status);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(store.Get(organization, intent.Id)));
        Assert.Null(await service.AuthorizeAsync(Guid.NewGuid(), intent.Id, context, Token, default));
    }

    [Fact]
    public void Live_credentials_and_remote_hosts_are_rejected()
    {
        Assert.False(OmisePaymentProvider.IsTestSecret("skey_live_abcdefghijklmnopqrstuvwxyz"));
        Assert.Throws<InvalidOperationException>(() => new OmisePaymentProvider(new HttpClient { BaseAddress = new Uri("https://other.example/") },
            Options.Create(new PaymentProviderOptions { OmiseSecretKey = "skey_test_abcdefghijklmnopqrstuvwxyz" }), NullLogger<OmisePaymentProvider>.Instance));
    }

    [Theory]
    [InlineData("used_token", "provider_token_used")]
    [InlineData("invalid_card_token", "provider_card_token_invalid")]
    [InlineData("invalid_charge", "provider_charge_invalid")]
    [InlineData("invalid_amount", "provider_amount_invalid")]
    [InlineData("feature_not_supported", "provider_feature_unsupported")]
    [InlineData("brand_not_supported", "provider_brand_unsupported")]
    [InlineData("failed_multi_currency", "provider_multicurrency_failed")]
    [InlineData("backend_error", "provider_backend_error")]
    [InlineData("bad_request", "provider_bad_request")]
    [InlineData("expired_charge", "provider_charge_expired")]
    [InlineData("failed_capture", "provider_capture_failed")]
    [InlineData("failed_reverse", "provider_void_failed")]
    public async Task Recognized_rejection_codes_are_safe_diagnostics_but_never_confirm_failure_or_retry(string code, string category)
    {
        var store = new InMemoryPaymentIntents();
        Guid organization = Guid.NewGuid();
        var context = new PaymentMutationContext("nexaconnect-order-service", Guid.NewGuid());
        PaymentIntent intent = store.Create(organization, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "diagnostic", 20.50m, "THB", "card"), context);
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent(JsonSerializer.Serialize(new { @object = "error", code, message = Token })) };
        var (provider, handler) = Provider(response);
        var service = new PaymentAuthorizationService(store, provider);
        PaymentIntent result = (await service.AuthorizeAsync(organization, intent.Id, context, Token, default))!;
        Assert.Equal("unknown", result.Status);
        Assert.Equal(category, result.FailureCode);
        Assert.Null(result.ProviderAuthorizationId);
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(result));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AuthorizeAsync(organization, intent.Id, context, Token, default));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("{\"object\":\"error\",\"code\":\"tokn_test_abcdefghijklmnopqrstuvwxyz\"}")]
    [InlineData("{\"object\":\"charge\",\"code\":\"used_token\"}")]
    [InlineData("{\"object\":\"error\",\"code\":123}")]
    [InlineData("not json")]
    public async Task Unsafe_or_malformed_rejection_payloads_retain_only_http_category(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(body) };
        var (provider, handler) = Provider(response);
        ProviderAuthorizationResult result = await provider.AuthorizeAsync(Intent(), Token, default);
        Assert.Equal(ProviderAuthorizationOutcome.Unknown, result.EffectiveOutcome);
        Assert.Equal("provider_http_400", result.FailureReason);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Oversized_rejection_body_does_not_escape_the_http_diagnostic_boundary()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent(JsonSerializer.Serialize(new { @object = "error", code = "used_token", message = new string('x', 65536) })) };
        var (provider, _) = Provider(response);
        ProviderAuthorizationResult result = await provider.AuthorizeAsync(Intent(), Token, default);
        Assert.Equal(ProviderAuthorizationOutcome.Unknown, result.EffectiveOutcome);
        Assert.Equal("provider_http_400", result.FailureReason);
    }

    [Fact]
    public async Task Capture_rejection_diagnostic_never_turns_an_uncertain_command_into_confirmed_failure()
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent("{\"object\":\"error\",\"code\":\"failed_capture\"}") };
        var (provider, handler) = Provider(Charge(intent), response);
        var result = await provider.CaptureAsync(intent, default);
        Assert.Equal(ProviderCaptureOutcome.Unknown, result.Outcome);
        Assert.Equal("provider_capture_failed", result.FailureReason);
        Assert.Equal(new[] { "GET /charges/" + ChargeId, "POST /charges/" + ChargeId + "/capture" }, handler.Requests);
    }

    [Fact]
    public async Task Reversal_rejection_diagnostic_never_turns_an_uncertain_command_into_confirmed_failure()
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent("{\"object\":\"error\",\"code\":\"failed_reverse\"}") };
        var (provider, handler) = Provider(Charge(intent), response);
        var result = await provider.VoidAsync(intent, default);
        Assert.Equal(ProviderVoidOutcome.Unknown, result.Outcome);
        Assert.Equal("provider_void_failed", result.FailureReason);
        Assert.Equal(new[] { "GET /charges/" + ChargeId, "POST /charges/" + ChargeId + "/reverse" }, handler.Requests);
    }

    [Fact]
    public async Task Read_only_inspection_uses_one_get_and_reports_only_explicit_safe_fields()
    {
        PaymentIntent intent = Intent();
        var charge = Charge(intent, paid: true);
        charge["card"] = new { number = Token };
        charge["failure_message"] = Token;
        var (provider, handler) = Provider(charge);
        var report = await provider.InspectTestChargeAsync(ChargeId, 20.50m, default);
        Assert.True(report.ReadSucceeded);
        Assert.True(report.CaptureFinancialFieldsConfirmed);
        Assert.True(report.MetadataUuidFieldsPresent);
        Assert.Equal(2050L, report.CapturedAmountSatang);
        Assert.Equal(0L, report.RefundedAmountSatang);
        Assert.False(report.Reversed);
        Assert.Equal(new[] { "GET /charges/" + ChargeId }, handler.Requests);
        Assert.Null(handler.Body);
        string json = JsonSerializer.Serialize(report);
        foreach (string forbidden in new[] { Token, ChargeId, intent.Id.ToString("D"), intent.OrganizationId.ToString("D"), intent.OrderId.ToString("D") })
            Assert.DoesNotContain(forbidden, json);
    }

    [Theory]
    [InlineData("refunded_amount")]
    [InlineData("reversed")]
    [InlineData("captured_amount")]
    public async Task Read_only_inspection_preserves_missing_financial_fields_as_unknown(string missing)
    {
        var charge = Charge(Intent(), paid: true);
        charge.Remove(missing);
        var (provider, _) = Provider(charge);
        var report = await provider.InspectTestChargeAsync(ChargeId, 20.50m, default);
        Assert.True(report.ReadSucceeded);
        Assert.False(report.CaptureFinancialFieldsConfirmed);
        if (missing == "refunded_amount") Assert.Null(report.RefundedAmountSatang);
        if (missing == "reversed") Assert.Null(report.Reversed);
        if (missing == "captured_amount") Assert.Null(report.CapturedAmountSatang);
    }

    [Theory]
    [InlineData("live")]
    [InlineData("id")]
    [InlineData("object")]
    public async Task Read_only_inspection_rejects_live_unbound_or_non_charge_responses(string mismatch)
    {
        var charge = Charge(Intent(), paid: true);
        charge[mismatch == "live" ? "livemode" : mismatch] = mismatch == "live" ? true : "unexpected";
        var (provider, handler) = Provider(charge);
        var report = await provider.InspectTestChargeAsync(ChargeId, 20.50m, default);
        Assert.False(report.ReadSucceeded);
        Assert.Equal("provider_charge_mismatch", report.FailureCategory);
        Assert.Null(report.AmountSatang);
        Assert.Null(report.Paid);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Read_only_inspection_distinguishes_amount_currency_status_and_metadata_mismatches()
    {
        var charge = Charge(Intent(), paid: true);
        charge["currency"] = Token;
        charge["status"] = Token;
        charge["metadata"] = new { nexa_intent_id = Token };
        var (provider, _) = Provider(charge);
        var report = await provider.InspectTestChargeAsync(ChargeId, 50m, default);
        Assert.True(report.ReadSucceeded);
        Assert.False(report.ExpectedAmountMatches);
        Assert.False(report.CurrencyIsThb);
        Assert.False(report.MetadataUuidFieldsPresent);
        Assert.False(report.CaptureFinancialFieldsConfirmed);
        Assert.Null(report.Status);
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(report));
    }

    [Fact]
    public async Task Read_only_inspection_keeps_transport_failure_bounded_without_retrying()
    {
        var (provider, handler) = Provider(new HttpRequestException(Token));
        var report = await provider.InspectTestChargeAsync(ChargeId, 50m, default);
        Assert.False(report.ReadSucceeded);
        Assert.Equal("provider_transport_failure", report.FailureCategory);
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(report));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Read_only_inspection_validates_reference_and_expected_amount_before_http()
    {
        var (provider, handler) = Provider();
        await Assert.ThrowsAsync<ArgumentException>(() => provider.InspectTestChargeAsync("../charges", 50m, default));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.InspectTestChargeAsync(ChargeId, 50.001m, default));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.InspectTestChargeAsync(ChargeId, 0m, default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Explicit_final_authorization_confirms_full_capture_without_optional_captured_amount_and_replay_is_get_only()
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        var paid = Charge(intent, paid: true);
        paid.Remove("captured_amount");
        paid["authorization_type"] = "final_auth";
        var (provider, handler) = Provider(Charge(intent), paid, paid, paid);
        Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.CaptureAsync(intent, default)).Outcome);
        Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.CaptureAsync(intent, default)).Outcome);
        Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.GetCaptureStatusAsync(intent, default)).Outcome);
        Assert.Equal(1, handler.Requests.Count(request => request.StartsWith("POST ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Final_authorization_inspection_reports_evidence_without_inventing_a_captured_amount()
    {
        var paid = Charge(Intent(), paid: true);
        paid.Remove("captured_amount");
        paid["authorization_type"] = "final_auth";
        var (provider, _) = Provider(paid);
        var report = await provider.InspectTestChargeAsync(ChargeId, 20.50m, default);
        Assert.True(report.CaptureFinancialFieldsConfirmed);
        Assert.Equal("final_auth", report.AuthorizationType);
        Assert.Null(report.CapturedAmountSatang);
    }

    [Theory]
    [InlineData("type_missing")]
    [InlineData("pre_auth")]
    [InlineData("unknown_type")]
    [InlineData("partial")]
    [InlineData("refunded")]
    [InlineData("refund_missing")]
    [InlineData("reversed")]
    [InlineData("reversed_missing")]
    [InlineData("authorized_false")]
    [InlineData("status_pending")]
    [InlineData("capture_true")]
    [InlineData("capturable_true")]
    [InlineData("reversible_true")]
    [InlineData("reversible_missing")]
    public async Task Final_authorization_fallback_never_accepts_missing_or_conflicting_evidence(string conflict)
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        var paid = Charge(intent, paid: true);
        paid.Remove("captured_amount");
        paid["authorization_type"] = "final_auth";
        switch (conflict)
        {
            case "type_missing": paid.Remove("authorization_type"); break;
            case "pre_auth": paid["authorization_type"] = "pre_auth"; break;
            case "unknown_type": paid["authorization_type"] = Token; break;
            case "partial": paid["captured_amount"] = 100; break;
            case "refunded": paid["refunded_amount"] = 100; break;
            case "refund_missing": paid.Remove("refunded_amount"); break;
            case "reversed": paid["reversed"] = true; break;
            case "reversed_missing": paid.Remove("reversed"); break;
            case "authorized_false": paid["authorized"] = false; break;
            case "status_pending": paid["status"] = "pending"; break;
            case "capture_true": paid["capture"] = true; break;
            case "capturable_true": paid["capturable"] = true; break;
            case "reversible_true": paid["reversible"] = true; break;
            case "reversible_missing": paid.Remove("reversible"); break;
        }
        var (provider, handler) = Provider(paid);
        Assert.Equal(ProviderCaptureOutcome.Unknown, (await provider.CaptureAsync(intent, default)).Outcome);
        Assert.Single(handler.Requests);
        Assert.StartsWith("GET ", handler.Requests[0]);
    }

    private static async Task<Dictionary<string, object?>> SignedPaidCharge(PaymentIntent intent)
    {
        var (issuer, issuedRequest) = Provider(Charge(intent));
        await issuer.AuthorizeAsync(intent, Token, default);
        var fields = issuedRequest.Body!.Split('&').Select(field => field.Split('=', 2))
            .ToDictionary(field => WebUtility.UrlDecode(field[0]), field => WebUtility.UrlDecode(field[1]));
        var paid = Charge(intent, paid: true);
        paid.Remove("captured_amount");
        var metadata = (Dictionary<string, string>)paid["metadata"]!;
        metadata["nexa_capture_mode"] = fields["metadata[nexa_capture_mode]"];
        metadata["nexa_capture_proof"] = fields["metadata[nexa_capture_proof]"];
        return paid;
    }

    [Fact]
    public async Task Signed_creation_context_survives_new_adapter_and_confirms_omitted_fields_without_capture_replay()
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        var paid = await SignedPaidCharge(intent);
        var (restarted, handler) = Provider(paid, paid, paid);
        Assert.Equal(ProviderCaptureOutcome.Captured, (await restarted.CaptureAsync(intent, default)).Outcome);
        Assert.Equal(ProviderCaptureOutcome.Captured, (await restarted.GetCaptureStatusAsync(intent, default)).Outcome);
        var report = await restarted.InspectTestChargeAsync(ChargeId, 20.50m, default);
        Assert.True(report.TrustedFullCaptureContextVerified);
        Assert.True(report.CaptureFinancialFieldsConfirmed);
        Assert.Null(report.AuthorizationType);
        Assert.Null(report.CapturedAmountSatang);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request));
        string reportJson = JsonSerializer.Serialize(report);
        Assert.DoesNotContain(((Dictionary<string, string>)paid["metadata"]!)["nexa_capture_proof"], reportJson);
    }

    [Fact]
    public async Task Signed_context_confirms_capture_response_then_replay_and_status_use_get_only()
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        var paid = await SignedPaidCharge(intent);
        var authorized = Charge(intent);
        authorized["metadata"] = paid["metadata"];
        var (provider, handler) = Provider(authorized, paid, paid, paid);
        Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.CaptureAsync(intent, default)).Outcome);
        Assert.Contains("capture_amount=2050", handler.Body!);
        Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.CaptureAsync(intent, default)).Outcome);
        Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.GetCaptureStatusAsync(intent, default)).Outcome);
        Assert.Equal(1, handler.Requests.Count(request => request.StartsWith("POST ", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("proof_missing")]
    [InlineData("proof_forged")]
    [InlineData("proof_malformed")]
    [InlineData("mode_changed")]
    [InlineData("organization_changed")]
    [InlineData("order_changed")]
    [InlineData("intent_changed")]
    [InlineData("amount_changed")]
    [InlineData("currency_changed")]
    [InlineData("type_conflict")]
    [InlineData("partial")]
    [InlineData("refund")]
    [InlineData("missing_refund")]
    [InlineData("reversible_conflict")]
    public async Task Signed_creation_context_cannot_override_tampering_or_conflicting_financial_state(string conflict)
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        var paid = await SignedPaidCharge(intent);
        var metadata = (Dictionary<string, string>)paid["metadata"]!;
        switch (conflict)
        {
            case "proof_missing": metadata.Remove("nexa_capture_proof"); break;
            case "proof_forged": metadata["nexa_capture_proof"] = new string('0', 64); break;
            case "proof_malformed": metadata["nexa_capture_proof"] = Token; break;
            case "mode_changed": metadata["nexa_capture_mode"] = "pre_auth"; break;
            case "organization_changed": metadata["nexa_organization_id"] = Guid.NewGuid().ToString("D"); break;
            case "order_changed": metadata["nexa_order_id"] = Guid.NewGuid().ToString("D"); break;
            case "intent_changed": metadata["nexa_intent_id"] = Guid.NewGuid().ToString("D"); break;
            case "amount_changed": paid["amount"] = 3000; break;
            case "currency_changed": paid["currency"] = "usd"; break;
            case "type_conflict": paid["authorization_type"] = "pre_auth"; break;
            case "partial": paid["captured_amount"] = 100; break;
            case "refund": paid["refunded_amount"] = 100; break;
            case "missing_refund": paid.Remove("refunded_amount"); break;
            case "reversible_conflict": paid["reversible"] = true; break;
        }
        var (provider, handler) = Provider(paid);
        Assert.Equal(ProviderCaptureOutcome.Unknown, (await provider.CaptureAsync(intent, default)).Outcome);
        Assert.Single(handler.Requests);
        Assert.StartsWith("GET ", handler.Requests[0]);
    }

    [Fact]
    public async Task Signed_context_from_another_amount_cannot_be_reused_after_matching_new_intent_amount()
    {
        PaymentIntent intent = Intent();
        var paid = await SignedPaidCharge(intent);
        paid["amount"] = 3000;
        var (provider, _) = Provider(paid);
        var result = await provider.GetCaptureStatusAsync(intent with { Amount = 30m, ProviderAuthorizationId = ChargeId }, default);
        Assert.Equal(ProviderCaptureOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Rotating_test_secret_invalidates_old_signed_context_without_guessing_or_posting()
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        var paid = await SignedPaidCharge(intent);
        var handler = new StubHandler([paid]);
        var provider = new OmisePaymentProvider(new HttpClient(handler) { BaseAddress = new Uri("https://api.omise.co/") },
            Options.Create(new PaymentProviderOptions { OmiseSecretKey = "skey_test_0123456789abcdefghijklmnopqrstuvwxyz" }), NullLogger<OmisePaymentProvider>.Instance);
        Assert.Equal(ProviderCaptureOutcome.Unknown, (await provider.CaptureAsync(intent, default)).Outcome);
        Assert.Single(handler.Requests);
        Assert.StartsWith("GET ", handler.Requests[0]);
    }

    [Fact]
    public async Task Signed_context_survives_lost_reference_lookup_without_creating_a_replacement_charge()
    {
        PaymentIntent intent = Intent();
        var paid = await SignedPaidCharge(intent);
        var (provider, handler) = Provider(new { total_pages = 1, data = new[] { paid } }, paid);
        Assert.Equal(ProviderCaptureOutcome.Captured, (await provider.GetCaptureStatusAsync(intent, default)).Outcome);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request));
    }

    [Fact]
    public async Task Copied_same_context_proof_does_not_override_the_bound_provider_charge_identity()
    {
        PaymentIntent intent = Intent() with { ProviderAuthorizationId = ChargeId };
        var copiedCharge = await SignedPaidCharge(intent);
        copiedCharge["id"] = "chrg_test_0123456789abcdefghijklmnopqrstuvwxyz";
        var (provider, handler) = Provider(copiedCharge);
        var result = await provider.CaptureAsync(intent, default);
        Assert.Equal(ProviderCaptureOutcome.Unknown, result.Outcome);
        Assert.Equal("provider_charge_mismatch", result.FailureReason);
        Assert.Single(handler.Requests);
        Assert.StartsWith("GET ", handler.Requests[0]);
    }

    [Fact]
    public async Task Duplicate_signed_contexts_still_make_lost_reference_recovery_ambiguous()
    {
        PaymentIntent intent = Intent();
        var original = await SignedPaidCharge(intent);
        var copiedCharge = new Dictionary<string, object?>(original)
        { ["id"] = "chrg_test_0123456789abcdefghijklmnopqrstuvwxyz" };
        var (provider, handler) = Provider(new { total_pages = 1, data = new[] { original, copiedCharge } });
        Assert.Equal(ProviderCaptureOutcome.Unknown, (await provider.GetCaptureStatusAsync(intent, default)).Outcome);
        Assert.Single(handler.Requests);
        Assert.StartsWith("GET /search", handler.Requests[0]);
    }

    private sealed class StubHandler(object[] responses) : HttpMessageHandler
    {
        private readonly Queue<object> responses = new(responses);
        public List<string> Requests { get; } = [];
        public string? Body { get; private set; }
        public bool BasicAuthValidated { get; private set; }
        public bool VersionValidated { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            BasicAuthValidated = request.Headers.Authorization?.Scheme == "Basic"
                && Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)) == "skey_test_abcdefghijklmnopqrstuvwxyz:";
            VersionValidated = request.Headers.GetValues("Omise-Version").Single() == "2019-05-29";
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            object response = responses.Dequeue();
            if (response is Exception exception) throw exception;
            if (response is HttpResponseMessage httpResponse) return httpResponse;
            return new HttpResponseMessage(response is HttpStatusCode code ? code : HttpStatusCode.OK)
            { Content = new StringContent(response is string text ? text : JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }
}
