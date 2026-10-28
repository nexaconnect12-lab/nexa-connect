using System.IO;
using System.Windows;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly ReceiptPrinterPreferences printerPreferences;
    private readonly IReceiptPrintDriver receiptPrintDriver;
    private readonly Func<Task<string[]>> printerDiscovery;
    private ReceiptPrintOptions receiptPrintOptions = new();
    private ReceiptPrintContent? displayedPrintContent;
    private sealed record PrinterChoice(string? Name, string Label);

    private void InitializePrinting()
    {
        receiptPrintOptions = printerPreferences.Load();
        RenderPrinterSettings([]);
        PrinterSetupStatus.Text = "Refresh printers to check availability. Windows confirms every print job. Preview does not print.";
    }
    private void RenderPrinterSettings(IEnumerable<string> names)
    {
        var choices = new List<PrinterChoice> { new(null, "Ask each time (Windows dialog)") };
        choices.AddRange(names.Select(n => new PrinterChoice(n, n)));
        if (receiptPrintOptions.PrinterName is { } saved && !choices.Any(c => string.Equals(c.Name, saved, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new(saved, saved + " (saved; refresh to check)"));
        ReceiptPrinterPicker.ItemsSource = choices;
        ReceiptPrinterPicker.SelectedItem = choices.First(c => string.Equals(c.Name, receiptPrintOptions.PrinterName, StringComparison.OrdinalIgnoreCase));
        ReceiptPaperPicker.SelectedIndex = receiptPrintOptions.Paper == ReceiptPaper.Roll80 ? 1 : 0;
        ReceiptLayoutText.Text = receiptPrintOptions.Paper == ReceiptPaper.Roll80
            ? "80 mm layout · verify the printer's paper size. PDF output on larger paper contains an 80 mm receipt strip."
            : "Standard page layout (default). Use Terminal & sync → Receipt printer to select 80 mm.";
    }
    private bool PrintSessionCurrent(int generation, string? subject) => generation == receiptSessionGeneration
        && !windowCancellation.IsCancellationRequested && !displaySafetyBlocked && IsSignedIn() && subject == _authentication.CurrentToken?.Subject;

    private async void RefreshPrinters_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked) return;
        int generation = receiptSessionGeneration; string? subject = _authentication.CurrentToken?.Subject;
        SetBusy("Finding Windows printers…");
        try
        {
            var names = await printerDiscovery();
            if (!PrintSessionCurrent(generation, subject)) return;
            RenderPrinterSettings(names);
            PrinterSetupStatus.Text = receiptPrintOptions.PrinterName is { } saved && !names.Contains(saved, StringComparer.OrdinalIgnoreCase)
                ? "The saved printer is unavailable. Reconnect it or choose another printer and save. No printer was substituted."
                : names.Length == 0 ? "No Windows printers found. Install a Windows printer driver, then refresh."
                : "Printers refreshed. Select a printer and layout, then Save printer settings.";
        }
        catch { if (PrintSessionCurrent(generation, subject)) PrinterSetupStatus.Text = "Windows printers could not be read. Check the Windows Print Spooler and installed drivers, then refresh."; }
        finally { UpdateOperationalState(); }
    }
    private void SavePrinterSettings_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked || ReceiptPrinterPicker.SelectedItem is not PrinterChoice choice) return;
        var requested = new ReceiptPrintOptions(choice.Name, ReceiptPaperPicker.SelectedIndex == 1 ? ReceiptPaper.Roll80 : ReceiptPaper.Standard);
        try
        {
            printerPreferences.Save(requested);
        }
        catch
        {
            RenderPrinterSettings(ReceiptPrinterPicker.Items.OfType<PrinterChoice>().Where(c => c.Name is not null).Select(c => c.Name!));
            PrinterSetupStatus.Text = "Printer settings could not be saved. Previous settings remain active. Check local folder access and retry.";
            return;
        }
        receiptPrintOptions = requested;
        ReceiptLayoutText.Text = requested.Paper == ReceiptPaper.Roll80 ? "80 mm layout · verify printer paper size; larger PDF pages contain an 80 mm strip." : "Standard page layout.";
        if (displayedPrintContent is { } content) ShowPrintPreview(content);
        TestReceiptPreview.Document = null;
        PrinterSetupStatus.Text = "Printer settings saved for this terminal and Windows user. Windows still confirms every print.";
    }
    private void ShowPrintPreview(ReceiptPrintContent content)
    {
        ReceiptPreview.Document = ReceiptDocumentFactory.Create(content, receiptPrintOptions.Paper);
        displayedPrintContent = content;
    }
    private void PreviewTestReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked) return;
        TestReceiptPreview.Document = ReceiptDocumentFactory.Create(ReceiptPrintContent.Test, receiptPrintOptions.Paper);
        PrinterSetupStatus.Text = "Test preview only — not a sale. Save your layout before previewing. No print job was sent.";
    }
    private async void PrintTestReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked) return;
        int generation = receiptSessionGeneration; string? subject = _authentication.CurrentToken?.Subject;
        SetBusy("Preparing test print…");
        try
        {
            var result = await ReceiptPrintWorkflow.RunAsync(() => Task.FromResult(ReceiptPrintContent.Test), receiptPrintOptions,
                receiptPrintDriver, () => PrintSessionCurrent(generation, subject),
                content => TestReceiptPreview.Document = ReceiptDocumentFactory.Create(content, receiptPrintOptions.Paper), true);
            if (PrintSessionCurrent(generation, subject)) PrinterSetupStatus.Text = PrintResultMessage(result, true);
        }
        finally { UpdateOperationalState(); }
    }
    private static string PrintResultMessage(ReceiptPrintResult result, bool test = false) => result switch
    {
        ReceiptPrintResult.Loaded => "Paid receipt loaded. Reprints use the same receipt number and amounts.",
        ReceiptPrintResult.Submitted => test ? "Test receipt submitted to Windows. Verify PDF or paper output. No sale or payment was created."
            : "Receipt submitted to Windows printing. Verify PDF or paper output. Payment is unchanged; use this same receipt for any reprint.",
        ReceiptPrintResult.Cancelled => test ? "Test printing cancelled. No sale or payment was created."
            : "Printing cancelled. Payment is unchanged; do not collect again.",
        _ => test ? "Test print unavailable. Check saved printer, connection, paper size and Windows queue before retrying. No sale or payment was created."
            : "Receipt unavailable or printing failed. Check access, saved printer, connection, paper size and Windows queue before retrying. Payment is unchanged; do not collect again."
    };
}
