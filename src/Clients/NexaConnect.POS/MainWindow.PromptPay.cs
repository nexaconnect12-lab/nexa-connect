using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using NexaConnect.Contracts.Media;
namespace NexaConnect.POS;

public partial class MainWindow
{
    private PromptPayQrBundleV1? promptPayQrBundle;
    private Guid? manualProofOrderId;
    private bool promptPayQrReading;
    private DateTimeOffset nextPromptPayQrRefresh=DateTimeOffset.MinValue;
    private readonly System.Windows.Threading.DispatcherTimer promptPayQrTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private void InitializePromptPayQr()
    {
        RefreshPromptPayQrButton.Visibility=_configuration.UseBranchPromptPayQr?Visibility.Visible:Visibility.Collapsed;
        if(!_configuration.UseBranchPromptPayQr)return;
        PromptPayQrImage.Source=null;
        promptPayQrTimer.Tick+=PromptPayQrTick;promptPayQrTimer.Start();
    }
    private void ClearBranchPromptPayQr()
    {
        manualProofOrderId=null;BankReferenceTextBox.Clear();ReceiptConfirmedCheckBox.IsChecked=false;
        if(!_configuration.UseBranchPromptPayQr)return;
        promptPayQrBundle=null;promptPayQrReading=false;PromptPayQrImage.Source=null;nextPromptPayQrRefresh=DateTimeOffset.MinValue;
    }
    private bool PromptPayQrAvailable=>PromptPayQrImage.Source is not null&&(!_configuration.UseBranchPromptPayQr||promptPayQrBundle?.ExpiresAtUtc>DateTimeOffset.UtcNow);
    private async void PromptPayQrTick(object? sender,EventArgs e)
    {
        if(promptPayQrBundle is {} bundle&&bundle.ExpiresAtUtc<=DateTimeOffset.UtcNow){promptPayQrBundle=null;PromptPayQrImage.Source=null;RenderManualPaymentReadiness();}
        if(!busy&&!promptPayQrReading&&IsSignedIn()&&!displaySafetyBlocked&&PaymentTab.IsSelected&&SelectedTenderMethod()=="promptpay_manual"&&DateTimeOffset.UtcNow>=nextPromptPayQrRefresh)
            await RefreshBranchPromptPayQrAsync();
    }
    private async void RefreshPromptPayQr_Click(object sender,RoutedEventArgs e)
    {if(!busy&&!promptPayQrReading&&IsSignedIn()&&!displaySafetyBlocked)await RefreshBranchPromptPayQrAsync();}
    private async Task<bool> RefreshBranchPromptPayQrAsync()
    {
        if(!_configuration.UseBranchPromptPayQr)return PromptPayQrAvailable;
        if(promptPayQrReading||!IsSignedIn()||displaySafetyBlocked)return false;
        int generation=receiptSessionGeneration;string? subject=_authentication.CurrentToken?.Subject;Guid? order=pendingOrder?.OrderId;
        string? oldHash=promptPayQrBundle?.Image?.Sha256;Guid? oldAsset=promptPayQrBundle?.Selection.AssetId;
        promptPayQrReading=true;PromptPayQrImage.Source=null;RenderManualPaymentReadiness();
        nextPromptPayQrRefresh=DateTimeOffset.UtcNow.AddSeconds(30);
        try{
            var bundle=await _api.PromptPayQrAsync(_authentication.CurrentToken!,windowCancellation.Token);
            if(!PrintSessionCurrent(generation,subject)||pendingOrder?.OrderId!=order)return false;
            PromptPayQrPolicy.Validate(bundle,_configuration.OrganizationId,_configuration.RestaurantId,_configuration.BranchId,DateTimeOffset.UtcNow);
            BitmapSource? bitmap=null;
            if(bundle.Image is {} image){using var stream=new MemoryStream(image.Png,false);var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                if(decoder.Frames.Count!=1||decoder.Frames[0].PixelWidth is <64 or >2048||decoder.Frames[0].PixelHeight is <64 or >2048)throw new InvalidDataException();
                bitmap=decoder.Frames[0];bitmap.Freeze();}
            promptPayQrBundle=bundle;PromptPayQrImage.Source=bitmap;
            if((oldAsset!=bundle.Selection.AssetId||oldHash!=bundle.Image?.Sha256)&&_localStore.LoadPendingSettlement()?.Method is null)ReceiptConfirmedCheckBox.IsChecked=false;
            return bitmap is not null;
        }catch{
            if(PrintSessionCurrent(generation,subject)&&pendingOrder?.OrderId==order){promptPayQrBundle=null;PromptPayQrImage.Source=null;}
            return false;
        }finally{if(generation==receiptSessionGeneration){promptPayQrReading=false;RenderManualPaymentReadiness();}}
    }
    private void PaymentProof_Changed(object sender,RoutedEventArgs e)
    {if(viewInitialized&&!busy)RenderManualPaymentReadiness();}
    private void RenderManualPaymentReadiness()
    {
        if(!viewInitialized)return;
        try{
            var saved=_localStore.LoadPendingSettlement();bool original=saved?.Method is not null;
            if(saved is not null&&pendingOrder is not null&&(saved.OrderId!=pendingOrder.OrderId||saved.IdempotencyKey!=settlementIdempotencyKey))
            {PaidButton.IsEnabled=false;PaymentReadinessText.Text="Original checkout and payment recovery differ. Preserve recovery and request reconciliation.";return;}
            if(!IsSignedIn()){PaidButton.IsEnabled=false;RefreshPromptPayQrButton.IsEnabled=false;PaymentReadinessText.Text="Sign in or resume the original cashier.";return;}
            if(manualProofOrderId!=pendingOrder?.OrderId){
                manualProofOrderId=pendingOrder?.OrderId;
                BankReferenceTextBox.Text=original?saved!.BankReference??"":"";
                ReceiptConfirmedCheckBox.IsChecked=original&&saved!.ReceiptConfirmed;
            }
            PromptPayQrText.Text=original?"Verify using the saved receipt and reference. Do not request another transfer.":promptPayQrReading?"Refreshing the approved branch QR…":PromptPayQrAvailable?"Scan the approved branch QR, then verify receipt of the exact amount.":"No current approved QR. Configure it in Customer Portal → Configuration → PromptPay QR, then refresh.";
            string method=saved?.Method??SelectedTenderMethod();
            var readiness=ManualTenderReadiness.Evaluate(method,cashSessionId is not null,PromptPayQrAvailable,
                original?saved!.ReceiptConfirmed:ReceiptConfirmedCheckBox.IsChecked.GetValueOrDefault(),original?saved!.BankReference:BankReferenceTextBox.Text,original);
            bool context=IsSignedIn()&&_activeShift is not null&&pendingOrder is not null&&!settlementInFlight&&!busy&&!displaySafetyBlocked&&cancellationStatus is null&&!CardCheckout;
            PaidButton.IsEnabled=context&&readiness.Allowed&&(!promptPayQrReading||original||method!="promptpay_manual");
            PaymentReadinessText.Text=displaySafetyBlocked?"Restore the private staff display before confirming payment.":_activeShift is null?"Open or resume the original shift.":pendingOrder is null?"Send or load the original order before confirming payment.":CardCheckout?"Card checkout must use original-order verification, not manual payment.":busy||settlementInFlight?"A command is in progress. Wait; do not repeat it.":cancellationStatus is not null?"Resolve the original cancellation or reconciliation before confirming payment.":promptPayQrReading&&!original&&method=="promptpay_manual"?"Refreshing the approved branch QR…":readiness.Guidance;
            PaidButton.ToolTip=PaymentReadinessText.Text;
            RefreshPromptPayQrButton.IsEnabled=IsSignedIn()&&!busy&&!promptPayQrReading&&!displaySafetyBlocked;
        }catch{PaidButton.IsEnabled=false;PaymentReadinessText.Text="Payment recovery is unavailable. Preserve the original order and recovery files.";}
    }
}
