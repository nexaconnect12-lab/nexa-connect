namespace NexaConnect.POS;

public sealed record ReceiptPrintContent(string JobName, string Text)
{
    public static ReceiptPrintContent Test => new("NexaConnect test receipt", "TEST PRINT — NOT A SALE\nNexaConnect printer setup\n\nSample item with a long name\n2 × 12.50 = 25.00\n\nSAMPLE TOTAL THB 25.00\n\nCheck text, margins and paper feed.\nNo order or payment was created.\nTEST PRINT — NOT A SALE");
}
public interface IReceiptPrintJob : IDisposable { void Submit(ReceiptPrintContent content); }
public interface IReceiptPrintDriver { IReceiptPrintJob? Prepare(ReceiptPrintOptions options); }
public enum ReceiptPrintResult { Loaded, Submitted, Cancelled, SessionChanged, Failed }

// Coordinates read-only authorization and a single explicit print attempt; never retries a spool job.
public static class ReceiptPrintWorkflow
{
    public static async Task<ReceiptPrintResult> RunAsync(Func<Task<ReceiptPrintContent>> load, ReceiptPrintOptions options,
        IReceiptPrintDriver driver, Func<bool> currentSession, Action<ReceiptPrintContent> preview, bool print)
    {
        try
        {
            options.Validate(); if (!currentSession()) return ReceiptPrintResult.SessionChanged;
            var content = await load(); if (!currentSession()) return ReceiptPrintResult.SessionChanged;
            preview(content); if (!print) return ReceiptPrintResult.Loaded;
            using var job = driver.Prepare(options);
            if (!currentSession()) return ReceiptPrintResult.SessionChanged;
            if (job is null) return ReceiptPrintResult.Cancelled;
            // The dialog may remain open while access is revoked. Reauthorize after it closes.
            var verified = await load(); if (!currentSession()) return ReceiptPrintResult.SessionChanged;
            if (verified != content) return ReceiptPrintResult.Failed;
            job.Submit(verified); return ReceiptPrintResult.Submitted;
        }
        catch { return currentSession() ? ReceiptPrintResult.Failed : ReceiptPrintResult.SessionChanged; }
    }
}
