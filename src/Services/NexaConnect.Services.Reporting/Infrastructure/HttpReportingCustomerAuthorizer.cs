using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Infrastructure;

public sealed class HttpReportingCustomerAuthorizer(HttpClient directory, ProductAuthorizationClient authorization) : IReportingCustomerAuthorizer
{
    public async Task<bool> IsGrantedAsync(Guid organizationId, Guid? branchId, string permission, string authorizationHeader, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        using HttpResponseMessage response = await directory.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode && await authorization.IsGrantedAsync(organizationId, null, branchId, permission, authorizationHeader, cancellationToken);
    }
}
