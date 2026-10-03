using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Payment.Application.Tenant;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class HttpPaymentTenantAuthorizer(
    IHttpClientFactory clients,
    IServiceWorkloadTokenProvider tokens,
    ProductAuthorizationClient authorization) : IPaymentTenantAuthorizer
{
    public async Task<bool> CanReadBranchFinancialsAsync(Guid organizationId, Guid restaurantId, Guid branchId,
        string authorizationHeader, CancellationToken ct)
    {
        if (organizationId == Guid.Empty || restaurantId == Guid.Empty || branchId == Guid.Empty
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out var bearer) || !bearer.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)) return false;
        using var access = new HttpRequestMessage(HttpMethod.Get, $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        access.Headers.Authorization = bearer;
        using var accessResponse = await clients.CreateClient("PaymentPlatformDirectory").SendAsync(access, ct);
        if (!accessResponse.IsSuccessStatusCode) return false;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/restaurant/v1/branches/{branchId:D}/authorization-scope");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(ct));
        using var response = await clients.CreateClient("PaymentRestaurant").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var scope = await response.Content.ReadFromJsonAsync<BranchScope>(cancellationToken: ct);
        return scope is not null && scope.OrganizationId == organizationId && scope.RestaurantId == restaurantId && scope.BranchId == branchId
            && await authorization.IsGrantedAsync(organizationId, restaurantId, branchId, ProductPermissions.PaymentIntentRead, authorizationHeader, ct)
            && await authorization.IsGrantedAsync(organizationId, restaurantId, branchId, ProductPermissions.PaymentRefundRead, authorizationHeader, ct);
    }
    public async Task<bool> CanAccessAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId, string permission,
        string authorizationHeader, CancellationToken cancellationToken) =>
        (await DecideAsync(organizationId, restaurantId, branchId, orderId, permission, authorizationHeader, cancellationToken)).Granted;

    public async Task<PaymentAccessDecision> DecideAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId, string permission,
        string authorizationHeader, CancellationToken cancellationToken, decimal? amount = null, string? currency = null)
    {
        if (organizationId == Guid.Empty || restaurantId == Guid.Empty || branchId == Guid.Empty || orderId == Guid.Empty
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? customerAuthorization))
            return new(false, Guid.Empty);

        using var accessRequest = new HttpRequestMessage(HttpMethod.Get,
            $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        accessRequest.Headers.Authorization = customerAuthorization;
        using HttpResponseMessage accessResponse = await clients.CreateClient("PaymentPlatformDirectory")
            .SendAsync(accessRequest, cancellationToken);
        if (!accessResponse.IsSuccessStatusCode) return new(false, Guid.Empty);

        string workloadToken = await tokens.GetAsync(cancellationToken);
        using var orderRequest = new HttpRequestMessage(HttpMethod.Get, $"api/order/v1/orders/{orderId:D}");
        orderRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", workloadToken);
        using HttpResponseMessage orderResponse = await clients.CreateClient("PaymentOrder").SendAsync(orderRequest, cancellationToken);
        if (!orderResponse.IsSuccessStatusCode) return new(false, Guid.Empty);
        OrderScope? order = await orderResponse.Content.ReadFromJsonAsync<OrderScope>(cancellationToken: cancellationToken);
        if (order is null || order.OrganizationId != organizationId || order.BranchId != branchId) return new(false, Guid.Empty);

        using var branchRequest = new HttpRequestMessage(HttpMethod.Get,
            $"api/restaurant/v1/branches/{branchId:D}/authorization-scope");
        branchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", workloadToken);
        using HttpResponseMessage branchResponse = await clients.CreateClient("PaymentRestaurant")
            .SendAsync(branchRequest, cancellationToken);
        if (!branchResponse.IsSuccessStatusCode) return new(false, Guid.Empty);
        BranchScope? branch = await branchResponse.Content.ReadFromJsonAsync<BranchScope>(cancellationToken: cancellationToken);
        if (branch is null || branch.OrganizationId != organizationId || branch.RestaurantId != restaurantId
            || branch.BranchId != branchId) return new(false, Guid.Empty);
        ProductAuthorizationClient.Decision? decision = await authorization.DecideAsync(organizationId, restaurantId,
            branchId, permission, authorizationHeader, cancellationToken, amount, currency);
        return decision is null ? new(false, Guid.Empty) : new(decision.Granted, decision.DecisionId, decision.EvaluatedLimit);
    }

    private sealed record OrderScope(Guid OrganizationId, Guid BranchId);
    private sealed record BranchScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId);
}
