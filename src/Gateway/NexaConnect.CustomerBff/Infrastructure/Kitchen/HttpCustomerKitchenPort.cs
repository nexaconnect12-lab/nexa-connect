using System.Net.Http.Headers;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Kitchen;

namespace NexaConnect.CustomerBff.Infrastructure.Kitchen;

public sealed class HttpCustomerKitchenPort(HttpClient client, IHttpClientFactory clients) : ICustomerKitchenPort
{
    public async Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "api/platform-directory/v1/me/access");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await clients.CreateClient("PlatformDirectory").SendAsync(message, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken: ct) : null;
    }

    public async Task<HttpResponseMessage> SendAsync(TenantContext tenant, string token, KitchenOperation operation, KitchenRequest request, CancellationToken ct)
    {
        string path = $"api/kitchen/v1/branches/{request.BranchId:D}/tickets";
        path += operation switch
        {
            KitchenOperation.Queue => $"?limit={request.Limit}&station={Uri.EscapeDataString(request.Station ?? "")}&cursor={Uri.EscapeDataString(request.Cursor ?? "")}",
            KitchenOperation.Detail => $"/{request.TicketId:D}",
            KitchenOperation.Transition => $"/{request.TicketId:D}/transitions",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        using var message = new HttpRequestMessage(operation == KitchenOperation.Transition ? HttpMethod.Post : HttpMethod.Get, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add(TenantContextHeaders.OrganizationId, tenant.OrganizationId.ToString("D"));
        message.Headers.Add(TenantContextHeaders.ApplicationCode, tenant.ApplicationCode);
        message.Headers.Add(TenantContextHeaders.PortalRequest, "customer");
        if (operation == KitchenOperation.Transition) message.Content = JsonContent.Create(request.Transition);
        return await client.SendAsync(message, ct);
    }
}
