using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.ManualRefunds;
using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Infrastructure.ManualRefunds;

public sealed class HttpManualRefundSaleReader(HttpClient client, IConfiguration configuration, IHostEnvironment environment,
    ILogger<HttpManualRefundSaleReader> logger) : IManualRefundSaleReader
{
    public async Task<ManualRefundSaleV1?> ReadAsync(RestaurantAuthorizationScope scope, Guid orderId, PosUserContext user, CancellationToken ct)
    {
        var address = ManualRefundDependencyAddress.Resolve(configuration, environment, "Order",
            $"api/order/v1/orders/{orderId:D}/manual-refund-sale?restaurantId={scope.RestaurantId:D}&branchId={scope.BranchId:D}");
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        request.Headers.Add(TenantContextHeaders.OrganizationId, scope.OrganizationId.ToString("D"));
        request.Headers.Add(TenantContextHeaders.ApplicationCode, "nexa_connect");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Manual refund Order evidence boundary {StatusCode}", (int)response.StatusCode);
            throw new HttpRequestException("Original manual settlement evidence is unavailable.");
        }
        await response.Content.LoadIntoBufferAsync(8192, ct);
        return await response.Content.ReadFromJsonAsync<ManualRefundSaleV1>(ct)
            ?? throw new HttpRequestException("Original manual settlement evidence is empty.");
    }
}
