using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly PosTerminalDiagnostics terminalDiagnostics;
    private TerminalServiceCheck[] terminalServiceChecks=[];
    private string[]? discoveredPrinterNames;
    private bool printerDiscoveryFailed,displayDiscoveryFailed;
    private int? detectedDisplayCount;
    private DateTimeOffset? printerCheckedAtUtc,displayCheckedAtUtc;
    private Task<string[]>? printerDiscoveryTask;
    private CancellationTokenSource? setupCancellation;

    private async Task<string[]> DiscoverPrintersBoundedAsync(CancellationToken ct)
    {
        if(printerDiscoveryTask is null||printerDiscoveryTask.IsCompleted)printerDiscoveryTask=printerDiscovery();
        return await printerDiscoveryTask.WaitAsync(TimeSpan.FromSeconds(6),ct);
    }
    private void RecordPrinterDiscovery(string[]? names)
    {
        discoveredPrinterNames=names;printerDiscoveryFailed=names is null;printerCheckedAtUtc=DateTimeOffset.UtcNow;
    }
    private void RecordDisplayDiscovery(int? count)
    {
        detectedDisplayCount=count is null?null:Math.Min(count.Value,32);displayDiscoveryFailed=count is null;displayCheckedAtUtc=DateTimeOffset.UtcNow;
    }
    private TerminalSetupFacts SetupFacts()=>new(touchFirst,displayDensity,receiptPrintOptions.Paper,receiptPrintOptions.PrinterName is not null,
        discoveredPrinterNames is null?null:Math.Min(discoveredPrinterNames.Length,256),
        discoveredPrinterNames is null||receiptPrintOptions.PrinterName is null?null:discoveredPrinterNames.Contains(receiptPrintOptions.PrinterName,StringComparer.OrdinalIgnoreCase),
        printerDiscoveryFailed,detectedDisplayCount,displayDiscoveryFailed,displays?.Active==true,displaySafetyBlocked,scanModeEnabled,printerCheckedAtUtc,displayCheckedAtUtc);
    private void RenderTerminalOverview()
    {
        if(!viewInitialized)return;
        if(terminalServiceChecks.Length==0)terminalServiceChecks=terminalDiagnostics.Initial();
        TerminalSetupList.ItemsSource=TerminalSetup.Rows(SetupFacts(),terminalServiceChecks);
        TerminalSetupControls.IsEnabled=!busy&&IsSignedIn()&&!displaySafetyBlocked;
    }
    private void ClearSetupDiagnostics()
    {
        setupCancellation?.Cancel();terminalServiceChecks=terminalDiagnostics.Initial();
        discoveredPrinterNames=null;printerCheckedAtUtc=null;printerDiscoveryFailed=false;
        TerminalSetupStatus.Text="Run explicit checks to refresh detection. Hardware acceptance is still untested.";
    }
    private async void RefreshTerminalSetup_Click(object sender,RoutedEventArgs e)
    {
        if(busy||!IsSignedIn()||displaySafetyBlocked)return;
        int generation=receiptSessionGeneration;string? subject=_authentication.CurrentToken?.Subject;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);setupCancellation=cancellation;
        SetBusy("Checking terminal setup…");
        try
        {
            try{RecordDisplayDiscovery(displayPlatform.Read().Count);}
            catch{RecordDisplayDiscovery(null);}
            async Task<string[]?> ReadPrinters(){try{return await DiscoverPrintersBoundedAsync(cancellation.Token);}catch{return null;}}
            var printers=ReadPrinters();var services=terminalDiagnostics.CheckAsync(cancellation.Token);
            await Task.WhenAll(printers,services);
            if(!PrintSessionCurrent(generation,subject))return;
            RecordPrinterDiscovery(await printers);terminalServiceChecks=await services;
            TerminalSetupStatus.Text="Checks complete. Detected means Windows/HTTP responded; actual output, scanner, display privacy and business access remain untested.";
            StatusText.Text="Terminal setup checks complete. Review the overview for results and untested items.";
        }
        catch{if(PrintSessionCurrent(generation,subject)){TerminalSetupStatus.Text="Setup checks could not complete. Review configuration and try again; no business action was submitted.";StatusText.Text="Terminal setup checks unavailable. Review the overview.";}}
        finally{setupCancellation=null;if(!windowCancellation.IsCancellationRequested)UpdateOperationalState();}
    }
    private void OpenInputSetup_Click(object sender,RoutedEventArgs e)=>NavigateSetup(TouchFirstCheckBox);
    private void OpenDisplaySetup_Click(object sender,RoutedEventArgs e)=>NavigateSetup(StaffDisplayPicker);
    private void OpenPrinterSetup_Click(object sender,RoutedEventArgs e)=>NavigateSetup(PrinterSetupControls);
    private void NavigateSetup(FrameworkElement target)
    {
        if(busy||!IsSignedIn()||displaySafetyBlocked)return;
        TerminalTab.IsSelected=true;target.BringIntoView();
    }
    private void OpenScannerSetup_Click(object sender,RoutedEventArgs e)
    {
        if(busy||!IsSignedIn()||displaySafetyBlocked)return;
        CheckoutTab.IsSelected=true;ScanModeCheckBox.BringIntoView();
        BarcodeScanStatus.Text="Enable scan entry, focus the barcode field, then explicitly scan/type and Add barcode. This navigation did not enable or submit anything.";
    }
    private void ExportTerminalSupport_Click(object sender,RoutedEventArgs e)
    {
        if(busy||!IsSignedIn()||displaySafetyBlocked)return;
        int generation=receiptSessionGeneration;string? subject=_authentication.CurrentToken?.Subject;
        var dialog=new SaveFileDialog{Title="Save sanitized POS support report",FileName=$"NexaConnect-POS-support-{DateTime.UtcNow:yyyyMMdd-HHmmss}Z.json",Filter="JSON report (*.json)|*.json",AddExtension=true,DefaultExt=".json",OverwritePrompt=true};
        try
        {
            if(dialog.ShowDialog(this)!=true||!PrintSessionCurrent(generation,subject))return;
            TerminalSupportReport.Create(SetupFacts(),terminalServiceChecks,typeof(MainWindow).Assembly.GetName().Version??new Version(0,0),Environment.Version,Environment.OSVersion.Version).Save(dialog.FileName);
            TerminalSetupStatus.Text="Sanitized support report saved. It contains configuration flags, counts, versions and last-check statuses; no identities, addresses or transaction data.";
        }
        catch{if(PrintSessionCurrent(generation,subject))TerminalSetupStatus.Text="Support report could not be saved. Check destination access and retry. Operational data is unchanged.";}
    }
}
