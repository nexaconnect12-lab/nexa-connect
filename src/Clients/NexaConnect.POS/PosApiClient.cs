using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NexaConnect.POS;

public sealed record PosShift(Guid ShiftId, Guid AuthorizationDecisionId);
public sealed record PosMenuItem(Guid ProductId, string Name, decimal UnitPrice, string Currency, string PreparationStation, bool Available);
public enum PosOrderStatus { Draft, Submitted, InventoryReserved, KitchenAccepted, Paid, PaymentFailed, Rejected, PaymentPending, PaymentReview }
public sealed record PosOrderResult(Guid OrderId, PosOrderStatus Status, decimal TotalAmount, string Currency);
public sealed record ManualTenderResult(Guid SettlementId, Guid OrderId, string Status, string Method,
    decimal Amount, string Currency, DateTimeOffset OccurredAtUtc, bool Replayed);
public sealed record CashSessionResult(Guid CashSessionId, string OpenedBy);

public sealed class PosApiClient : IDisposable
{
    private readonly PosClientConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly HttpClient _orderHttpClient;

    public PosApiClient(PosClientConfiguration configuration)
    {
        _configuration = configuration;
        _httpClient = new HttpClient { BaseAddress = new Uri(configuration.PosApi) };
        _orderHttpClient = new HttpClient { BaseAddress = new Uri(configuration.OrderApi) };
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
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Menu could not be loaded.");
        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<PosMenuItem>>(cancellationToken) ?? [];
    }

    public async Task<PosOrderResult> PlaceOrderAsync(PosTokenSet token, PosClientConfiguration configuration, IReadOnlyCollection<(Guid ProductId, int Quantity)> lines, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/order/v1/workflows/place", token);
        AddTenantContext(request, configuration.OrganizationId);
        request.Content = JsonContent.Create(new
        {
            restaurantId = configuration.RestaurantId, organizationId = configuration.OrganizationId, branchId = configuration.BranchId,
            currency = configuration.Currency, paymentMethod = configuration.PaymentMethod, idempotencyKey = Guid.NewGuid().ToString("N"),
            lines = lines.Select(line => new { productId = line.ProductId, quantity = line.Quantity }).ToArray()
        });
        using var response = await _orderHttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Order could not be placed.");
        return await response.Content.ReadFromJsonAsync<PosOrderResult>(cancellationToken) ?? throw new InvalidDataException("The Order API returned an empty response.");
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

    public async Task CloseCashSessionAsync(PosTokenSet token, Guid cashSessionId, decimal actualClosingAmount, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"api/pos/v1/cash-sessions/{cashSessionId:D}/close", token); request.Content = JsonContent.Create(new { actualClosingAmount });
        using var response = await _httpClient.SendAsync(request, cancellationToken); await EnsureSuccessAsync(response, "Cash session could not be closed.");
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
        if (response.StatusCode == HttpStatusCode.Forbidden)
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
                HttpStatusCode.Conflict => "The resource changed concurrently. Refresh its state before continuing.",
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
    }
}

public sealed class PosApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
