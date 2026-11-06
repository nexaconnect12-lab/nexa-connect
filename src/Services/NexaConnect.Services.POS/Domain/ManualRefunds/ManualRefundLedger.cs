namespace NexaConnect.Services.POS.Domain.ManualRefunds;

public enum ManualRefundTender { Cash, PromptPay }

public sealed record ManualRefundApproval(Guid OperationId, decimal Amount, string Reason,
    string ManagerSubject, Guid AuthorizationDecisionId, string CashierSubject, DateTimeOffset ApprovedAtUtc);

public sealed record ManualRefundPayout(Guid TerminalId, Guid ShiftId, Guid? CashSessionId,
    string CashierSubject, DateTimeOffset StartedAtUtc);

public sealed record ManualRefundCompletion(string? TransferReference, DateTimeOffset RecordedAtUtc);

public sealed record ManualRefund(ManualRefundApproval Approval, ManualRefundPayout? Payout = null,
    ManualRefundCompletion? Completion = null);

// One original verified manual settlement is the balance boundary. The repository must
// persist each transition under an exclusive lock for this scope and original order.
public sealed class ManualRefundLedger
{
    private readonly Dictionary<Guid, ManualRefund> refunds = [];

    public ManualRefundLedger(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId,
        Guid settlementId, ManualRefundTender tender, decimal paidAmount, string currency, DateTimeOffset paidAtUtc)
    {
        if (organizationId == Guid.Empty || restaurantId == Guid.Empty || branchId == Guid.Empty
            || orderId == Guid.Empty || settlementId == Guid.Empty)
            throw new ArgumentException("The original sale scope is required.");
        if (!Enum.IsDefined(tender) || currency != "THB" || !ValidAmount(paidAmount) || paidAtUtc == default)
            throw new ArgumentException("A supported original THB manual settlement is required.");

        OrganizationId = organizationId;
        RestaurantId = restaurantId;
        BranchId = branchId;
        OrderId = orderId;
        SettlementId = settlementId;
        Tender = tender;
        PaidAmount = paidAmount;
        Currency = currency;
        PaidAtUtc = paidAtUtc;
    }

    public Guid OrganizationId { get; }
    public Guid RestaurantId { get; }
    public Guid BranchId { get; }
    public Guid OrderId { get; }
    public Guid SettlementId { get; }
    public ManualRefundTender Tender { get; }
    public decimal PaidAmount { get; }
    public string Currency { get; }
    public DateTimeOffset PaidAtUtc { get; }
    public decimal AvailableAmount => PaidAmount - refunds.Values.Sum(refund => refund.Approval.Amount);

    public ManualRefund Approve(ManualRefundApproval approval)
    {
        if (approval.OperationId == Guid.Empty || approval.AuthorizationDecisionId == Guid.Empty
            || !ValidAmount(approval.Amount) || !ValidSubject(approval.ManagerSubject)
            || !ValidSubject(approval.CashierSubject) || approval.ManagerSubject == approval.CashierSubject
            || approval.ApprovedAtUtc < PaidAtUtc
            || approval.Reason is not ("customer_request" or "duplicate_charge" or "item_unavailable" or "service_issue" or "other"))
            throw new ArgumentException("Valid refund approval, cashier and reason are required.");

        if (refunds.TryGetValue(approval.OperationId, out var existing))
        {
            // A live authorization decision can change on retry. Preserve the decision
            // and time that authorized the original approval.
            if (existing.Approval with
                {
                    ApprovedAtUtc = approval.ApprovedAtUtc,
                    AuthorizationDecisionId = approval.AuthorizationDecisionId
                } != approval)
                throw new InvalidOperationException("The original refund approval differs from this request.");
            return existing;
        }

        if (approval.Amount > AvailableAmount)
            throw new InvalidOperationException("The refund exceeds the remaining paid amount.");
        var refund = new ManualRefund(approval);
        refunds.Add(approval.OperationId, refund);
        return refund;
    }

    public ManualRefund BeginPayout(Guid operationId, ManualRefundPayout payout)
    {
        var refund = Find(operationId);
        if (payout.CashierSubject != refund.Approval.CashierSubject)
            throw new UnauthorizedAccessException("The approved cashier must complete the refund.");
        if (payout.TerminalId == Guid.Empty || payout.ShiftId == Guid.Empty
            || payout.StartedAtUtc < refund.Approval.ApprovedAtUtc
            || (Tender == ManualRefundTender.Cash ? payout.CashSessionId is null || payout.CashSessionId == Guid.Empty
                : payout.CashSessionId is not null))
            throw new ArgumentException("Valid payout custody is required for the original tender.");

        if (refund.Payout is { } existing)
        {
            if (existing with { StartedAtUtc = payout.StartedAtUtc } != payout)
                throw new InvalidOperationException("The original payout is bound to another drawer or shift.");
            return refund;
        }

        refund = refund with { Payout = payout };
        refunds[operationId] = refund;
        return refund;
    }

    public ManualRefund Complete(Guid operationId, ManualRefundPayout custody, bool transferVerified,
        string? transferReference, DateTimeOffset now)
    {
        var refund = Find(operationId);
        if (refund.Payout is not { } payout)
            throw new InvalidOperationException("Begin the approved payout before recording it.");
        if (payout != custody with { StartedAtUtc = payout.StartedAtUtc })
            throw new UnauthorizedAccessException("The original cashier and payout custody are required.");
        var reference = transferReference?.Trim();
        if (now < payout.StartedAtUtc || (Tender == ManualRefundTender.PromptPay
                ? !transferVerified || string.IsNullOrEmpty(reference) || reference.Length > 64 || reference.Any(char.IsControl)
                : transferVerified || reference is not null))
            throw new ArgumentException("Valid refund evidence for the original tender is required.");

        if (refund.Completion is { } completed)
        {
            if (completed.TransferReference != reference)
                throw new InvalidOperationException("The original refund has different completion evidence.");
            return refund;
        }

        if (reference is not null && refunds.Values.Any(other => other.Completion?.TransferReference == reference))
            throw new InvalidOperationException("The transfer reference already records a refund for this order.");

        refund = refund with { Completion = new(reference, now) };
        refunds[operationId] = refund;
        return refund;
    }

    private ManualRefund Find(Guid operationId) => refunds.GetValueOrDefault(operationId)
        ?? throw new InvalidOperationException("The refund approval is unavailable.");

    private static bool ValidSubject(string? subject) =>
        !string.IsNullOrWhiteSpace(subject) && subject.Length <= 200 && !subject.Any(char.IsControl);

    private static bool ValidAmount(decimal amount) =>
        amount is > 0 and < 1_000_000_000_000_000m && decimal.Round(amount, 4) == amount;
}
