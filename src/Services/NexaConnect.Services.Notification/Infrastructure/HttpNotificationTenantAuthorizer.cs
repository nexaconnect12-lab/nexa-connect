using System.Net.Http.Headers;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Notification.Application.Tenant;

namespace NexaConnect.Services.Notification.Infrastructure;

public sealed class HttpNotificationTenantAuthorizer(IHttpClientFactory clients, ProductAuthorizationClient authorization) : INotificationTenantAuthorizer
{
    public async Task<bool> CanAccessAsync(Guid organizationId, string permission, string authorizationHeader, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || !AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? customerAuthorization)) return false;
        using var access = new HttpRequestMessage(HttpMethod.Get, $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        access.Headers.Authorization = customerAuthorization;
        using HttpResponseMessage response = await clients.CreateClient("NotificationPlatformDirectory").SendAsync(access, cancellationToken);
        return response.IsSuccessStatusCode && await authorization.IsGrantedAsync(organizationId, null, null, permission, authorizationHeader, cancellationToken);
    }
}
