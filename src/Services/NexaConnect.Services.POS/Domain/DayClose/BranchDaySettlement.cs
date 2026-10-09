namespace NexaConnect.Services.POS.Domain.DayClose;

public sealed record SettlementCommand(Guid BranchId, DateOnly BusinessDate, Guid OperationId,
    long ExpectedPreparationVersion, Guid PreparationOperationId, Guid ApprovalId, long ReviewedApprovalVersion);

public sealed record SettlementReceipt(Guid DecisionId, Guid EventId, Guid CorrelationId, DateTimeOffset SettledAtUtc,
    Guid SettlementId, DayIdentity Identity, Guid ApprovalId, long SealVersion, DayEvidence Snapshot, FinalizationFence[] Sources);
public sealed record SettlementAcknowledgement(string Source,Guid SealId,string Phase,Guid? DecisionId);

public static class BranchDaySettlement
{
    public const string FinalizePermission = "pos.day-close.finalize";

    public static void ValidateAcknowledgements(FinalizationState preparation,SettlementAcknowledgement[] sources,string phase,Guid? decision)
    {
        if(phase is not("armed" or "committed" or "aborted")||(phase=="armed")!=(decision is null)||decision==Guid.Empty
            ||sources.Length!=3||sources.Select(s=>s.Source).Distinct().Count()!=3)
            throw new DayCloseConflictException("source_barriers_unproven");
        foreach(var owner in new[]{"Order","Payment","POS"})
        {
            var proof=sources.SingleOrDefault(s=>s.Source==owner);
            var expected=preparation.Sources.SingleOrDefault(s=>s.Source==owner);
            if(proof is null||expected is null||proof.SealId!=expected.SealId||proof.Phase!=phase||proof.DecisionId!=decision)
                throw new DayCloseConflictException("source_barriers_unproven");
        }
    }

    public static void ValidateAdmission(SettlementCommand command, FinalizationState preparation,
        DayApprovalDecision? approval, long approvalVersion, string approvalStatus, DateTimeOffset now)
    {
        if (command.OperationId == Guid.Empty || command.PreparationOperationId == Guid.Empty || command.ApprovalId == Guid.Empty
            || command.ExpectedPreparationVersion <= 0 || command.ReviewedApprovalVersion <= 0)
            throw new ArgumentException();
        if (command.BranchId != preparation.Identity.BranchId || command.BusinessDate != preparation.Identity.BusinessDate
            || command.ExpectedPreparationVersion != preparation.Version || command.PreparationOperationId != preparation.Command.OperationId
            || command.ApprovalId != preparation.Approval.ApprovalId || command.ReviewedApprovalVersion != preparation.Command.ReviewedApprovalVersion
            || preparation.Status != "prepared" || approval is null || !preparation.Approval.Snapshot.SameEvidence(approval.Snapshot)
            || !FinalizationPreparation.HasLiveProof(preparation, approval, approvalVersion, approvalStatus, preparation.Sources, now))
            throw new DayCloseConflictException("reviewed_preparation_changed");
    }

    public static SettlementReceipt Commit(Guid settlementId, Guid decisionId, Guid correlationId, FinalizationState preparation, DateTimeOffset now)
    {
        if (settlementId == Guid.Empty || decisionId == Guid.Empty || correlationId == Guid.Empty || preparation.Sources.Length != 3)
            throw new ArgumentException();
        var day = preparation.Identity;
        var snapshot = preparation.Approval.Snapshot;
        return new(decisionId, Guid.NewGuid(), correlationId, now, settlementId, day, preparation.Approval.ApprovalId,
            preparation.Approval.SealVersion, snapshot with { Tenders = [..snapshot.Tenders], Issues = [..snapshot.Issues],
                SealComparison=snapshot.SealComparison is null?null:snapshot.SealComparison with{Tenders=[..snapshot.SealComparison.Tenders],Changes=snapshot.SealComparison.Changes?.ToArray()} }, [..preparation.Sources]);
    }
}
