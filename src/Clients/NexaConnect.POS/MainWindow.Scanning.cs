using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private bool scanModeEnabled,barcodeReading,confirmationOpen;
    private long scanGeneration;
    private CancellationTokenSource? scanCancellation;
    private readonly BarcodeRepeatGuard barcodeRepeats=new();
    private void InitializeScanning()
    {
        Deactivated+=(_,_)=>ClearBarcodeInput();
        PreviewKeyDown+=(_,e)=>{if(scanModeEnabled&&e.Key==Key.Enter&&!BarcodeScanTextBox.IsKeyboardFocusWithin)e.Handled=true;};
    }
    private bool CanScanBarcode()=>BarcodeScanPolicy.CanScan(scanModeEnabled,IsActive,CheckoutTab.IsSelected,IsSignedIn(),_activeShift is not null,
        busy,pendingCheckout is not null||pendingOrder is not null,displaySafetyBlocked,confirmationOpen);
    private void ScanMode_Changed(object sender,RoutedEventArgs e)
    {
        if(!viewInitialized)return;
        scanModeEnabled=ScanModeCheckBox.IsChecked==true;ClearBarcodeInput();UpdateScanControls();
        RenderTerminalOverview();
        if(scanModeEnabled&&CanScanBarcode())BarcodeScanTextBox.Focus();
    }
    private void ClearBarcodeInput()
    {
        scanGeneration++;scanCancellation?.Cancel();BarcodeScanTextBox?.Clear();
    }
    private void UpdateScanControls()
    {
        BarcodeScanPanel.Visibility=scanModeEnabled?Visibility.Visible:Visibility.Collapsed;
        bool editable=CanEditCart()&&!displaySafetyBlocked&&!confirmationOpen&&CheckoutTab.IsSelected;
        ScanModeCheckBox.IsEnabled=editable;
        BarcodeScanTextBox.IsEnabled=AddBarcodeButton.IsEnabled=editable&&scanModeEnabled;
        if(!editable&&!barcodeReading)ClearBarcodeInput();
    }
    private void BarcodeScan_KeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Enter)return;
        e.Handled=true;
        if(!e.IsRepeat&&Keyboard.Modifiers==ModifierKeys.None)ScanBarcode_Click(sender,new RoutedEventArgs());
    }
    private async void ScanBarcode_Click(object sender,RoutedEventArgs e)
    {
        if(barcodeReading||!CanScanBarcode())return;
        string code=BarcodeScanTextBox.Text;BarcodeScanTextBox.Clear();
        if(!BarcodeScanPolicy.Valid(code)){BarcodeScanStatus.Text="Use a product barcode of 1–64 letters, digits, dots, underscores or hyphens.";return;}
        long now=Environment.TickCount64;
        if(barcodeRepeats.IsRepeat(code,now)){BarcodeScanStatus.Text="Repeated scan ignored. Wait briefly to scan again, or use + for another unit.";return;}
        long generation=scanGeneration;string? subject=_authentication.CurrentToken?.Subject;Guid? shift=_activeShift?.ShiftId;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);scanCancellation=cancellation;
        barcodeReading=true;SetBusy("Looking up barcode…");
        try
        {
            var match=await _api.LookupBarcodeAsync(_authentication.CurrentToken!,code,cancellation.Token);
            if(generation!=scanGeneration||subject!=_authentication.CurrentToken?.Subject||shift!=_activeShift?.ShiftId)return;
            UpdateOperationalState();
            if(!CanScanBarcode())return;
            if(TryAddMenuItem(match.Item)){barcodeRepeats.Accepted(code,Environment.TickCount64);BarcodeScanStatus.Text="One item added. Review the cart before confirming pricing.";}
            else BarcodeScanStatus.Text="Item is unavailable, has a different currency, or cannot be added to this cart.";
        }
        catch(PosApiException error)when(error.StatusCode==404){if(generation==scanGeneration)BarcodeScanStatus.Text="No product with this barcode in the configured branch. Ask a manager to check its binding.";}
        catch{if(generation==scanGeneration)BarcodeScanStatus.Text="Barcode lookup failed or access changed. No item was added. Check the connection and try again.";}
        finally
        {
            scanCancellation=null;barcodeReading=false;UpdateOperationalState();
            if(generation==scanGeneration&&CanScanBarcode())BarcodeScanTextBox.Focus();
        }
    }
    private bool ConfirmStaffAction(string message,string title,bool okCancel=false)
    {
        ClearBarcodeInput();confirmationOpen=true;
        int generation=receiptSessionGeneration;string? subject=_authentication.CurrentToken?.Subject;
        bool Current()=>PrintSessionCurrent(generation,subject);
        try{return scanModeEnabled?ScannerSafeConfirmation.Ask(this,message,title,Current):
            MessageBox.Show(this,message,title,okCancel?MessageBoxButton.OKCancel:MessageBoxButton.YesNo,MessageBoxImage.Warning)==(okCancel?MessageBoxResult.OK:MessageBoxResult.Yes);}
        finally{confirmationOpen=false;UpdateScanControls();}
    }
}
