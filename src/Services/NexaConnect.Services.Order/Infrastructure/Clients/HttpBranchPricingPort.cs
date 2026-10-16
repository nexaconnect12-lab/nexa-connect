using System.Net.Http.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Infrastructure.Clients;

public sealed class HttpBranchPricingPort(HttpClient client, ILogger<HttpBranchPricingPort> logger) : IBranchPricingPort
{
    public async Task<PricingPolicy> GetAsync(Guid organizationId, Guid restaurantId, Guid branchId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/restaurant/v1/branches/{branchId:D}/pricing");
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId, organizationId.ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Order pricing dependency rejected with status {StatusCode}", (int)response.StatusCode);
            throw new HttpRequestException("Branch pricing is unavailable.", null, response.StatusCode);
        }
        var value = await response.Content.ReadFromJsonAsync<BranchPricingResponse>(cancellationToken)
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
