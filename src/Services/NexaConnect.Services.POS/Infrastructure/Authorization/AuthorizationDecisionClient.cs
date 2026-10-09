using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Infrastructure.Authorization;

public sealed class AuthorizationDecisionClient(
    IHttpClientFactory clients,
    IConfiguration configuration) : IAuthorizationDecisionClient
{
    public Task<AuthorizationDecision> DecideAsync(
        PosUserContext user,
        RestaurantAuthorizationScope scope,
        string permission,
        CancellationToken cancellationToken)=>DecideCoreAsync(user,scope,permission,null,null,cancellationToken);
    public Task<AuthorizationDecision> DecideForAmountAsync(PosUserContext user,RestaurantAuthorizationScope scope,string permission,decimal amount,string currency,CancellationToken ct)=>DecideCoreAsync(user,scope,permission,amount,currency,ct);
    private async Task<AuthorizationDecision> DecideCoreAsync(PosUserContext user,RestaurantAuthorizationScope scope,string permission,decimal? amount,string? currency,CancellationToken cancellationToken)
    {
        using HttpClient client = clients.CreateClient("Authorization");
        client.BaseAddress = new Uri(configuration["Services:Authorization"]
            ?? throw new InvalidOperationException("Services:Authorization is required."));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "api/authorization/v1/decisions",
            new
            {
                scope.OrganizationId,
                RestaurantId = (Guid?)scope.RestaurantId,
                BranchId = (Guid?)scope.BranchId,
                Permission = permission,
                Amount = amount,
                Currency = currency
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthorizationDecision>(cancellationToken)
            ?? throw new InvalidOperationException("Authorization returned an empty decision.");
    }
}
