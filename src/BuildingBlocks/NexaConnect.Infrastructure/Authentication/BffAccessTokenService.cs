using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NexaConnect.Infrastructure.Authentication;

public sealed class BffAccessTokenService(
    IHttpClientFactory clients,
    TimeProvider timeProvider,
    ILogger<BffAccessTokenService> logger)
{
    public async Task<string?> GetValidAccessTokenAsync(
        HttpContext context,
        string cookieScheme,
        string authority,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        AuthenticateResult authentication = await context.AuthenticateAsync(cookieScheme);
        if (!authentication.Succeeded || authentication.Principal is null || authentication.Properties is null)
            return null;

        string? accessToken = authentication.Properties.GetTokenValue("access_token");
        string? expiresAtValue = authentication.Properties.GetTokenValue("expires_at");
        if (!string.IsNullOrWhiteSpace(accessToken)
            && DateTimeOffset.TryParse(expiresAtValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expiresAt)
            && expiresAt > timeProvider.GetUtcNow().AddMinutes(1))
            return accessToken;

        string? refreshToken = authentication.Properties.GetTokenValue("refresh_token");
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            logger.LogInformation("BFF session requires reauthentication because no usable refresh token is available");
            await context.SignOutAsync(cookieScheme);
            return null;
        }

        try
        {
            HttpClient client = clients.CreateClient(nameof(BffAccessTokenService));
            using HttpResponseMessage discovery = await client.GetAsync(
                $"{authority.TrimEnd('/')}/.well-known/openid-configuration", cancellationToken);
            discovery.EnsureSuccessStatusCode();
            OpenIdConfiguration? configuration = await discovery.Content.ReadFromJsonAsync<OpenIdConfiguration>(cancellationToken);
            if (string.IsNullOrWhiteSpace(configuration?.TokenEndpoint)) throw new InvalidOperationException("OIDC token endpoint is unavailable.");

            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret
                })
            };
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("BFF token refresh was rejected with status {StatusCode}; the session will be cleared", (int)response.StatusCode);
                await context.SignOutAsync(cookieScheme);
                return null;
            }

            TokenRefreshResponse? refreshed = await response.Content.ReadFromJsonAsync<TokenRefreshResponse>(cancellationToken);
            if (string.IsNullOrWhiteSpace(refreshed?.AccessToken)) throw new InvalidOperationException("OIDC refresh response did not contain an access token.");

            authentication.Properties.UpdateTokenValue("access_token", refreshed.AccessToken);
            authentication.Properties.UpdateTokenValue("expires_at", timeProvider.GetUtcNow().AddSeconds(refreshed.ExpiresIn).ToString("O", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                authentication.Properties.UpdateTokenValue("refresh_token", refreshed.RefreshToken);
            await context.SignInAsync(cookieScheme, authentication.Principal, authentication.Properties);
            logger.LogInformation("BFF access token refreshed successfully");
            return refreshed.AccessToken;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(exception, "BFF token refresh failed; the session will be cleared");
            await context.SignOutAsync(cookieScheme);
            return null;
        }
    }

    private sealed record OpenIdConfiguration([property: JsonPropertyName("token_endpoint")] string TokenEndpoint);
    private sealed record TokenRefreshResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
