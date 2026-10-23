using System.Net.Http.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Infrastructure.Clients;

public sealed class HttpBranchPricingPort(HttpClient client, ILogger<HttpBranchPricingPort> logger) : IBranchPricingPort
{
    public async Task<PricingPolicy> GetAsync(Guid organizationId, Guid restaurantId, Guid branchId, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/restaurant/v1/branches/{branchId:D}/pricing");
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId, organizationId.ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Order pricing dependency rejected with status {StatusCode}", (int)response.StatusCode);
            throw new HttpRequestException("Branch pricing is unavailable.", null, response.StatusCode);
        }
        await response.Content.LoadIntoBufferAsync(4096, deadline.Token);
        var value = await response.Content.ReadFromJsonAsync<BranchPricingResponse>(deadline.Token)
            ?? throw new HttpRequestException("Branch pricing is unavailable.");
        if (value.OrganizationId != organizationId || value.RestaurantId != restaurantId || value.BranchId != branchId)
        {
            logger.LogWarning("Order pricing dependency scope mismatch");
            throw new ArgumentException("Branch pricing scope does not match checkout.");
        }
        return new(value.ConcurrencyVersion, value.TaxPercent, value.TaxInclusive, value.ServiceChargePercent);
    }
    private sealed record BranchPricingResponse(Guid OrganizationId, Guid RestaurantId, Guid BranchId,
        long ConcurrencyVersion, decimal TaxPercent, bool TaxInclusive, decimal ServiceChargePercent);
}
