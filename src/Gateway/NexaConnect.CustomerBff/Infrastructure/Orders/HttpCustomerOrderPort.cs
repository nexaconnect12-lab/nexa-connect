using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Orders;

namespace NexaConnect.CustomerBff.Infrastructure.Orders;

public sealed class HttpCustomerOrderPort(HttpClient client) : ICustomerOrderPort
{
    public Task<HttpResponseMessage> PlaceAsync(
        TenantContext tenant,
        Guid branchId,
        CustomerPlaceOrderRequest request,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            request.RestaurantId,
            tenant.OrganizationId,
            BranchId = branchId,
            request.Currency,
            request.PaymentMethod,
            request.IdempotencyKey,
            Lines = request.Lines,
            request.OrderId,
            request.CorrelationId
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/order/v1/workflows/place")
        {
            Content = JsonContent.Create(payload)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Add(TenantContextHeaders.OrganizationId, tenant.OrganizationId.ToString("D"));
        message.Headers.Add(TenantContextHeaders.ApplicationCode, tenant.ApplicationCode);
        message.Headers.Add(TenantContextHeaders.PortalRequest, "customer");
        return client.SendAsync(message, cancellationToken);
    }
}
