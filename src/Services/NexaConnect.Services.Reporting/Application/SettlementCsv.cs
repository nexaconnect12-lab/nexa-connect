using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NexaConnect.Contracts.Reporting;

namespace NexaConnect.Services.Reporting.Application;

// An export of one fresh ledger observation, never a new financial calculation.
public static class SettlementCsv
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly string[] Columns = "record_type,organization_id,branch_id,from_date,to_date_exclusive,source_observed_at_utc,compared_at_utc,manifest_hash,report_status,expected,matched,missing,conflicting,unexpected,event_id,settlement_id,business_date,settled_at_utc,approval_id,seal_version,currency,gross_sales,completed_refunds,net_sales,cash_variance,contract_version,delivery_status,correction_evidence,decision_id,window_from_utc,window_to_utc,source,source_seal_id,source_epoch,source_revision,correction_manifest_id,correction_epoch,correction_revision,correction_manifest_hash,correction_expected,correction_matched,correction_missing,correction_conflicting,correction_unexpected,correction_adjustment_included_in_variance,correction_event_id,correction_id,original_settlement_id,work_id,order_id,tender_id,drawer_id,posting_date,posted_at_utc,correction_item_adjustment".Split(',');

    public static string FileName(Guid branchId, DateOnly from, DateOnly to) =>
        FormattableString.Invariant($"day-settlements-{branchId:D}-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");

    // Quoting alone does not prevent spreadsheet formula execution. Keep signed numeric cells numeric.
    public static string Cell(string value)
    {
        var first = value.AsSpan().TrimStart();
        bool number = Regex.IsMatch(value, @"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]{1,4})?\z", RegexOptions.CultureInvariant);
        if (!number && (value.Any(c => c is '\t' or '\r' or '\n')
            || (!first.IsEmpty && first[0] is '=' or '+' or '-' or '@')))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static byte[] Render(SettlementReport report, SettlementReportRange range)
    {
        SettlementReporting.ValidateRange(range);
        if (report.OrganizationId != range.OrganizationId || report.BranchId != range.BranchId
            || report.FromDate != range.FromDate || report.ToDate != range.ToDate || report.Items is null
            || report.Items.Count > 62 || report.Items.Sum(i => i.Corrections?.Items.Count ?? 0) > 2000
            || report.Items.Count != report.Expected + report.Unexpected
            || report.Expected > 31 || report.Expected != report.Matched + report.Missing + report.Conflicting
            || new[] { report.Expected, report.Matched, report.Missing, report.Conflicting, report.Unexpected }.Any(n => n < 0)
            || report.Items.Count(i => i.Status == "matched") != report.Matched
            || report.Items.Count(i => i.Status == "missing") != report.Missing
            || report.Items.Count(i => i.Status == "conflicting") != report.Conflicting
            || report.Items.Count(i => i.Status == "unexpected") != report.Unexpected
            || report.Status != (report.Missing + report.Conflicting + report.Unexpected == 0 ? "matched" : "gaps"))
            throw new InvalidOperationException("Invalid settlement export observation.");

        var text = new StringBuilder();
        text.AppendJoin(',', Columns.Select(Cell)).Append("\r\n");
        var metadata = new Dictionary<string, string>
        {
            ["organization_id"] = report.OrganizationId.ToString("D"), ["branch_id"] = report.BranchId.ToString("D"),
            ["from_date"] = Date(range.FromDate), ["to_date_exclusive"] = Date(range.ToDate),
            ["source_observed_at_utc"] = Time(report.SourceObservedAtUtc), ["compared_at_utc"] = Time(report.ComparedAtUtc),
            ["manifest_hash"] = report.ManifestHash, ["report_status"] = report.Status,
            ["expected"] = Number(report.Expected), ["matched"] = Number(report.Matched), ["missing"] = Number(report.Missing),
            ["conflicting"] = Number(report.Conflicting), ["unexpected"] = Number(report.Unexpected)
        };
        void Write(string kind, Dictionary<string, string>? values = null)
        {
            var row = new Dictionary<string, string>(metadata) { ["record_type"] = kind };
            if (values is not null) foreach (var field in values) row[field.Key] = field.Value;
            text.AppendJoin(',', Columns.Select(c => Cell(row.GetValueOrDefault(c, "")))).Append("\r\n");
        }
        Write("observation"); // Empty ranges still carry provenance and zero counts.
        foreach (var item in report.Items.OrderBy(i => i.BusinessDate).ThenBy(i => i.EventId))
        {
            var identity = new Dictionary<string, string>
            {
                ["event_id"] = item.EventId.ToString("D"), ["settlement_id"] = item.SettlementId.ToString("D"),
                ["business_date"] = Date(item.BusinessDate), ["delivery_status"] = item.Status,
                ["currency"] = item.Currency, ["contract_version"] = Number(item.ContractVersion),
                ["correction_evidence"] = item.CorrectionEvidence
            };
            var settlement = new Dictionary<string, string>(identity)
            {
                ["settled_at_utc"] = Time(item.SettledAtUtc), ["approval_id"] = item.ApprovalId.ToString("D"),
                ["seal_version"] = Number(item.SealVersion), ["gross_sales"] = item.GrossSales,
                ["completed_refunds"] = item.CompletedRefunds, ["net_sales"] = item.NetSales, ["cash_variance"] = item.CashVariance,
                ["decision_id"] = item.DecisionId?.ToString("D") ?? "", ["window_from_utc"] = OptionalTime(item.FromUtc),
                ["window_to_utc"] = OptionalTime(item.ToUtc)
            };
            if (item.Corrections is { } proof)
            {
                settlement["correction_manifest_id"] = proof.ManifestId.ToString("D");
                settlement["correction_epoch"] = proof.RevisionEpoch.ToString("D");
                settlement["correction_revision"] = Number(proof.SourceRevision);
                settlement["correction_manifest_hash"] = proof.ManifestHash;
                settlement["correction_expected"] = Number(proof.Expected); settlement["correction_matched"] = Number(proof.Matched);
                settlement["correction_missing"] = Number(proof.Missing); settlement["correction_conflicting"] = Number(proof.Conflicting);
                settlement["correction_unexpected"] = Number(proof.Unexpected);
                settlement["correction_adjustment_included_in_variance"] = Money(proof.Adjustment);
            }
            Write("settlement", settlement);
            foreach (var source in item.Sources.OrderBy(s => s.Source, StringComparer.Ordinal))
                Write("source_seal", new(identity) { ["source"] = source.Source, ["source_seal_id"] = source.SealId.ToString("D"),
                    ["source_epoch"] = source.Epoch.ToString("D"), ["source_revision"] = Number(source.Revision) });
            foreach (var correction in (item.Corrections?.Items ?? []).OrderBy(c => c.EventId))
                Write("correction", new(identity)
                {
                    ["correction_event_id"] = correction.EventId.ToString("D"), ["correction_id"] = correction.CorrectionId.ToString("D"),
                    ["original_settlement_id"] = correction.OriginalSettlementId.ToString("D"), ["work_id"] = correction.WorkId.ToString("D"),
                    ["order_id"] = correction.OrderId.ToString("D"), ["tender_id"] = correction.TenderId.ToString("D"),
                    ["drawer_id"] = correction.DrawerId.ToString("D"), ["posting_date"] = Date(correction.PostingDate),
                    ["posted_at_utc"] = Time(correction.PostedAtUtc), ["correction_item_adjustment"] = Money(correction.Adjustment)
                });
        }
        var encoding = new UTF8Encoding(true, true);
        if (encoding.GetByteCount(text.ToString()) + encoding.GetPreamble().Length > MaximumBytes)
            throw new InvalidOperationException("Settlement export exceeds its byte limit.");
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(text.ToString())];
    }
    private static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);
    private static string Money(decimal n) => n.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Time(DateTimeOffset t) => t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string OptionalTime(DateTimeOffset? t) => t is null ? "" : Time(t.Value);
}
