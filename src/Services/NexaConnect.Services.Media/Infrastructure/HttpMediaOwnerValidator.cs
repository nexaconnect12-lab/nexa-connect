using System.Net;
using System.Net.Http.Headers;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Media.Application;

namespace NexaConnect.Services.Media.Infrastructure;

public sealed class HttpMediaOwnerValidator(HttpClient client, IServiceWorkloadTokenProvider tokens, ILogger<HttpMediaOwnerValidator> logger) : IMediaOwnerValidator
{
    public async Task<bool> ExistsAsync(Guid organizationId, string ownerService, string ownerType, Guid ownerId, CancellationToken cancellationToken)
    {
        if (ownerService != "catalog" || ownerType != "product") return false;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/catalog/v1/internal/organizations/{organizationId:D}/products/{ownerId:D}/exists");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(cancellationToken));
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return true;
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        logger.LogWarning("Media owner validation dependency failed for organization {OrganizationId}, status {StatusCode}", organizationId, (int)response.StatusCode);
        throw new HttpRequestException("Catalog owner validation failed.", null, response.StatusCode);
    }
}
