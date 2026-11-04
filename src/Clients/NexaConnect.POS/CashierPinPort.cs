using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Contracts.POS;

namespace NexaConnect.POS;

public interface ICashierPinPort
{
    Task<CashierPinStatusV1> StatusAsync(string accessToken, CancellationToken ct);
    Task<CashierPinResultV1> ChangeAsync(string accessToken, string pin, string? currentPin, long version, CancellationToken ct);
    Task<CashierPinResultV1> VerifyAsync(string accessToken, string pin, long version, CancellationToken ct);
}
internal sealed class HttpCashierPinPort(PosClientConfiguration config) : ICashierPinPort, IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 16 * 1024 };
    private string Root => config.PosApi.TrimEnd('/') + $"/api/pos/v1/customer/organizations/{config.OrganizationId}/cashier-pin";
    private CashierPinScopeV1 Scope => new(config.BranchId, config.StoreId, config.TerminalId);
    public Task<CashierPinStatusV1> StatusAsync(string token, CancellationToken ct)
        => Send<CashierPinStatusV1>(HttpMethod.Get, Root + $"?branchId={config.BranchId}&storeId={config.StoreId}&terminalId={config.TerminalId}", token, null, ct);
    public Task<CashierPinResultV1> ChangeAsync(string token, string pin, string? current, long version, CancellationToken ct)
        => Send<CashierPinResultV1>(HttpMethod.Put, Root, token, new CashierPinChangeV1(Scope, pin, current, version, Guid.NewGuid()), ct);
    public Task<CashierPinResultV1> VerifyAsync(string token, string pin, long version, CancellationToken ct)
        => Send<CashierPinResultV1>(HttpMethod.Post, Root + "/verify", token, new CashierPinVerifyV1(Scope, pin, version, Guid.NewGuid()), ct);
    private async Task<T> Send<T>(HttpMethod method, string uri, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string? correlation = NexaConnect.Observability.CorrelationContext.Current;
        request.Headers.Add("X-Correlation-ID", correlation is { Length: > 0 and <= 128 } && correlation.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-') ? correlation : Guid.NewGuid().ToString("N"));
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new System.IO.InvalidDataException("PIN service response was empty.");
    }
    public void Dispose() => client.Dispose();
}
