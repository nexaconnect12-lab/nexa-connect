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
}
