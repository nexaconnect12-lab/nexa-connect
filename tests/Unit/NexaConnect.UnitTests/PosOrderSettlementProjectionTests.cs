using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.POS.Application.OrderSettlements;

namespace NexaConnect.UnitTests;

public sealed class PosOrderSettlementProjectionTests
{
    [Theory]
    [InlineData("cash")]
    [InlineData("promptpay_manual")]
    public async Task Supported_thb_settlement_is_forwarded_without_sensitive_evidence(string method)
    {
        var store=new Store();var service=new OrderSettlementProjectionService(store);var value=Event(method);
        Assert.Equal(OrderSettlementProjectionStatus.Applied,await service.ProjectAsync(value,default));
        Assert.Same(value,store.Value);
    }

    [Theory]
    [InlineData("card","THB",10)]
    [InlineData("cash","USD",10)]
    [InlineData("cash","THB",0)]
    public async Task Unsupported_or_incomplete_settlement_is_rejected(string method,string currency,decimal amount)
    {
        var service=new OrderSettlementProjectionService(new Store());var value=Event(method) with{Currency=currency,Amount=amount};
        await Assert.ThrowsAsync<ArgumentException>(()=>service.ProjectAsync(value,default));
    }

    private static OrderManualTenderSettledV1 Event(string method)=>new(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,
        Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),method,120m,"THB");
    private sealed class Store:IOrderSettlementProjectionStore
    {
        public OrderManualTenderSettledV1? Value{get;private set;}
        public Task<OrderSettlementProjectionStatus> ProjectAsync(OrderManualTenderSettledV1 settlement,CancellationToken cancellationToken)
        {Value=settlement;return Task.FromResult(OrderSettlementProjectionStatus.Applied);}
    }
}
