using System.IO;
using System.Printing;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Extensions.Logging;
using NexaConnect.Observability;

namespace NexaConnect.POS;

public sealed class WindowsReceiptPrintDriver : IReceiptPrintDriver
{
    private static readonly ILoggerFactory LoggerFactory = NexaConnectObservabilityExtensions.CreateClientLoggerFactory("nexaconnect-pos-client");
    private static readonly ILogger Logger = LoggerFactory.CreateLogger("NexaConnect.POS.Printing");
    public static string[] Printers()
    {
        using var server = new LocalPrintServer();
        using var queues = server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections]);
        return queues.Select(q => q.FullName).Where(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 256 && !n.Any(char.IsControl))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public IReceiptPrintJob? Prepare(ReceiptPrintOptions options)
    {
        try { return PrepareCore(options); }
        catch { Logger.LogWarning("Receipt printer preparation failed"); throw; }
    }
    private static IReceiptPrintJob? PrepareCore(ReceiptPrintOptions options)
    {
        options.Validate(); var dialog = new PrintDialog();
        using var server = new LocalPrintServer();
        using var queues = options.PrinterName is null ? null : server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections]);
        PrintQueue? selected = queues?.FirstOrDefault(q => string.Equals(q.FullName, options.PrinterName, StringComparison.OrdinalIgnoreCase));
        if (options.PrinterName is not null && selected is null) throw new InvalidDataException("Saved printer is unavailable. Refresh printers and select it again.");
        if (selected is not null) dialog.PrintQueue = selected;
        if (dialog.ShowDialog() != true) return null;
        // The selected queue belongs to the job until submission; do not dispose it with discovery.
        var jobServer = new PrintServer(dialog.PrintQueue.HostingPrintServer.Name);
        PrintQueue? queue = null;
        try
        {
            queue = new PrintQueue(jobServer, dialog.PrintQueue.Name);
            queue.Refresh();
            if (queue.IsOffline || queue.IsNotAvailable || queue.IsInError) throw new InvalidOperationException("Printer is unavailable.");
            var capabilities = queue.GetPrintCapabilities(dialog.PrintTicket);
            var area = capabilities.PageImageableArea ?? throw new InvalidDataException("Printer does not report printable bounds.");
            var layout = ReceiptPrintLayout.ForPage(options.Paper,
                capabilities.OrientedPageMediaWidth ?? dialog.PrintTicket.PageMediaSize?.Width ?? dialog.PrintableAreaWidth,
                capabilities.OrientedPageMediaHeight ?? dialog.PrintTicket.PageMediaSize?.Height ?? dialog.PrintableAreaHeight,
                area.OriginWidth, area.OriginHeight, area.ExtentWidth, area.ExtentHeight);
            dialog.PrintQueue = queue;
            return new Job(dialog, queue, jobServer, options.Paper, layout);
        }
        catch { queue?.Dispose(); jobServer.Dispose(); throw; }
    }
    private sealed class Job(PrintDialog dialog, PrintQueue queue, PrintServer server, ReceiptPaper paper, ReceiptPrintLayout layout) : IReceiptPrintJob
    {
        public void Submit(ReceiptPrintContent content)
        {
            var document = ReceiptDocumentFactory.Create(content, paper, layout);
            try { dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, content.JobName); Logger.LogInformation("Receipt submitted to Windows spooler"); }
            catch { Logger.LogWarning("Receipt spool submission failed; verify Windows queue before retry"); throw; }
        }
        public void Dispose() { queue.Dispose(); server.Dispose(); }
    }
}
