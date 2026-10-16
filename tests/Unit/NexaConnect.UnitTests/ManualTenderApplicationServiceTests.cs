using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.ManualTenders;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.UnitTests;

public sealed class ManualTenderApplicationServiceTests
{
    [Fact]
    public async Task Admission_rejection_preserves_unpaid_order_and_creates_no_receipt_or_commit()
    {
        var repository = new Repository();
        var service = new ManualTenderApplicationService(repository, new DeniedAdmission());
        await Assert.ThrowsAsync<ManualTenderAdmissionConflictException>(() => service.ConfirmAsync(repository.Command("cash", false), default));
        Assert.Equal(OrderStatus.KitchenAccepted, repository.Order.Status);
        Assert.Null(repository.Order.Receipt); Assert.Equal(0, repository.Commits);
    }

    [Fact]
    public async Task Committed_replay_does_not_require_current_terminal_readiness()
    {
        var repository = new Repository(); var command = repository.Command("cash", false);
        await new ManualTenderApplicationService(repository, new FixtureManualTenderAdmission()).ConfirmAsync(command, default);
        var result = await new ManualTenderApplicationService(repository, new DeniedAdmission()).ConfirmAsync(command, default);
        Assert.True(result!.Replayed); Assert.Equal(1, repository.Commits);
    }

    private sealed class DeniedAdmission : IManualTenderAdmission
    {
        public Task<DateTimeOffset> AdmitAsync(ManualTenderAdmissionRequest request, CancellationToken ct) =>
            throw new ManualTenderAdmissionConflictException();
    }

    [Theory]
    [InlineData(-31)]
    [InlineData(31)]
    public async Task Stale_or_future_admission_proof_never_commits_paid(int seconds)
    {
        var repository = new Repository(); var at = DateTimeOffset.UtcNow;
        var service = new ManualTenderApplicationService(repository, new AtAdmission(at.AddSeconds(seconds)), new FixedClock(at));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.ConfirmAsync(repository.Command("cash", false), default));
        Assert.Equal(OrderStatus.KitchenAccepted, repository.Order.Status); Assert.Equal(0, repository.Commits);
    }

    [Fact]
    public async Task Admission_outage_retains_unpaid_state_and_original_operation_can_retry()
    {
        var repository = new Repository(); var command = repository.Command("cash", false);
        await Assert.ThrowsAsync<HttpRequestException>(() => new ManualTenderApplicationService(repository, new UnavailableAdmission()).ConfirmAsync(command, default));
        Assert.Equal(0, repository.Commits); Assert.Null(repository.Order.Receipt);
        var result = await new ManualTenderApplicationService(repository, new FixtureManualTenderAdmission()).ConfirmAsync(command, default);
        Assert.False(result!.Replayed); Assert.Equal(1, repository.Commits);
    }

    [Fact]
    public async Task Settlement_and_receipt_retain_pos_observation_time()
    {
        var repository = new Repository(); var now = DateTimeOffset.UtcNow; var at = now.AddSeconds(-2);
        var result = await new ManualTenderApplicationService(repository, new AtAdmission(at), new FixedClock(now))
            .ConfirmAsync(repository.Command("cash", false), default);
        Assert.Equal(at, result!.OccurredAtUtc); Assert.Equal(at, repository.Order.Receipt!.PaidAtUtc);
    }

    private sealed class AtAdmission(DateTimeOffset at) : IManualTenderAdmission
    { public Task<DateTimeOffset> AdmitAsync(ManualTenderAdmissionRequest request, CancellationToken ct) => Task.FromResult(at); }
    private sealed class UnavailableAdmission : IManualTenderAdmission
    { public Task<DateTimeOffset> AdmitAsync(ManualTenderAdmissionRequest request, CancellationToken ct) => throw new HttpRequestException(); }
    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => at; }
    [Fact]
    public async Task Confirm_is_attributed_atomic_and_matching_replay_returns_original()
    {
        var repository=new Repository();var service=new ManualTenderApplicationService(repository,new FixtureManualTenderAdmission());
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
        var repository=new Repository();var service=new ManualTenderApplicationService(repository,new FixtureManualTenderAdmission());
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
