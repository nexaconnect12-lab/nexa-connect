using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexaConnect.Observability;

namespace NexaConnect.POS;

public sealed record PosShift(Guid ShiftId, Guid AuthorizationDecisionId);
public sealed record PosMenuItem(Guid ProductId, string Name, decimal UnitPrice, string Currency, string PreparationStation, bool Available);
public enum PosOrderStatus { Draft, Submitted, InventoryReserved, KitchenAccepted, Paid, PaymentFailed, Rejected, PaymentPending, PaymentReview }
public sealed record PosOrderResult(Guid OrderId, PosOrderStatus Status, decimal TotalAmount, string Currency, bool CardTokenRequired = false, PosOrderPricing? Pricing = null);
public sealed record PosOrderPricing(long PolicyVersion, decimal TaxPercent, bool TaxInclusive,
    decimal ServiceChargePercent, decimal MenuAmount, decimal SubtotalAmount, decimal ServiceChargeAmount,
    decimal TaxAmount, decimal TotalAmount)
{
    public string Summary => $"Subtotal THB {SubtotalAmount:N2} | Service charge THB {ServiceChargeAmount:N2} | Tax THB {TaxAmount:N2} | Total THB {TotalAmount:N2}";
}
public sealed record PosOrderQuote(string Fingerprint, PosOrderPricing Pricing, IReadOnlyList<PosQuoteLine> Lines);
public sealed record PosQuoteLine(Guid ProductId, string Name, decimal UnitPrice, int Quantity);
public sealed class PosPricingChangedException : Exception;
public sealed record ManualTenderResult(Guid SettlementId, Guid OrderId, string Status, string Method,
    decimal Amount, string Currency, DateTimeOffset OccurredAtUtc, bool Replayed);
public sealed record CashSessionResult(Guid CashSessionId, string OpenedBy);
public sealed record PosCashMovementSummary(Guid MovementId, string MovementType, decimal Amount,
    string? ReasonCode, DateTimeOffset OccurredAtUtc);
public sealed record PosCashSessionSummary(Guid CashSessionId, Guid ShiftId, Guid StoreId, Guid TerminalId,
    string Currency, decimal OpeningAmount, decimal NetMovementAmount, decimal ExpectedClosingAmount,
    decimal? ActualClosingAmount, decimal? VarianceAmount, string Status, DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc, long ConcurrencyVersion, IReadOnlyList<PosCashMovementSummary> Movements);
public sealed record PosCashReviewAccess(bool CanRead, bool CanResolve);
public sealed record PosCashReviewListItem(Guid CashSessionId, Guid ShiftId, Guid StoreId, Guid TerminalId,
    string ShiftNumber, string CashierSubjectId, string Currency, decimal ExpectedClosingAmount,
    decimal ActualClosingAmount, decimal VarianceAmount, DateTimeOffset ClosedAtUtc, long SessionVersion,
    string ReviewStatus, long ReviewVersion, DateTimeOffset? ReviewedAtUtc);
public sealed record PosCashReviewHistoryEntry(Guid Id, long SessionVersion, string Decision, string Reason,
    string ReviewerSubjectId, Guid AuthorizationDecisionId, long ReviewVersion, DateTimeOffset OccurredAtUtc);
public sealed record PosCashReviewDetail(PosCashReviewListItem Session,
    IReadOnlyList<PosCashMovementSummary> Movements, IReadOnlyList<PosCashReviewHistoryEntry> History);
public sealed record PosCashReviewPage(IReadOnlyList<PosCashReviewListItem> Items, string? NextCursor);

