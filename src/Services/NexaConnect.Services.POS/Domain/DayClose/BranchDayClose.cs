namespace NexaConnect.Services.POS.Domain.DayClose;

public sealed record DayIdentity(Guid OrganizationId, Guid RestaurantId, Guid BranchId, DateOnly BusinessDate);
public sealed record DayTender(string Method, string Currency, decimal Amount);
public sealed record CutoffReference(Guid ManifestId, long Generation, string EvidenceVersion);
public sealed record DayCutoffEvidence(CutoffReference Order, CutoffReference Payment, CutoffReference Pos,
    Guid CheckId, bool SourcesCurrent, int FinancialGaps);
public sealed record DayEvidence(string TimeZone, string Currency, DateTimeOffset FromUtc, DateTimeOffset ToUtc,
    decimal GrossSales, decimal CompletedRefunds, decimal NetSales, decimal CashVariance,
    DayTender[] Tenders, string? OrderVersion, string? PaymentVersion, string? PosVersion,
    int UnresolvedOrders, int UnresolvedPayments, int UnresolvedRefunds, int OpenShifts, int OpenCashSessions,
    int PendingCashReviews, string[] Issues, DateTimeOffset ObservedAtUtc, DateTimeOffset? OrderObservedAtUtc = null,
    DateTimeOffset? PaymentObservedAtUtc = null, DateTimeOffset? PosObservedAtUtc = null,
    Guid? RecordedCheckId = null, DateTimeOffset? RecordedCheckedAtUtc = null, DayCutoffEvidence? Cutoff = null)
{
    public bool IsValid(DateTimeOffset now) => !string.IsNullOrWhiteSpace(TimeZone) && TimeZone.Length<=100
        && Currency is { Length:3 } && Currency.All(c=>c is >= 'A' and <= 'Z')
        && FromUtc!=default && ToUtc>FromUtc && ToUtc-FromUtc<=TimeSpan.FromHours(27) && ToUtc<=now
        && GrossSales>=0 && CompletedRefunds>=0 && NetSales==GrossSales-CompletedRefunds
        && ObservedAtUtc>=ToUtc && ObservedAtUtc<=now
        && new[]{OrderObservedAtUtc,PaymentObservedAtUtc,PosObservedAtUtc}.All(t=>t is null || t>=ToUtc && t<=now)
        && new[]{UnresolvedOrders,UnresolvedPayments,UnresolvedRefunds,OpenShifts,OpenCashSessions,PendingCashReviews}.All(n=>n>=0)
        && Tenders is not null && Tenders.All(t=>!string.IsNullOrWhiteSpace(t.Method) && t.Amount>=0 && t.Currency==Currency)
        && Issues is not null && Issues.Length<=32 && Issues.All(x=>!string.IsNullOrWhiteSpace(x) && x.Length<=100)
        && (Cutoff is null || Cutoff.CheckId != Guid.Empty && Cutoff.FinancialGaps >= 0
            && new[]{Cutoff.Order,Cutoff.Payment,Cutoff.Pos}.All(r=>r is not null && r.ManifestId!=Guid.Empty
                && r.Generation>0 && r.EvidenceVersion.Length==64 && r.EvidenceVersion.All(char.IsAsciiHexDigit)));
    public string[] Blockers() => Issues.Where(x => x != "recorded_check_is_historical"
        && !(x == "cash_variance" && PendingCashReviews == 0))
        .Concat(new (int Count,string Code)[]{(UnresolvedOrders,"unresolved_orders"),(UnresolvedPayments,"unresolved_payments"),(UnresolvedRefunds,"unresolved_refunds"),(OpenShifts,"open_shifts"),(OpenCashSessions,"open_cash_sessions"),(PendingCashReviews,"pending_cash_reviews")}.Where(x=>x.Count>0).Select(x=>x.Code))
        .Concat(new[] { OrderVersion, PaymentVersion, PosVersion }.Any(x => x is null || x.Length != 64 || x.Any(c => !char.IsAsciiHexDigit(c)))
            ? ["source_evidence_unavailable"] : Array.Empty<string>())
        .Concat(Cutoff is { SourcesCurrent:false } ? ["cutoff_superseded"] : Array.Empty<string>())
        .Concat(Cutoff is { FinancialGaps:>0 } ? ["cutoff_financial_gaps"] : Array.Empty<string>()).Distinct().Order().ToArray();
    // Observation timestamps do not participate in evidence equality.
    public bool SameEvidence(DayEvidence other) => TimeZone == other.TimeZone && Currency == other.Currency
        && FromUtc == other.FromUtc && ToUtc == other.ToUtc && GrossSales == other.GrossSales
        && CompletedRefunds == other.CompletedRefunds && NetSales == other.NetSales && CashVariance == other.CashVariance
        && Tenders.SequenceEqual(other.Tenders) && OrderVersion == other.OrderVersion
        && PaymentVersion == other.PaymentVersion && PosVersion == other.PosVersion
        && UnresolvedOrders == other.UnresolvedOrders && UnresolvedPayments == other.UnresolvedPayments
        && UnresolvedRefunds == other.UnresolvedRefunds && OpenShifts == other.OpenShifts
        && OpenCashSessions == other.OpenCashSessions && PendingCashReviews == other.PendingCashReviews
        && Blockers().SequenceEqual(other.Blockers())
        && (Cutoff is null ? other.Cutoff is null : other.Cutoff is not null
            && Cutoff.Order==other.Cutoff.Order && Cutoff.Payment==other.Cutoff.Payment && Cutoff.Pos==other.Cutoff.Pos
            && Cutoff.SourcesCurrent==other.Cutoff.SourcesCurrent && Cutoff.FinancialGaps==other.Cutoff.FinancialGaps);
}
public sealed record PreparationCommand(Guid BranchId, DateOnly BusinessDate, Guid OperationId, long ExpectedVersion, string ReasonCode);
public sealed record PreparationState(DayIdentity Identity, long Version, string Status, DayEvidence? Snapshot,
    string[] Blockers, DateTimeOffset UpdatedAtUtc, Guid? OperationId = null, Guid? ClaimId = null,
    DateTimeOffset? LeaseUntilUtc = null, string? PreparingSubject = null, PreparationCommand? PendingCommand = null);
