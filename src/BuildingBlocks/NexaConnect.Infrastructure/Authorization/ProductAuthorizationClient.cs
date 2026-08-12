using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace NexaConnect.Infrastructure.Authorization;

public sealed class ProductAuthorizationClient(HttpClient client, ILogger<ProductAuthorizationClient> logger)
{
    public async Task<bool> IsGrantedAsync(
        Guid organizationId,
        Guid? restaurantId,
        Guid? branchId,
        string permission,
        string authorizationHeader,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || string.IsNullOrWhiteSpace(permission)
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? authorization))
            return false;

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/authorization/v1/decisions")
        {
            Content = JsonContent.Create(new AuthorizationDecisionRequest(
                organizationId, restaurantId, branchId, permission, null, null))
        };
        request.Headers.Authorization = authorization;
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Authorization dependency returned {StatusCode} for permission {Permission} in organization {OrganizationId}",
                (int)response.StatusCode, permission, organizationId);
            return false;
        }
        AuthorizationDecisionResponse? decision = await response.Content.ReadFromJsonAsync<AuthorizationDecisionResponse>(
            cancellationToken: cancellationToken);
        bool granted = decision?.Granted == true;
        if (!granted)
            logger.LogWarning("Product permission {Permission} denied in organization {OrganizationId}, restaurant {RestaurantId}, branch {BranchId}",
                permission, organizationId, restaurantId, branchId);
        return granted;
    }

    private sealed record AuthorizationDecisionRequest(
        Guid OrganizationId, Guid? RestaurantId, Guid? BranchId, string Permission, decimal? Amount, string? Currency);
    private sealed record AuthorizationDecisionResponse(Guid DecisionId, bool Granted, decimal? EvaluatedLimit);
}
