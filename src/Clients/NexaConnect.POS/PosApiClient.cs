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
public sealed record PosOrderResult(Guid OrderId, PosOrderStatus Status, decimal TotalAmount, string Currency);
public sealed record ManualTenderResult(Guid SettlementId, Guid OrderId, string Status, string Method,
    decimal Amount, string Currency, DateTimeOffset OccurredAtUtc, bool Replayed);
public sealed record CashSessionResult(Guid CashSessionId, string OpenedBy);
public sealed record PosCashMovementSummary(Guid MovementId, string MovementType, decimal Amount,
    string? ReasonCode, DateTimeOffset OccurredAtUtc);
public sealed record PosCashSessionSummary(Guid CashSessionId, Guid ShiftId, Guid StoreId, Guid TerminalId,
    string Currency, decimal OpeningAmount, decimal NetMovementAmount, decimal ExpectedClosingAmount,
    decimal? ActualClosingAmount, decimal? VarianceAmount, string Status, DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc, long ConcurrencyVersion, IReadOnlyList<PosCashMovementSummary> Movements);

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

    public async Task<PosOrderResult> PlaceOrderAsync(PosTokenSet token, PendingCheckout checkout, CancellationToken cancellationToken = default)
    {
        checkout.Validate(_configuration);
        using var request = CreateRequest(HttpMethod.Post, "api/order/v1/workflows/place", token);
        AddTenantContext(request, checkout.OrganizationId);
        request.Content = JsonContent.Create(new
        {
            restaurantId = checkout.RestaurantId, organizationId = checkout.OrganizationId, branchId = checkout.BranchId,
            currency = checkout.Currency, paymentMethod = checkout.PaymentMethod, idempotencyKey = checkout.OrderId.ToString("N"),
            orderId = checkout.OrderId, correlationId = checkout.OrderId,
            lines = checkout.Lines.Select(line => new { productId = line.ProductId, quantity = line.Quantity }).ToArray()
        });
        using var response = await SendCheckoutAsync(_orderHttpClient, request, "order.place", checkout.OrderId, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
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
