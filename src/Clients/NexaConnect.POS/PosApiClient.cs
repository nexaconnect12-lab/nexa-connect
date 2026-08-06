using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NexaConnect.POS;

public sealed record PosShift(Guid ShiftId, Guid AuthorizationDecisionId);

public sealed class PosApiClient : IDisposable
{
    private readonly PosClientConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public PosApiClient(PosClientConfiguration configuration)
    {
        _configuration = configuration;
        _httpClient = new HttpClient { BaseAddress = new Uri(configuration.PosApi) };
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

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string fallback)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string detail = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Sign in again to continue.",
            HttpStatusCode.Forbidden => "Your account is not authorized for this terminal.",
            HttpStatusCode.Conflict => "The shift changed or is already open.",
            HttpStatusCode.ServiceUnavailable => "A POS dependency is temporarily unavailable.",
            _ => fallback
        };
        throw new PosApiException((int)response.StatusCode, detail);
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class PosApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
