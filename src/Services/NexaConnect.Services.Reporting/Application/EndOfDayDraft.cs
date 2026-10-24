using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.Reporting;

namespace NexaConnect.Services.Reporting.Application;

public sealed record BranchBusinessCalendar(Guid OrganizationId, Guid RestaurantId, Guid BranchId, string TimeZone, string Currency);
public sealed record EndOfDayDraftReport(DateOnly BusinessDate, BranchBusinessCalendar Branch, EndOfDayWindow Window,
    string Status, decimal GrossSales, decimal CompletedRefunds, decimal NetSales, IReadOnlyList<TenderTotal> Tenders,
    decimal CashVariance, OrderDaySummary Order, PaymentDaySummary Payment, PosDaySummary Pos,
    DashboardSummary Projection, FinancialCompletenessObservation? RecordedCompleteness, IReadOnlyList<string> Issues);
public interface IEndOfDaySources
{
    Task<BranchBusinessCalendar?> CalendarAsync(Guid branch, CancellationToken ct);
    Task<OrderDaySummary> OrderAsync(EndOfDayWindow window, string bearer, CancellationToken ct);
    Task<PaymentDaySummary> PaymentAsync(EndOfDayWindow window, string bearer, CancellationToken ct);
    Task<PosDaySummary> PosAsync(EndOfDayWindow window, string bearer, CancellationToken ct);
}

public sealed class EndOfDayDraft(IEndOfDaySources sources, IReportingCustomerAuthorizer authorization,
    IReportingReadRepository projections, IFinancialCompletenessRepository completeness, TimeProvider clock)
{
    public static EndOfDayWindow Window(Guid organization, BranchBusinessCalendar branch, DateOnly date, DateTimeOffset now)
    {
        if (organization == Guid.Empty || branch.OrganizationId != organization || branch.RestaurantId == Guid.Empty || branch.BranchId == Guid.Empty)
            throw new UnauthorizedAccessException();
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(branch.TimeZone); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new InvalidOperationException("Branch timezone requires correction."); }
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Windows timezone history can omit the local gap at a historical date-line change.
        // Reject these extreme historical offsets rather than producing a plausible wrong day.
        if (zone.GetAdjustmentRules().Any(r => r.DateStart <= end && r.DateEnd >= start
                && Math.Abs(r.BaseUtcOffsetDelta.TotalHours) >= 12))
            throw new ArgumentException("Historical date-line timezone offsets are unsupported.");
        // Reject unusual midnight transitions rather than silently choosing an accounting boundary.
        if (zone.IsInvalidTime(start) || zone.IsAmbiguousTime(start) || zone.IsInvalidTime(end) || zone.IsAmbiguousTime(end))
            throw new ArgumentException("This business date has an unsupported midnight timezone transition.");
        var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, zone));
        var to = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end, zone));
        if (TimeZoneInfo.ConvertTime(from, zone).DateTime != start || TimeZoneInfo.ConvertTime(to, zone).DateTime != end)
            throw new ArgumentException("This business date cannot round-trip its timezone boundaries.");
        if (to > now || to <= from || to - from > TimeSpan.FromHours(27)) throw new ArgumentException("Select a completed branch business date.");
        return new(organization, branch.RestaurantId, branch.BranchId, from, to);
    }

    public async Task<EndOfDayDraftReport> ReadAsync(Guid organization, Guid branchId, DateOnly date, string bearer, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        ct = deadline.Token;
        if (branchId == Guid.Empty || date == default || date == DateOnly.MaxValue) throw new ArgumentException("Branch and business date are required.");
        if (!await authorization.IsGrantedAsync(organization, branchId, ProductPermissions.ReportingSalesRead, bearer, ct))
            throw new UnauthorizedAccessException();
        var branch = await sources.CalendarAsync(branchId, ct) ?? throw new UnauthorizedAccessException();
        if (branch.BranchId != branchId) throw new UnauthorizedAccessException();
        var window = Window(organization, branch, date, clock.GetUtcNow());
        var orderTask = sources.OrderAsync(window, bearer, ct);
        var paymentTask = sources.PaymentAsync(window, bearer, ct);
        var posTask = sources.PosAsync(window, bearer, ct);
        var range = new ReportingRange(organization, branchId, window.FromUtc, window.ToUtc);
        var projectionTask = projections.DashboardAsync(range, ct);
        var checkTask = completeness.LatestAsync(range, ct);
        await Task.WhenAll(orderTask, paymentTask, posTask, projectionTask, checkTask);
        var order = await orderTask; var payment = await paymentTask; var pos = await posTask;
        var projection = await projectionTask; var check = await checkTask;
        if (order.Window != window || payment.Window != window || pos.Window != window || check is not null && check.Range != range)
            throw new InvalidOperationException("Source returned inconsistent scope.");
        var now = clock.GetUtcNow();
        if (branch.Currency.Length != 3 || branch.Currency.Any(c => c is < 'A' or > 'Z')
            || order.GrossSales < 0 || payment.CompletedRefunds < 0 || order.Tenders.Any(t => t.Amount < 0 || string.IsNullOrWhiteSpace(t.Method))
            || new[] { order.CompletedOrders, order.UnresolvedOrders, order.EvidenceGaps, payment.UnresolvedPayments,
                payment.UnresolvedRefunds, payment.EvidenceGaps, pos.OpenShifts, pos.OpenCashSessions, pos.PendingCashReviews }.Any(n => n < 0)
            || new[] { order.ObservedAtUtc, payment.ObservedAtUtc, pos.ObservedAtUtc }.Any(t => t < window.ToUtc || t > now))
            throw new InvalidOperationException("Source returned invalid observation.");
        var currencies = order.Currencies.Concat(order.Tenders.Select(t => t.Currency)).Concat(payment.Currencies).Concat(pos.Currencies)
            .Concat(projection.Currency is null ? [] : new[] { projection.Currency }).Distinct(StringComparer.Ordinal).ToArray();
        if (currencies.Any(c => c != branch.Currency)) throw new MixedReportingCurrencyException();
        var issues = new List<string>();
        if (order.UnresolvedOrders > 0) issues.Add("unresolved_orders");
        if (payment.UnresolvedPayments > 0) issues.Add("unresolved_payments");
        if (payment.UnresolvedRefunds > 0) issues.Add("unresolved_refunds");
        if (pos.OpenShifts > 0) issues.Add("open_shifts");
        if (pos.OpenCashSessions > 0) issues.Add("open_cash_sessions");
        if (pos.PendingCashReviews > 0) issues.Add("pending_cash_reviews");
        if (pos.CashVariance != 0) issues.Add("cash_variance");
        if (order.EvidenceGaps + payment.EvidenceGaps > 0) issues.Add("missing_source_evidence");
        if (projection.GrossSales != order.GrossSales || projection.Refunded != payment.CompletedRefunds || projection.CompletedOrders != order.CompletedOrders)
            issues.Add("projection_totals_differ");
        if (check is null) issues.Add("financial_evidence_not_checked");
        else
        {
            // Historical checks cannot establish completeness of the newer source snapshots.
            issues.Add("recorded_check_is_historical");
            if (check.Status != "observed_complete") issues.Add("recorded_financial_gaps");
        }
        return new(date, branch, window, "draft", order.GrossSales, payment.CompletedRefunds,
            order.GrossSales - payment.CompletedRefunds, order.Tenders, pos.CashVariance, order, payment, pos, projection, check, issues);
    }
}
