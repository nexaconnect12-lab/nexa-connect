using NexaConnect.POS;
using System.Net;
using System.Net.Http.Json;

namespace NexaConnect.UnitTests;

public sealed class ReceiptPrintingTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Refund_lookup_checks_the_requested_refund_identity(bool substituted)
    {
        var c = new PosClientConfiguration("https://identity.test/", "nexaconnect-pos", "nexaconnect-pos://oauth/callback", "openid", "https://pos.test/", "https://order.test/", Guid.NewGuid(), Guid.NewGuid(), "THB", "cash_manual", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CatalogApi: "https://catalog.test/");
        Guid requested = Guid.NewGuid(), intent = Guid.NewGuid();
        var refund = new PosPaymentRefund(substituted ? Guid.NewGuid() : requested, c.OrganizationId, c.RestaurantId, c.BranchId, Guid.NewGuid(), intent, Guid.NewGuid(), 1, "THB", "customer_request", "failed", DateTimeOffset.UtcNow, null, null, null);
        using var api = new PosApiClient(c, paymentHandler: new RefundResponse(refund));
        var token = new PosTokenSet("synthetic", null, DateTimeOffset.UtcNow.AddMinutes(5), "Bearer");
        if (substituted) await Assert.ThrowsAsync<InvalidDataException>(() => api.GetRefundAsync(token, intent, requested));
        else Assert.Equal(requested, (await api.GetRefundAsync(token, intent, requested)).Id);
    }
    private sealed class RefundResponse(PosPaymentRefund refund) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Assert.Equal(HttpMethod.Get, request.Method); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(refund) }); }
    }
    [Fact]
    public void Preferences_default_to_dialog_and_standard_and_do_not_change_input_preferences()
    {
        string root = Path.Combine(Path.GetTempPath(), "Nexa-PrintPreferences", Guid.NewGuid().ToString("N"));
        try
        {
            var input = new PosUiPreferences(root); input.Save(new(true, PosDisplayDensity.Touch));
            var printing = new ReceiptPrinterPreferences(root); Assert.Equal(new(), printing.Load());
            printing.Save(new("Microsoft Print to PDF", ReceiptPaper.Roll80));
            Assert.Equal(new("Microsoft Print to PDF", ReceiptPaper.Roll80), new ReceiptPrinterPreferences(root).Load());
            Assert.Equal(new(true, PosDisplayDensity.Touch), input.Load());
            File.WriteAllText(Path.Combine(root, "printing.json"), new string('x', 4097)); Assert.Equal(new(), printing.Load());
            File.WriteAllText(Path.Combine(root, "printing.json"), "{\"Version\":1,\"Paper\":\"unknown\"}"); Assert.Equal(new(), printing.Load());
            printing.Save(new("PDF", ReceiptPaper.Roll80));
            using (File.Open(Path.Combine(root, "printing.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            { var failure = Record.Exception(() => printing.Save(new("Other", ReceiptPaper.Standard))); Assert.True(failure is IOException or UnauthorizedAccessException); }
            Assert.Equal(new("PDF", ReceiptPaper.Roll80), printing.Load());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("bad\nprinter")][InlineData("")][InlineData(" ")]
    public void Invalid_printer_identity_is_rejected(string name) => Assert.Throws<InvalidDataException>(() => new ReceiptPrintOptions(name).Validate());
    [Fact]
    public void Printer_preferences_are_isolated_by_full_terminal_scope_and_windows_profile()
    {
        string root = Path.Combine(Path.GetTempPath(), "Nexa-PrintScope", Guid.NewGuid().ToString("N"));
        try
        {
            var c = new PosClientConfiguration("https://identity.test/", "nexaconnect-pos", "nexaconnect-pos://oauth/callback", "openid", "https://pos.test/", "https://order.test/", Guid.NewGuid(), Guid.NewGuid(), "THB", "cash_manual", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            ReceiptPrinterPreferences.ForTerminal(c, root).Save(new("PDF", ReceiptPaper.Roll80));
            foreach (var other in new[] { c with { OrganizationId = Guid.NewGuid() }, c with { RestaurantId = Guid.NewGuid() }, c with { BranchId = Guid.NewGuid() }, c with { StoreId = Guid.NewGuid() }, c with { TerminalId = Guid.NewGuid() } })
                Assert.Equal(new(), ReceiptPrinterPreferences.ForTerminal(other, root).Load());
            Assert.Equal(new(), ReceiptPrinterPreferences.ForTerminal(c, Path.Combine(root, "other-user")).Load());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void Roll_geometry_respects_printable_margins_without_scaling_to_a_wider_pdf_page()
    {
        double mm = ReceiptPrintLayout.Millimetre;
        var preview = ReceiptPrintLayout.Preview(ReceiptPaper.Roll80);
        Assert.Equal(80 * mm, preview.Width); Assert.Equal(72 * mm, preview.ContentWidth, 6);
        var pdf = ReceiptPrintLayout.ForPage(ReceiptPaper.Roll80, 210 * mm, 297 * mm, 3 * mm, 3 * mm, 204 * mm, 291 * mm);
        Assert.Equal(preview.ContentWidth, pdf.ContentWidth, 6); Assert.Equal(preview.Left, pdf.Left, 6);
        Assert.Throws<InvalidDataException>(() => ReceiptPrintLayout.ForPage(ReceiptPaper.Roll80, 58 * mm, 200 * mm, 0, 0, 58 * mm, 200 * mm));
        Assert.Throws<InvalidDataException>(() => ReceiptPrintLayout.ForPage(ReceiptPaper.Roll80, 80 * mm, 200 * mm, 13 * mm, 0, 67 * mm, 200 * mm));
        Assert.Throws<InvalidDataException>(() => ReceiptPrintLayout.ForPage(ReceiptPaper.Standard, double.NaN, 500, 0, 0, 500, 500));
    }
    [Fact]
    public async Task Print_reauthorizes_after_dialog_and_submits_exact_original_content_once()
    {
        var driver = new Driver(); int reads = 0; var receipt = new ReceiptPrintContent("Sales receipt", "R-ORIGINAL\nTOTAL THB 123.45");
        var result = await ReceiptPrintWorkflow.RunAsync(() => { reads++; return Task.FromResult(receipt); }, new(), driver, () => true, _ => { }, true);
        Assert.Equal(ReceiptPrintResult.Submitted, result); Assert.Equal(2, reads); Assert.Equal(receipt, driver.Printed); Assert.Equal(1, driver.Submits); Assert.True(driver.Disposed);
    }
    [Theory]
    [InlineData("denied-before")][InlineData("denied-after-dialog")][InlineData("changed-receipt")][InlineData("missing-printer")][InlineData("cancel")][InlineData("locked-during-read")][InlineData("locked-in-dialog")][InlineData("locked-during-final-read")]
    public async Task Denial_changed_receipt_missing_printer_cancel_and_session_races_never_spool(string scenario)
    {
        bool current = true; int reads = 0; var receipt = new ReceiptPrintContent("Sales receipt", "immutable");
        var driver = new Driver { Cancel = scenario == "cancel", Missing = scenario == "missing-printer" };
        driver.OnPrepare = () => { if (scenario == "locked-in-dialog") current = false; };
        var result = await ReceiptPrintWorkflow.RunAsync(() => {
            reads++;
            if (scenario == "denied-before" || scenario == "denied-after-dialog" && reads == 2) throw new UnauthorizedAccessException();
            if (scenario == "locked-during-read" || scenario == "locked-during-final-read" && reads == 2) current = false;
            return Task.FromResult(scenario == "changed-receipt" && reads == 2 ? receipt with { Text = "changed" } : receipt);
        }, new(), driver, () => current, _ => { }, true);
        Assert.NotEqual(ReceiptPrintResult.Submitted, result); Assert.Equal(0, driver.Submits);
        if (scenario == "cancel") Assert.Equal(ReceiptPrintResult.Cancelled, result);
        if (scenario.StartsWith("locked")) Assert.Equal(ReceiptPrintResult.SessionChanged, result);
    }
    [Fact]
    public async Task Spool_failure_is_not_retried_and_preview_does_not_discover_or_print()
    {
        var driver = new Driver { FailSubmit = true };
        var result = await ReceiptPrintWorkflow.RunAsync(() => Task.FromResult(ReceiptPrintContent.Test), new(), driver, () => true, _ => { }, true);
        Assert.Equal(ReceiptPrintResult.Failed, result); Assert.Equal(1, driver.Submits);
        driver = new();
        result = await ReceiptPrintWorkflow.RunAsync(() => Task.FromResult(ReceiptPrintContent.Test), new(), driver, () => true, _ => { }, false);
        Assert.Equal(ReceiptPrintResult.Loaded, result); Assert.Equal(0, driver.Prepares); Assert.Contains("NOT A SALE", ReceiptPrintContent.Test.Text);
    }
    private sealed class Driver : IReceiptPrintDriver, IReceiptPrintJob
    {
        public bool Cancel, Missing, FailSubmit, Disposed; public int Prepares, Submits; public ReceiptPrintContent? Printed; public Action? OnPrepare;
        public IReceiptPrintJob? Prepare(ReceiptPrintOptions options) { Prepares++; OnPrepare?.Invoke(); if (Missing) throw new IOException(); return Cancel ? null : this; }
        public void Submit(ReceiptPrintContent content) { Submits++; if (FailSubmit) throw new IOException(); Printed = content; }
        public void Dispose() => Disposed = true;
    }
}