public sealed class DayCloseConflictException(string code) : Exception(code);

/// <summary>Preparation observes a day; it never approves settlement or fences source writes.</summary>
public sealed class BranchDayClose
{
    private PreparationState state;
    private BranchDayClose(PreparationState state) => this.state = Copy(state);
    public static BranchDayClose New(DayIdentity id, DateTimeOffset now) => new(new(id, 0, "blocked", null, ["not_prepared"], now));
    public static BranchDayClose Restore(PreparationState state) => new(state);
    public PreparationState Export() => Copy(state);
    public void Begin(PreparationCommand command, string subject, Guid claim, DateTimeOffset now, bool resume)
    {
        if (command.BranchId != state.Identity.BranchId || command.BusinessDate != state.Identity.BusinessDate
            || command.OperationId == Guid.Empty || command.ExpectedVersion < 0 || command.ReasonCode is not ("routine_close" or "recheck")
            || string.IsNullOrWhiteSpace(subject) || claim == Guid.Empty) throw new ArgumentException("Invalid preparation command.");
        if (state.Status == "preparing" && state.LeaseUntilUtc > now) throw new DayCloseConflictException("preparation_in_progress");
        if (!resume && state.Version != command.ExpectedVersion) throw new DayCloseConflictException("version_changed");
        if (resume && (state.OperationId != command.OperationId || state.PreparingSubject != subject)) throw new DayCloseConflictException("operation_conflict");
        state = state with { Version = state.Version + 1, Status = "preparing", Blockers = ["preparing"], UpdatedAtUtc = now,
            OperationId = command.OperationId, ClaimId = claim, LeaseUntilUtc = now.AddSeconds(30), PreparingSubject = subject, PendingCommand = command };
    }
    public void Complete(Guid claim, DayEvidence? evidence, DateTimeOffset now)
    {
        if (state.Status != "preparing" || state.ClaimId != claim) throw new DayCloseConflictException("preparation_superseded");
        if(evidence is not null && !evidence.IsValid(now))evidence=null;
        var blockers = evidence?.Blockers() ?? ["source_unavailable"];
        state = state with { Version = state.Version + 1, Status = blockers.Length == 0 ? "ready_for_review" : "blocked",
            Snapshot = evidence is null ? null : evidence with { Tenders = [..evidence.Tenders], Issues = [..evidence.Issues] }, Blockers = blockers, UpdatedAtUtc = now, OperationId = null, ClaimId = null,
            LeaseUntilUtc = null, PreparingSubject = null, PendingCommand = null };
    }
    public bool Validate(DayEvidence? evidence, DateTimeOffset now)
    {
        if (state.Status != "ready_for_review") return false;
        if(evidence is not null && !evidence.IsValid(now))evidence=null;
        if (evidence is not null && state.Snapshot!.SameEvidence(evidence) && evidence.Blockers().Length == 0) return false;
        state = state with { Version = state.Version + 1, Status = "blocked", UpdatedAtUtc = now,
            Blockers = evidence is null ? ["source_unavailable"] : new[] { "source_evidence_changed" }.Concat(evidence.Blockers()).Distinct().Order().ToArray() };
        return true;
    }
    private static PreparationState Copy(PreparationState value) => value with { Blockers = [..value.Blockers],
        Snapshot = value.Snapshot is null ? null : value.Snapshot with { Tenders = [..value.Snapshot.Tenders], Issues = [..value.Snapshot.Issues] } };
}
