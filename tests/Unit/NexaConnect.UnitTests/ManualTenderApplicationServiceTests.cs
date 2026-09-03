using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.ManualTenders;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.UnitTests;

public sealed class ManualTenderApplicationServiceTests
{
    [Fact]
    public async Task Confirm_is_attributed_atomic_and_matching_replay_returns_original()
    {
        var repository=new Repository();var service=new ManualTenderApplicationService(repository);
        var command=repository.Command("cash",false);
        ManualTenderResult first=Assert.IsType<ManualTenderResult>(await service.ConfirmAsync(command,default));
        ManualTenderResult replay=Assert.IsType<ManualTenderResult>(await service.ConfirmAsync(command,default));
        Assert.False(first.Replayed);Assert.True(replay.Replayed);Assert.Equal(first.SettlementId,replay.SettlementId);
        Assert.Equal(1,repository.Commits);Assert.Equal("operator-1",repository.Audit!.SubjectId);
        Assert.Equal("order.manual-tender.settled",repository.Audit.Action);Assert.Equal(OrderStatus.Paid,repository.Order.Status);
    }

    [Fact]
    public async Task Reused_key_with_different_payload_is_rejected_without_second_commit()
    {
        var repository=new Repository();var service=new ManualTenderApplicationService(repository);
        await service.ConfirmAsync(repository.Command("cash",false),default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ConfirmAsync(repository.Command("promptpay_manual",true),default));
        Assert.Equal(1,repository.Commits);
    }

    private sealed class Repository:IOrderRepository,IOrderLookup,IManualTenderRepository
    {
        public OrderAggregate Order{get;}=OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),[new OrderLine(Guid.NewGuid(),"Pad thai",120m,1,"kitchen")],"THB",Guid.NewGuid());
        public StoredManualTender? Stored{get;private set;} public PlatformAuditEventV1? Audit{get;private set;} public int Commits{get;private set;}
        public Repository(){Order.Submit();Order.MarkInventoryReserved();Order.MarkKitchenAccepted();}
        public ConfirmManualTenderCommand Command(string method,bool receipt)=>new(Order.OrganizationId,Order.BranchId,Order.Id,Guid.NewGuid(),FixedKey,method,120m,"THB",receipt,null,"operator-1",Guid.NewGuid(),Guid.NewGuid());
        private static readonly Guid FixedKey=Guid.Parse("fcab080c-1ee6-46f1-9f97-4dcf717c9436");
        public Task SaveAsync(OrderAggregate order,CancellationToken cancellationToken)=>Task.CompletedTask;
        public Task<OrderAggregate?> GetAsync(Guid orderId,CancellationToken cancellationToken)=>Task.FromResult<OrderAggregate?>(orderId==Order.Id?Order:null);
        public Task<StoredManualTender?> FindAsync(Guid organizationId,Guid branchId,Guid idempotencyKey,CancellationToken cancellationToken)=>Task.FromResult(Stored);
        public Task<ManualTenderCommitResult> CommitAsync(OrderAggregate order,ManualTenderSettlement settlement,string fingerprint,Guid authorizationDecisionId,OrderManualTenderSettledV1 integrationEvent,PlatformAuditEventV1 audit,CancellationToken cancellationToken)
        {Commits++;Audit=audit;Stored=new(settlement.Id,order.Id,fingerprint,integrationEvent.Method,settlement.Amount,settlement.Currency,settlement.OccurredAtUtc);return Task.FromResult(new ManualTenderCommitResult(ManualTenderCommitStatus.Created,Stored));}
    }
}
