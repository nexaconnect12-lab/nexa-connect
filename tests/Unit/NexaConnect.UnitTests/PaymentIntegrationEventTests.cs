using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.UnitTests;

public sealed class PaymentIntegrationEventTests
{
    [Fact]
    public void Intent_created_contract_preserves_tenant_financial_and_resource_context()
    {
        var paymentEvent = new PaymentIntentCreatedV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 12.50m, "USD", "cash", "pending");

        PaymentIntentCreatedV1 copy = JsonSerializer.Deserialize<PaymentIntentCreatedV1>(JsonSerializer.Serialize(paymentEvent))!;

        Assert.Equal(paymentEvent.OrganizationId, copy.OrganizationId);
        Assert.Equal(paymentEvent.PaymentIntentId, copy.PaymentIntentId);
        Assert.Equal(paymentEvent.Amount, copy.Amount);
        Assert.Equal("pending", copy.Status);
    }

    [Fact]
    public void Authorization_contracts_preserve_tenant_order_and_safe_outcome_context()
    {
        var authorized = new PaymentAuthorizedV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 12.50m, "USD", "card");
        var failed = new PaymentAuthorizationFailedV1(Guid.NewGuid(), authorized.CorrelationId, DateTimeOffset.UtcNow,
            authorized.OrganizationId, authorized.RestaurantId, authorized.BranchId, authorized.OrderId,
            authorized.PaymentIntentId, "provider_declined");

        PaymentAuthorizedV1 copy = JsonSerializer.Deserialize<PaymentAuthorizedV1>(JsonSerializer.Serialize(authorized))!;
        PaymentAuthorizationFailedV1 failedCopy = JsonSerializer.Deserialize<PaymentAuthorizationFailedV1>(JsonSerializer.Serialize(failed))!;

        Assert.Equal(authorized.OrderId, copy.OrderId);
        Assert.Equal("provider_declined", failedCopy.FailureCode);
    }
}
