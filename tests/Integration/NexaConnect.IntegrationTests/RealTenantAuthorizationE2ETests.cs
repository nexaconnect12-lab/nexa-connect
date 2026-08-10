using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NexaConnect.IntegrationTests;

public sealed class RealTenantAuthorizationE2ETests
{
    [Fact]
    public async Task Real_platform_directory_and_restaurant_confirm_selected_branch_ownership()
    {
        string? platformDirectoryUrl = Environment.GetEnvironmentVariable("NEXACONNECT_E2E_PLATFORM_DIRECTORY_URL");
        string? restaurantUrl = Environment.GetEnvironmentVariable("NEXACONNECT_E2E_RESTAURANT_URL");
        string? userAccessToken = Environment.GetEnvironmentVariable("NEXACONNECT_E2E_USER_ACCESS_TOKEN");
        string? organizationValue = Environment.GetEnvironmentVariable("NEXACONNECT_E2E_ORGANIZATION_ID");
        string? branchValue = Environment.GetEnvironmentVariable("NEXACONNECT_E2E_BRANCH_ID");
        if (string.IsNullOrWhiteSpace(platformDirectoryUrl)
            || string.IsNullOrWhiteSpace(restaurantUrl)
            || string.IsNullOrWhiteSpace(userAccessToken)
            || !Guid.TryParse(organizationValue, out Guid organizationId)
            || !Guid.TryParse(branchValue, out Guid branchId))
            return;

        using var platform = new HttpClient { BaseAddress = new Uri(platformDirectoryUrl) };
        platform.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);
        using HttpResponseMessage accessResponse = await platform.GetAsync(
            $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        Assert.Equal(System.Net.HttpStatusCode.OK, accessResponse.StatusCode);

        string? workloadToken = await GetWorkloadTokenAsync();
        if (string.IsNullOrWhiteSpace(workloadToken))
            return;
        using var restaurant = new HttpClient { BaseAddress = new Uri(restaurantUrl) };
        restaurant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", workloadToken);
        using HttpResponseMessage scopeResponse = await restaurant.GetAsync(
            $"api/restaurant/v1/branches/{branchId:D}/authorization-scope");
        Assert.Equal(System.Net.HttpStatusCode.OK, scopeResponse.StatusCode);
        using JsonDocument scope = JsonDocument.Parse(await scopeResponse.Content.ReadAsStringAsync());
        Assert.Equal(organizationId, scope.RootElement.GetProperty("organizationId").GetGuid());
        Assert.Equal(branchId, scope.RootElement.GetProperty("branchId").GetGuid());
    }

    private static async Task<string?> GetWorkloadTokenAsync()
    {
        string? endpoint = Environment.GetEnvironmentVariable("NEXACONNECT_E2E_TOKEN_ENDPOINT");
        string? clientId = Environment.GetEnvironmentVariable("NEXACONNECT_E2E_WORKLOAD_CLIENT_ID");
        string? clientSecret = Environment.GetEnvironmentVariable("NEXACONNECT_E2E_WORKLOAD_CLIENT_SECRET");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return null;
        using var client = new HttpClient();
        using var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        }));
        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("access_token").GetString();
    }
}
