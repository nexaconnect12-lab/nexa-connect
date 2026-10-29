namespace NexaConnect.POS;

public enum TerminalReadiness { Configured, Detected, Untested, Unavailable }
public enum TerminalService { Identity, Pos, Order, Catalog, Payment, Media }
public sealed record TerminalServiceCheck(TerminalService Service, bool Configured, TerminalReadiness Status, int? HttpStatus, DateTimeOffset? CheckedAtUtc);
public sealed record TerminalSetupFacts(bool TouchFirst, PosDisplayDensity Density, ReceiptPaper Paper, bool HasSavedPrinter,
    int? PrinterCount, bool? SavedPrinterDetected, bool PrinterDiscoveryFailed, int? DisplayCount,
    bool DisplayDiscoveryFailed, bool CustomerDisplayActive, bool DisplaySafetyBlocked, bool ScannerMode,
    DateTimeOffset? PrinterCheckedAtUtc=null,DateTimeOffset? DisplayCheckedAtUtc=null);
public sealed record TerminalSetupRow(string Area, TerminalReadiness Status, string Detail);

public static class TerminalSetup
{
    public static TerminalSetupRow[] Rows(TerminalSetupFacts f, IEnumerable<TerminalServiceCheck> services)
    {
        var rows = new List<TerminalSetupRow>
        {
            new("Input",TerminalReadiness.Configured,f.TouchFirst?"Touch-first enabled; physical input acceptance remains untested.":"Keyboard/mouse default; automatic keyboards off."),
            new("Density",TerminalReadiness.Configured,f.Density==PosDisplayDensity.Touch?"Touch spacing selected.":"Standard spacing selected."),
            new("Displays",f.DisplayDiscoveryFailed||f.DisplaySafetyBlocked||f.DisplayCount==0?TerminalReadiness.Unavailable:f.DisplayCount is >0?TerminalReadiness.Detected:TerminalReadiness.Untested,
                f.DisplaySafetyBlocked?"Safety lock active. Restart on the private staff screen and review roles.":f.DisplayDiscoveryFailed?"Display discovery failed; customer view unavailable.":f.DisplayCount==0?"No Windows displays detected.":"Windows display detection is not physical role/touch acceptance."),
            new("Customer view",f.DisplaySafetyBlocked?TerminalReadiness.Unavailable:f.CustomerDisplayActive?TerminalReadiness.Configured:TerminalReadiness.Untested,
                f.CustomerDisplayActive?"Roles assigned for this running window; physical privacy test still required.":"Single staff screen. Assign distinct extended-desktop roles if needed."),
            new("Receipt layout",TerminalReadiness.Configured,f.Paper==ReceiptPaper.Roll80?"80 mm layout selected; verify driver paper and actual output.":"Standard page layout selected."),
            new("Printer",f.PrinterDiscoveryFailed||f.PrinterCount==0||f.SavedPrinterDetected==false&&f.HasSavedPrinter?TerminalReadiness.Unavailable:
                f.SavedPrinterDetected==true||f.PrinterCount is >0&&!f.HasSavedPrinter?TerminalReadiness.Detected:f.HasSavedPrinter?TerminalReadiness.Configured:TerminalReadiness.Untested,
                f.PrinterDiscoveryFailed?"Windows discovery failed. Check spooler and driver.":f.PrinterCount==0?"No Windows queues detected. Install a driver and refresh.":f.HasSavedPrinter&&f.SavedPrinterDetected==false?"Saved queue missing; choose another explicitly. No fallback was selected.":f.HasSavedPrinter?"Saved queue configured; discovery/output test does not certify physical printing.":"Ask each time. Refresh printers and preview/print a labelled test receipt."),
            new("Scanner mode",TerminalReadiness.Configured,f.ScannerMode?"Explicit scan entry enabled for this running window.":"Scan entry off (startup default)."),
            new("Scanner device",TerminalReadiness.Untested,"Keyboard-emulating device identity cannot be detected here. Confirm model and Enter-only suffix, then test."),
            new("Output acceptance",TerminalReadiness.Untested,"Saved PDF, physical printing, scan-to-cart and dual-display acceptance require explicit operator checks.")
        };
        foreach(var check in services.OrderBy(s=>s.Service)) rows.Add(new($"Service: {check.Service}",check.Status,
            !check.Configured?"Optional endpoint not configured.":check.CheckedAtUtc is null?"Not checked. This check will test reachability only.":check.HttpStatus is { } status?
            $"HTTP {status}. Health, identity and business access remain unverified; redirects are refused.":"No HTTP response. Check connectivity, certificate and endpoint configuration."));
        return rows.ToArray();
    }
}