public sealed record PosReceiptLine(Guid ProductId, string Name, decimal UnitPrice, int Quantity, decimal Total);
public sealed record PosReceipt(int Version, string ReceiptNumber, Guid OrderId, Guid OrganizationId,
    Guid RestaurantId, Guid BranchId, string OrderNumber, DateTimeOffset PaidAtUtc, string Currency, string Tender,
    IReadOnlyList<PosReceiptLine> Lines, PosOrderPricing? Pricing, decimal SubtotalAmount,
    decimal ServiceChargeAmount, decimal TaxAmount, decimal TotalAmount)
{
    public string Render()
    {
        static string Money(decimal value) => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        static string Safe(string value) => new(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return "SALES RECEIPT — PAID\n" + Safe(ReceiptNumber) + "\nOrder: " + Safe(OrderNumber)
            + "\nOrder ID: " + OrderId.ToString("D") + "\nBranch: " + BranchId.ToString("D")
            + "\nPaid (UTC): " + PaidAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            + "\nTender: " + Safe(Tender) + "\n\n"
            + string.Join("\n", Lines.Select(l => $"{l.Quantity} × {Safe(l.Name)} @ {Money(l.UnitPrice)} = {Money(l.Total)}"))
            + $"\n\nSubtotal {Currency} {Money(SubtotalAmount)}\nService charge {Currency} {Money(ServiceChargeAmount)}"
            + $"\nTax {Currency} {Money(TaxAmount)}\nTOTAL {Currency} {Money(TotalAmount)}"
            + (Pricing?.TaxInclusive == true ? "\nMenu prices include tax." : "")
            + (Pricing is null ? "\nLegacy order: tax breakdown unavailable." : "")
            + "\n\nOrdinary sales receipt. Not a tax invoice.";
    }
}

public sealed class PosApiClient : IDisposable
{
    private readonly PosClientConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly HttpClient _orderHttpClient;
    private readonly HttpClient _catalogHttpClient;
    private readonly ILoggerFactory loggerFactory = NexaConnectObservabilityExtensions.CreateClientLoggerFactory("nexaconnect-pos-client");

    public PosApiClient(PosClientConfiguration configuration, HttpMessageHandler? posHandler = null,
        HttpMessageHandler? orderHandler = null, HttpMessageHandler? catalogHandler = null)
    {
        _configuration = configuration;
        configuration.ValidateCheckout();
        _httpClient = new HttpClient(posHandler ?? new HttpClientHandler()) { BaseAddress = new Uri(configuration.PosApi) };
        _orderHttpClient = new HttpClient(orderHandler ?? new HttpClientHandler()) { BaseAddress = new Uri(configuration.OrderApi) };
        _catalogHttpClient = new HttpClient(catalogHandler ?? new HttpClientHandler()) { BaseAddress = new Uri(configuration.CatalogApi) };
    }

    public async Task<PosReceipt> GetReceiptAsync(PosTokenSet token, Guid orderId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/order/v1/orders/{orderId:D}/receipt?branchId={_configuration.BranchId:D}", token);
        AddTenantContext(request, _configuration.OrganizationId);
        using var response = await SendCheckoutAsync(_orderHttpClient, request, "order.receipt.read", Guid.NewGuid(), cancellationToken);
        await EnsureSuccessAsync(response, "Receipt unavailable. Payment is unchanged; verify access or retry receipt retrieval.");
        var receipt = await response.Content.ReadFromJsonAsync<PosReceipt>(cancellationToken)
            ?? throw new InvalidDataException("Empty receipt response.");
        if (receipt.Version != 1 || receipt.OrderId != orderId || receipt.OrganizationId != _configuration.OrganizationId
            || receipt.RestaurantId != _configuration.RestaurantId || receipt.BranchId != _configuration.BranchId
            || receipt.Currency != _configuration.Currency || receipt.PaidAtUtc == default
            || receipt.ReceiptNumber != $"R-{orderId:N}".ToUpperInvariant() || string.IsNullOrWhiteSpace(receipt.Tender)
            || receipt.Lines is null || receipt.Lines.Count == 0 || receipt.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || l.Total != l.UnitPrice * l.Quantity)
            || receipt.TotalAmount < 0 || receipt.SubtotalAmount + receipt.ServiceChargeAmount + receipt.TaxAmount != receipt.TotalAmount
            || (receipt.Pricing is { } p && (p.TotalAmount != receipt.TotalAmount || p.SubtotalAmount != receipt.SubtotalAmount
                || p.ServiceChargeAmount != receipt.ServiceChargeAmount || p.TaxAmount != receipt.TaxAmount)))
            throw new InvalidDataException("Receipt does not match this order and terminal scope.");
        return receipt;
    }

    public async Task<PosShift> OpenShiftAsync(
        PosTokenSet token,
        Guid branchId,
        Guid storeId,
        Guid terminalId,
        string shiftNumber,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post,
            "api/pos/v1/shifts/open",
            token);
        request.Content = JsonContent.Create(new
        {
            branchId,
            storeId,
            terminalId,
            shiftNumber
        });
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shift open failed.");
        return await response.Content.ReadFromJsonAsync<PosShift>(cancellationToken)
            ?? throw new InvalidDataException("The POS API returned an empty shift response.");
    }

    public async Task CloseShiftAsync(
        PosTokenSet token,
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post,
            $"api/pos/v1/shifts/{shiftId:D}/close",
            token);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shift close failed.");
    }

    public async Task<IReadOnlyCollection<PosMenuItem>> GetMenuAsync(PosTokenSet token, Guid branchId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/catalog/v1/branches/{branchId:D}/menu-items", token);
        if (branchId != _configuration.BranchId) throw new InvalidDataException("Menu branch differs from terminal configuration.");
        AddTenantContext(request, _configuration.OrganizationId);
        using var response = await SendCheckoutAsync(_catalogHttpClient, request, "catalog.menu", Guid.NewGuid(), cancellationToken);
        await EnsureSuccessAsync(response, "Menu could not be loaded.");
        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<PosMenuItem>>(cancellationToken) ?? [];
    }

    public async Task<PosOrderQuote> QuoteAsync(PosTokenSet token, PendingCheckout checkout, CancellationToken cancellationToken = default)
    {
        checkout.Validate(_configuration);
        using var request = CreateRequest(HttpMethod.Post, "api/order/v1/workflows/quote", token);
        AddTenantContext(request, checkout.OrganizationId);
        request.Content = JsonContent.Create(new
        {
            checkout.RestaurantId, checkout.OrganizationId, checkout.BranchId, checkout.Currency,
            checkout.PaymentMethod, IdempotencyKey = checkout.OrderId.ToString("N"), checkout.Lines
        });
        using var response = await SendCheckoutAsync(_orderHttpClient, request, "order.quote", checkout.OrderId, cancellationToken);
        await EnsureSuccessAsync(response, "Pricing could not be loaded.");
        var quote = await response.Content.ReadFromJsonAsync<PosOrderQuote>(cancellationToken)
            ?? throw new InvalidDataException("Order quote was empty.");
        if (quote.Fingerprint is null || !System.Text.RegularExpressions.Regex.IsMatch(quote.Fingerprint, "\\A[0-9A-F]{64}\\z")
            || quote.Pricing is null || quote.Lines is null
            || quote.Pricing.TotalAmount <= 0 || quote.Pricing.SubtotalAmount < 0 || quote.Pricing.ServiceChargeAmount < 0 || quote.Pricing.TaxAmount < 0
            || quote.Pricing.TotalAmount != quote.Pricing.SubtotalAmount + quote.Pricing.ServiceChargeAmount + quote.Pricing.TaxAmount
            || !quote.Lines.OrderBy(l => l.ProductId).Select(l => (l.ProductId, l.Quantity))
                .SequenceEqual(checkout.Lines.OrderBy(l => l.ProductId).Select(l => (l.ProductId, l.Quantity))))
            throw new InvalidDataException("Order quote does not match checkout.");
        return quote;
    }

    public async Task<PosOrderResult> PlaceOrderAsync(PosTokenSet token, PendingCheckout checkout, CancellationToken cancellationToken = default)
        => await PlaceOrderAsync(token, checkout, null, cancellationToken);

    public async Task<PosOrderResult> PlaceOrderAsync(PosTokenSet token, PendingCheckout checkout, string? cardToken, CancellationToken cancellationToken = default)
    {
        checkout.Validate(_configuration);
        if (cardToken is not null && (checkout.PaymentMethod != "card_omise_test"
            || !System.Text.RegularExpressions.Regex.IsMatch(cardToken, @"\Atokn_test_[a-z0-9]{10,64}\z")))
            throw new InvalidDataException("Use a fresh Omise test card token for test checkout.");
        using var request = CreateRequest(HttpMethod.Post, "api/order/v1/workflows/place", token);
        AddTenantContext(request, checkout.OrganizationId);
        request.Content = JsonContent.Create(new
        {
            restaurantId = checkout.RestaurantId, organizationId = checkout.OrganizationId, branchId = checkout.BranchId,
            currency = checkout.Currency, paymentMethod = checkout.PaymentMethod, idempotencyKey = checkout.OrderId.ToString("N"),
            orderId = checkout.OrderId, correlationId = checkout.OrderId,
            cardToken, pricingFingerprint = checkout.PricingFingerprint,
            lines = checkout.Lines.Select(line => new { productId = line.ProductId, quantity = line.Quantity }).ToArray()
        });
        using var response = await SendCheckoutAsync(_orderHttpClient, request, "order.place", checkout.OrderId, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)))
                if (body.RootElement.TryGetProperty("code", out var code) && code.GetString() == "pricing_changed")
                    throw new PosPricingChangedException();
            try
            {
                PosOrderResult? rejected = await response.Content.ReadFromJsonAsync<PosOrderResult>(cancellationToken);
                if (rejected is not null && rejected.Status is PosOrderStatus.Rejected or PosOrderStatus.PaymentFailed)
                {
                    ValidateOrderResult(checkout, rejected);
                    return rejected;
                }
            }
            catch (JsonException)
            {
                // A non-order conflict continues through the normal safe error mapping.
            }
        }
        await EnsureSuccessAsync(response, "Order could not be placed.");
        var result = await response.Content.ReadFromJsonAsync<PosOrderResult>(cancellationToken)
            ?? throw new InvalidDataException("The Order API returned an empty response.");
        ValidateOrderResult(checkout, result);
        return result;
    }

    private static void ValidateOrderResult(PendingCheckout checkout, PosOrderResult result)
    {
        if (result.OrderId != checkout.OrderId || result.Currency != checkout.Currency || result.TotalAmount <= 0)
            throw new InvalidDataException("Order response does not match the pending checkout.");
        if (result.Pricing is {} pricing && (pricing.TotalAmount != result.TotalAmount
            || pricing.TotalAmount != pricing.SubtotalAmount + pricing.ServiceChargeAmount + pricing.TaxAmount))
            throw new InvalidDataException("Order pricing does not match its total.");
        if (result.CardTokenRequired && (checkout.PaymentMethod != "card_omise_test"
            || result.Status is not (PosOrderStatus.KitchenAccepted or PosOrderStatus.PaymentPending)))
            throw new InvalidDataException("Card-token action does not match the original checkout state.");
    }

    public async Task<ManualTenderResult> ConfirmManualSettlementAsync(
        PosTokenSet token,
        Guid orderId,
        Guid idempotencyKey,
        string method,
        decimal amount,
        string currency,
        bool receiptConfirmed,
        string? bankReference,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post,
            $"api/order/v1/orders/{orderId:D}/manual-settlement", token);
        AddTenantContext(request, _configuration.OrganizationId);
        request.Content = JsonContent.Create(new
        {
            organizationId = _configuration.OrganizationId,
            branchId = _configuration.BranchId,
            terminalId = _configuration.TerminalId,
            idempotencyKey,
            method,
            amount,
            currency,
            receiptConfirmed,
            bankReference,
            correlationId = orderId
        });
        using var response = await _orderHttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Manual settlement could not be confirmed.");
        return await response.Content.ReadFromJsonAsync<ManualTenderResult>(cancellationToken)
            ?? throw new InvalidDataException("The Order API returned an empty settlement response.");
    }

    public async Task<CashSessionResult> OpenCashSessionAsync(PosTokenSet token, Guid shiftId, Guid storeId, string currency, decimal openingAmount, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/pos/v1/cash-sessions/open", token); request.Content = JsonContent.Create(new { shiftId, storeId, currency, openingAmount });
        using var response = await _httpClient.SendAsync(request, cancellationToken); await EnsureSuccessAsync(response, "Cash session could not be opened.");
        return await response.Content.ReadFromJsonAsync<CashSessionResult>(cancellationToken) ?? throw new InvalidDataException("Empty cash-session response.");
    }

    public async Task RecordCashMovementAsync(
        PosTokenSet token,
        Guid cashSessionId,
        Guid terminalId,
        Guid clientOperationId,
        string movementType,
        decimal amount,
        string? reasonCode,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"api/pos/v1/cash-sessions/{cashSessionId:D}/movements", token);
        request.Headers.Add("X-Client-Operation-Id", clientOperationId.ToString("D"));
        request.Headers.Add("X-Nexa-Terminal-Id", terminalId.ToString("D"));
        request.Content = JsonContent.Create(new { movementType, amount, reasonCode });
        using var response = await _httpClient.SendAsync(request, cancellationToken); await EnsureSuccessAsync(response, "Cash movement could not be recorded.");
    }

    public async Task<PosCashSessionSummary> GetCashSessionSummaryAsync(
        PosTokenSet token,
        Guid cashSessionId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"api/pos/v1/cash-sessions/{cashSessionId:D}/summary", token);
        request.Headers.Add("X-Nexa-Terminal-Id", _configuration.TerminalId.ToString("D"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Cash reconciliation could not be refreshed.");
        PosCashSessionSummary summary = await response.Content.ReadFromJsonAsync<PosCashSessionSummary>(cancellationToken)
            ?? throw new InvalidDataException("The POS API returned an empty cash reconciliation response.");
        if (summary.CashSessionId != cashSessionId || summary.ShiftId == Guid.Empty ||
            summary.StoreId != _configuration.StoreId ||
            summary.TerminalId != _configuration.TerminalId ||
            !string.Equals(summary.Currency, _configuration.Currency, StringComparison.Ordinal) ||
            summary.ConcurrencyVersion <= 0 || summary.OpeningAmount < 0 ||
            summary.ExpectedClosingAmount != summary.OpeningAmount + summary.NetMovementAmount ||
            summary.Status is not ("open" or "closed") ||
            summary.Movements is null ||
            summary.Movements.Any(movement => movement.MovementId == Guid.Empty || movement.Amount <= 0 ||
                movement.MovementType is not ("sale" or "refund" or "pay_in" or "pay_out" or "float_adjustment")) ||
            summary.NetMovementAmount != summary.Movements.Sum(movement =>
                movement.MovementType is "sale" or "pay_in" or "float_adjustment"
                    ? movement.Amount
                    : -movement.Amount) ||
            summary.Status == "open" && (summary.ActualClosingAmount is not null ||
                summary.VarianceAmount is not null || summary.ClosedAtUtc is not null) ||
            summary.Status == "closed" && (summary.ActualClosingAmount is null ||
                summary.VarianceAmount != summary.ActualClosingAmount - summary.ExpectedClosingAmount ||
                summary.ClosedAtUtc is null))
        {
            throw new InvalidDataException("The cash reconciliation response does not match this terminal and session.");
        }
        return summary;
    }

    public async Task CloseCashSessionAsync(PosTokenSet token, Guid cashSessionId, decimal actualClosingAmount,
        long expectedConcurrencyVersion, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"api/pos/v1/cash-sessions/{cashSessionId:D}/close", token);
        request.Headers.Add("X-Nexa-Terminal-Id", _configuration.TerminalId.ToString("D"));
        request.Content = JsonContent.Create(new { actualClosingAmount, expectedConcurrencyVersion });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Cash session could not be closed.");
    }

    public async Task<PosCashReviewAccess> GetCashReviewAccessAsync(PosTokenSet token,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/pos/v1/cash-reviews/access?{CashReviewScopeQuery()}", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Cash-review access could not be checked.");
        PosCashReviewAccess access = await response.Content.ReadFromJsonAsync<PosCashReviewAccess>(cancellationToken)
            ?? throw new InvalidDataException("The POS API returned an empty cash-review access response.");
        if (access.CanResolve && !access.CanRead)
            throw new InvalidDataException("The cash-review access response is inconsistent.");
        return access;
    }

    public async Task<PosCashReviewPage> GetCashReviewsAsync(PosTokenSet token, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, string? cursor = null, CancellationToken cancellationToken = default)
    {
        string path = $"api/pos/v1/cash-reviews?{CashReviewScopeQuery()}&fromUtc={Uri.EscapeDataString(fromUtc.ToUniversalTime().ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToUniversalTime().ToString("O"))}&limit=50";
        if (!string.IsNullOrWhiteSpace(cursor)) path += $"&cursor={Uri.EscapeDataString(cursor)}";
        using var request = CreateRequest(HttpMethod.Get, path, token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Cash-review history could not be loaded.");
        PosCashReviewPage page = await response.Content.ReadFromJsonAsync<PosCashReviewPage>(cancellationToken)
            ?? throw new InvalidDataException("The POS API returned an empty cash-review page.");
        if (page.Items is null || page.Items.Any(item => !ValidReviewItem(item)))
            throw new InvalidDataException("Cash-review history does not match this store.");
        return page;
    }

    public async Task<PosCashReviewDetail> GetCashReviewAsync(PosTokenSet token, Guid cashSessionId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"api/pos/v1/cash-reviews/{cashSessionId:D}?{CashReviewScopeQuery()}", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Cash-review detail could not be loaded.");
        PosCashReviewDetail detail = await response.Content.ReadFromJsonAsync<PosCashReviewDetail>(cancellationToken)
            ?? throw new InvalidDataException("The POS API returned an empty cash-review detail.");
        ValidateReviewDetail(cashSessionId, detail);
        return detail;
    }

    public async Task<PosCashReviewDetail> ResolveCashReviewAsync(PosTokenSet token, Guid cashSessionId,
        string decision, string reason, long expectedSessionVersion, long expectedReviewVersion,
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post,
            $"api/pos/v1/cash-reviews/{cashSessionId:D}/decisions", token);
        request.Content = JsonContent.Create(new
        {
            organizationId = _configuration.OrganizationId,
            branchId = _configuration.BranchId,
            storeId = _configuration.StoreId,
            decision,
            reason,
            expectedSessionVersion,
            expectedReviewVersion,
            idempotencyKey
        });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Cash-review decision could not be saved.");
        PosCashReviewDetail detail = await response.Content.ReadFromJsonAsync<PosCashReviewDetail>(cancellationToken)
            ?? throw new InvalidDataException("The POS API returned an empty cash-review decision response.");
        ValidateReviewDetail(cashSessionId, detail);
        if (!detail.History.Any(entry => entry.Id == idempotencyKey))
            throw new InvalidDataException("The cash-review response did not confirm this decision identity.");
        return detail;
    }

    public async Task EnrollTerminalAsync(PosTokenSet token, PosClientConfiguration configuration, string code, string deviceType, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/pos/v1/terminals/enroll", token); request.Content = JsonContent.Create(new { branchId = configuration.BranchId, storeId = configuration.StoreId, terminalId = configuration.TerminalId, code, deviceType });
        using var response = await _httpClient.SendAsync(request, cancellationToken); await EnsureSuccessAsync(response, "Terminal enrollment failed.");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, PosTokenSet token)
    {
        if (token.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("The POS access token has expired. Sign in again.");
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);
        return request;
    }

    private static void AddTenantContext(HttpRequestMessage request, Guid organizationId)
    {
        request.Headers.Add("X-Nexa-Organization-Id", organizationId.ToString("D"));
        request.Headers.Add("X-Nexa-Application-Code", "nexa_connect");
    }

    private string CashReviewScopeQuery() =>
        $"organizationId={_configuration.OrganizationId:D}&branchId={_configuration.BranchId:D}&storeId={_configuration.StoreId:D}";

    private bool ValidReviewItem(PosCashReviewListItem item) => item.CashSessionId != Guid.Empty &&
        item.ShiftId != Guid.Empty && item.StoreId == _configuration.StoreId && item.TerminalId != Guid.Empty &&
        item.Currency == _configuration.Currency && item.SessionVersion > 0 && item.ReviewVersion >= 0 &&
        item.ClosedAtUtc != default && item.ActualClosingAmount - item.ExpectedClosingAmount == item.VarianceAmount &&
        item.ReviewStatus is "balanced" or "review_required" or "investigating" or "approved" &&
        (item.VarianceAmount == 0 ? item.ReviewStatus == "balanced" : item.ReviewStatus != "balanced");

    private void ValidateReviewDetail(Guid cashSessionId, PosCashReviewDetail detail)
    {
        PosCashReviewHistoryEntry[] orderedHistory = detail.History?.ToArray() ?? [];
        bool historySequenceIsValid = orderedHistory.Select((entry, index) =>
                entry.ReviewVersion == index + 1 &&
                (index == 0 || entry.OccurredAtUtc >= orderedHistory[index - 1].OccurredAtUtc))
            .All(valid => valid);
        PosCashReviewHistoryEntry? latest = orderedHistory.LastOrDefault();
        bool currentStateIsValid = detail.Session.ReviewStatus switch
        {
            "investigating" => latest is { Decision: "investigate" } &&
                latest.SessionVersion == detail.Session.SessionVersion,
            "approved" => latest is { Decision: "approve" } &&
                latest.SessionVersion == detail.Session.SessionVersion,
            "review_required" => latest is null || latest.SessionVersion < detail.Session.SessionVersion,
            "balanced" => true,
            _ => false
        };

        if (detail.Session.CashSessionId != cashSessionId || !ValidReviewItem(detail.Session) ||
            detail.Movements is null || detail.History is null ||
            detail.Movements.Any(movement => movement.MovementId == Guid.Empty || movement.Amount <= 0 ||
                movement.MovementType is not ("sale" or "refund" or "pay_in" or "pay_out" or "float_adjustment")) ||
            detail.History.Any(entry => entry.Id == Guid.Empty || entry.SessionVersion <= 0 ||
                entry.SessionVersion > detail.Session.SessionVersion || entry.ReviewVersion <= 0 ||
                entry.AuthorizationDecisionId == Guid.Empty || string.IsNullOrWhiteSpace(entry.Reason) ||
                entry.Decision is not ("approve" or "investigate")) ||
            detail.History.Select(entry => entry.ReviewVersion).Distinct().Count() != detail.History.Count ||
            (detail.History.Count == 0 ? detail.Session.ReviewVersion != 0 :
                detail.History.Max(entry => entry.ReviewVersion) != detail.Session.ReviewVersion) ||
            !historySequenceIsValid || !currentStateIsValid)
            throw new InvalidDataException("Cash-review detail does not match this store or review history.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string fallback)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? stage = null;
        string? conflictTitle = null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Conflict)
        {
            try
            {
                using JsonDocument problem = await response.Content.ReadFromJsonAsync<JsonDocument>()
                    ?? throw new InvalidDataException();
                if (problem.RootElement.TryGetProperty("extensions", out JsonElement extensions) &&
                    extensions.TryGetProperty("stage", out JsonElement stageElement))
                {
                    stage = stageElement.GetString();
                }
                if (response.StatusCode == HttpStatusCode.Conflict
                    && problem.RootElement.TryGetProperty("title", out JsonElement titleElement))
                {
                    string? title = titleElement.GetString();
                    if (!string.IsNullOrWhiteSpace(title) && title.Length <= 300)
                    {
                        conflictTitle = title;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        string detail = stage switch
        {
            "store-terminal-scope" => "This terminal is not enrolled for the configured store. Use Enroll terminal first.",
            "authorization-decision" => "Your account has no POS permission for this branch. Ask an authorization administrator to assign cashier access.",
            _ => response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Sign in again to continue.",
                HttpStatusCode.Forbidden => "Your account is not authorized for this operation.",
                HttpStatusCode.Conflict => conflictTitle ?? "The resource changed concurrently. Refresh its state before continuing.",
                HttpStatusCode.ServiceUnavailable => "A POS dependency is temporarily unavailable.",
                _ => fallback
            }
        };
        throw new PosApiException((int)response.StatusCode, detail);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _orderHttpClient.Dispose();
        _catalogHttpClient.Dispose();
        loggerFactory.Dispose();
    }

    private async Task<HttpResponseMessage> SendCheckoutAsync(HttpClient client, HttpRequestMessage request,
        string operation, Guid correlationId, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Correlation-ID", correlationId.ToString("D"));
        var logger = loggerFactory.CreateLogger("NexaConnect.POS.Checkout");
        try
        {
            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Checkout boundary rejected {Operation} {StatusCode} {CorrelationId}", operation, (int)response.StatusCode, correlationId);
            return response;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Checkout transport failed {Operation} {CorrelationId}", operation, correlationId);
            throw;
        }
    }
}

public sealed class PosApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
