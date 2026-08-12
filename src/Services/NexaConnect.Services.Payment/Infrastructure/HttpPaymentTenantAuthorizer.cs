using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Payment.Application.Tenant;
using NexaConnect.Infrastructure.Authorization;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class HttpPaymentTenantAuthorizer(
    IHttpClientFactory clients,
    IServiceWorkloadTokenProvider tokens,
    ProductAuthorizationClient authorization) : IPaymentTenantAuthorizer
{
    public async Task<bool> CanAccessAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId, string permission,
        string authorizationHeader, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || restaurantId == Guid.Empty || branchId == Guid.Empty || orderId == Guid.Empty
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? customerAuthorization))
            return false;

        using var accessRequest = new HttpRequestMessage(HttpMethod.Get,
            $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        accessRequest.Headers.Authorization = customerAuthorization;
        using HttpResponseMessage accessResponse = await clients.CreateClient("PaymentPlatformDirectory")
            .SendAsync(accessRequest, cancellationToken);
        if (!accessResponse.IsSuccessStatusCode) return false;

        string workloadToken = await tokens.GetAsync(cancellationToken);
        using var orderRequest = new HttpRequestMessage(HttpMethod.Get, $"api/order/v1/orders/{orderId:D}");
        orderRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", workloadToken);
        using HttpResponseMessage orderResponse = await clients.CreateClient("PaymentOrder").SendAsync(orderRequest, cancellationToken);
        if (!orderResponse.IsSuccessStatusCode) return false;
        OrderScope? order = await orderResponse.Content.ReadFromJsonAsync<OrderScope>(cancellationToken: cancellationToken);
        if (order is null || order.OrganizationId != organizationId || order.BranchId != branchId) return false;

        using var branchRequest = new HttpRequestMessage(HttpMethod.Get,
            $"api/restaurant/v1/branches/{branchId:D}/authorization-scope");
        branchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", workloadToken);
        using HttpResponseMessage branchResponse = await clients.CreateClient("PaymentRestaurant")
            .SendAsync(branchRequest, cancellationToken);
        if (!branchResponse.IsSuccessStatusCode) return false;
        BranchScope? branch = await branchResponse.Content.ReadFromJsonAsync<BranchScope>(cancellationToken: cancellationToken);
        return branch is not null && branch.OrganizationId == organizationId && branch.RestaurantId == restaurantId
            && branch.BranchId == branchId
            && await authorization.IsGrantedAsync(organizationId, restaurantId, branchId, permission,
                authorizationHeader, cancellationToken);
    }

    private sealed record OrderScope(Guid OrganizationId, Guid BranchId);
    private sealed record BranchScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId);
}
