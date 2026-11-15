using NexaConnect.Services.POS.Domain.ManualRefunds;

namespace NexaConnect.UnitTests;

public sealed class ManualRefundLedgerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1);
    private static ManualRefundLedger Ledger(ManualRefundTender tender = ManualRefundTender.Cash) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tender, 100m, "THB", Now.AddHours(-1));
    private static ManualRefundApproval Approval(decimal amount) =>
        new(Guid.NewGuid(), amount, "customer_request", "manager", Guid.NewGuid(), "cashier", Now);
    private static ManualRefundPayout Payout(Guid? drawer) =>
        new(Guid.NewGuid(), Guid.NewGuid(), drawer, "cashier", Now.AddMinutes(1));

    [Fact]
    public void CashierLimitCountsCompletedAndReservedAmountsAndExactReplayRetainsAuthority()
    {
        var ledger = Ledger();
        var approval = Approval(20) with { ManagerSubject = "cashier", CashierLimit = 50 };
        Assert.Throws<CashRefundManagerApprovalRequiredException>(() => ledger.Approve(approval with { CashierLimit = 0 }));
        ledger.Approve(approval);
        var custody = Payout(Guid.NewGuid());
        ledger.BeginPayout(approval.OperationId, custody);
        ledger.Complete(approval.OperationId, custody, false, null, Now.AddMinutes(2));
        ledger.Approve(Approval(25));
        Assert.Throws<CashRefundManagerApprovalRequiredException>(() =>
            ledger.Approve(approval with { OperationId = Guid.NewGuid(), Amount = 6 }));
        var exact = ledger.Approve(approval with { OperationId = Guid.NewGuid(), Amount = 5 });
        Assert.Equal(50m, exact.Approval.CashierLimit);
        Assert.Same(exact, ledger.Approve(exact.Approval with { CashierLimit = 0, AuthorizationDecisionId = Guid.NewGuid() }));
        Assert.Throws<InvalidOperationException>(() => ledger.Approve(exact.Approval with { Amount = 4 }));
        Assert.Throws<ArgumentException>(() => Ledger(ManualRefundTender.PromptPay).Approve(approval));
        Assert.Equal(50m, ledger.AvailableAmount);
    }

    [Fact]
    public void ApprovalReservesBalanceAndExactRetryPreservesOriginalDecision()
    {
        var ledger = Ledger();
        var approval = Approval(65);
        Assert.Throws<ArgumentException>(() => ledger.Approve(approval with { ApprovedAtUtc = Now.AddHours(-2) }));
        var approved = ledger.Approve(approval);
        Assert.Equal(35, ledger.AvailableAmount);
        Assert.Same(approved, ledger.Approve(approval with
        {
            ApprovedAtUtc = Now.AddMinutes(1), AuthorizationDecisionId = Guid.NewGuid()
        }));
        Assert.Throws<InvalidOperationException>(() => ledger.Approve(approval with { Amount = 64 }));
        Assert.Throws<InvalidOperationException>(() => ledger.Approve(Approval(36)));
        ledger.Approve(Approval(35));
        Assert.Equal(0, ledger.AvailableAmount);
    }

    [Fact]
    public void CashRequiresSeparateApproverAndExactCashierDrawerCustody()
    {
        var ledger = Ledger();
        var approval = Approval(50);
        Assert.Throws<ArgumentException>(() => ledger.Approve(approval with { ManagerSubject = approval.CashierSubject }));
        ledger.Approve(approval);
        var payout = Payout(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => ledger.Complete(approval.OperationId, payout, false, null, Now.AddMinutes(2)));
        Assert.Throws<UnauthorizedAccessException>(() =>
            ledger.BeginPayout(approval.OperationId, payout with { CashierSubject = approval.ManagerSubject }));
        Assert.Throws<ArgumentException>(() => ledger.BeginPayout(approval.OperationId, payout with { CashSessionId = null }));
        var started = ledger.BeginPayout(approval.OperationId, payout);
        Assert.Same(started, ledger.BeginPayout(approval.OperationId, payout with { StartedAtUtc = Now.AddMinutes(2) }));
        Assert.Throws<InvalidOperationException>(() =>
            ledger.BeginPayout(approval.OperationId, payout with { CashSessionId = Guid.NewGuid() }));
        Assert.Throws<UnauthorizedAccessException>(() =>
            ledger.Complete(approval.OperationId, payout with { ShiftId = Guid.NewGuid() }, false, null, Now.AddMinutes(2)));
        Assert.Throws<ArgumentException>(() => ledger.Complete(approval.OperationId, payout, true, "transfer", Now.AddMinutes(2)));
        var completed = ledger.Complete(approval.OperationId, payout, false, null, Now.AddMinutes(2));
        Assert.Same(completed, ledger.Complete(approval.OperationId, payout, false, null, Now.AddMinutes(3)));
        Assert.Equal(50, ledger.AvailableAmount);
    }

    [Fact]
    public void PromptPayRequiresVerifiedTransferReferenceAndHasNoDrawerCustody()
    {
        var ledger = Ledger(ManualRefundTender.PromptPay);
        var approval = Approval(50);
        ledger.Approve(approval);
        var payout = Payout(null);
        Assert.Throws<ArgumentException>(() => ledger.BeginPayout(approval.OperationId, payout with { CashSessionId = Guid.NewGuid() }));
        ledger.BeginPayout(approval.OperationId, payout);
        Assert.Throws<ArgumentException>(() => ledger.Complete(approval.OperationId, payout, true, null, Now.AddMinutes(2)));
        Assert.Throws<ArgumentException>(() => ledger.Complete(approval.OperationId, payout, false, "verified-ref", Now.AddMinutes(2)));
        var completed = ledger.Complete(approval.OperationId, payout, true, "  verified-ref  ", Now.AddMinutes(2));
        Assert.Equal("verified-ref", completed.Completion?.TransferReference);
        Assert.Same(completed, ledger.Complete(approval.OperationId, payout, true, "verified-ref", Now.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => ledger.Complete(approval.OperationId, payout, true, "other-ref", Now.AddMinutes(3)));
        var another = Approval(50);
        ledger.Approve(another);
        ledger.BeginPayout(another.OperationId, payout);
        Assert.Throws<ManualRefundReferenceConflictException>(() => ledger.Complete(another.OperationId, payout, true, "verified-ref", Now.AddMinutes(3)));
    }
}
