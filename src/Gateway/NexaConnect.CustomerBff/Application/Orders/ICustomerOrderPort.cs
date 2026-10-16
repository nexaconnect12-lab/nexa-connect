using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Orders;

public interface ICustomerOrderPort
{
    Task<HttpResponseMessage> QuoteAsync(TenantContext tenant, Guid branchId, CustomerPlaceOrderRequest request,
        string accessToken, CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<HttpResponseMessage> PlaceAsync(
        TenantContext tenant,
        Guid branchId,
        CustomerPlaceOrderRequest request,
        string accessToken,
        CancellationToken cancellationToken);
}

public sealed record CustomerPlaceOrderRequest(
    Guid RestaurantId,
    string Currency,
    string PaymentMethod,
    string IdempotencyKey,
    IReadOnlyCollection<CustomerOrderLine> Lines,
    Guid? OrderId = null,
    Guid? CorrelationId = null, string? PricingFingerprint = null);

public sealed record CustomerOrderLine(Guid ProductId, int Quantity);
