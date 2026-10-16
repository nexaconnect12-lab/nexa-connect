using System.Globalization;
using System.Text;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.UnitTests;

public sealed class SettlementCsvTests
{
    private static readonly Guid Org = Guid.NewGuid(), Branch = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 9, 1), To = new(2026, 9, 3);
    private static readonly SettlementReportRange Range = new(Org, Branch, From, To);
    private static SettlementReport Report()
    {
        var epoch = Guid.NewGuid(); var time = new DateTimeOffset(2026, 9, 2, 1, 0, 0, TimeSpan.Zero);
        var correction = new SettlementCorrectionsV2(Guid.NewGuid(), epoch, 2, new('A', 64), 1, 1, 0, 0, 0, -10.1234m,
            [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), From, time.AddHours(-2), -10.1234m)]);
        SettlementReportItem Row(int version, string status) => new(Guid.NewGuid(), Guid.NewGuid(), version == 1 ? From : From.AddDays(1),
            time.AddDays(1), Guid.NewGuid(), 1, "THB", "999999999999999.9999", "1", "999999999999998.9999", "-2.1234", version,
            version == 1 ? "historical_unavailable" : "verified", version == 1 ? null : correction, status,
            [new("Order", Guid.NewGuid(), Guid.NewGuid(), 1), new("Payment", Guid.NewGuid(), Guid.NewGuid(), 1), new("POS", Guid.NewGuid(), epoch, 2)],
            version == 1 ? null : Guid.NewGuid(), version == 1 ? null : time.AddHours(-1), version == 1 ? null : time.AddHours(23));
        return new(Org, Branch, From, To, time.AddDays(2), time.AddDays(2).AddMinutes(1), new('B', 64), "gaps", 2, 1, 1, 0, 0,
            [Row(1, "matched"), Row(2, "missing")]);
    }
    private static Dictionary<string, string>[] Rows(byte[] bytes)
    {
        var lines = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        static string[] Cells(string line) => line[1..^1].Split("\",\"");
        var header = Cells(lines[0]);
        return lines.Skip(1).Select(line => header.Zip(Cells(line)).ToDictionary(p => p.First, p => p.Second)).ToArray();
    }
    [Theory]
    [InlineData("en-US")][InlineData("fr-FR")][InlineData("th-TH")]
    public void Export_preserves_exact_money_legacy_gaps_and_separate_corrections(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var report = Report(); var bytes = SettlementCsv.Render(report, Range); var rows = Rows(bytes);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.True(bytes.Length < SettlementCsv.MaximumBytes);
            var settlements = rows.Where(r => r["record_type"] == "settlement").ToArray();
            Assert.Equal(2, settlements.Length);
            Assert.All(settlements, r => { Assert.Equal("999999999999999.9999", r["gross_sales"]);
                Assert.Equal("999999999999998.9999", r["net_sales"]); Assert.Equal("-2.1234", r["cash_variance"]); });
            Assert.Equal("historical_unavailable", settlements[0]["correction_evidence"]);
            Assert.Equal("", settlements[0]["correction_adjustment_included_in_variance"]);
            Assert.Equal("missing", settlements[1]["delivery_status"]);
            Assert.Equal("-10.1234", settlements[1]["correction_adjustment_included_in_variance"]);
            var detail = Assert.Single(rows, r => r["record_type"] == "correction");
            Assert.Equal("-10.1234", detail["correction_item_adjustment"]);
            Assert.Equal("", detail["gross_sales"]); Assert.Equal("", detail["cash_variance"]);
            Assert.Equal(6, rows.Count(r => r["record_type"] == "source_seal"));
            Assert.All(rows, r => { Assert.Equal(report.ManifestHash, r["manifest_hash"]); Assert.Equal("gaps", r["report_status"]); });
            Assert.Equal($"day-settlements-{Branch:D}-2026-09-01-2026-09-03.csv", SettlementCsv.FileName(Branch, From, To));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void Empty_export_keeps_scope_and_observation_and_invalid_scope_or_counts_fail_closed()
    {
        var empty = Report() with { Items = [], Status = "matched", Expected = 0, Matched = 0, Missing = 0 };
        var row = Assert.Single(Rows(SettlementCsv.Render(empty, Range)));
        Assert.Equal("observation", row["record_type"]); Assert.Equal("0", row["expected"]);
        Assert.Throws<InvalidOperationException>(() => SettlementCsv.Render(empty with { OrganizationId = Guid.NewGuid() }, Range));
        Assert.Throws<InvalidOperationException>(() => SettlementCsv.Render(Report() with { Missing = 0 }, Range));
    }
    [Theory]
    [InlineData("=SUM(1,2)", "'=" )][InlineData("  +cmd", "'  +")][InlineData("-1+2", "'-")]
    [InlineData("@cmd", "'@")][InlineData("\tcmd", "'\t")][InlineData("\r\n=cmd", "'\r\n=")][InlineData("-1\n", "'-1\n")]
    public void Csv_text_neutralizes_formulas_and_quotes_embedded_quotes(string input, string prefix)
    {
        Assert.StartsWith("\"" + prefix, SettlementCsv.Cell(input));
        Assert.Equal("\"a,\"\"b\"\"\"", SettlementCsv.Cell("a,\"b\""));
        Assert.Equal("\"-10.1234\"", SettlementCsv.Cell("-10.1234"));
    }
}
