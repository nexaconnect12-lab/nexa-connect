using System.Windows;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private async void CollectQr_Click(object sender, RoutedEventArgs e)
    {
        if (!IsSignedIn() || busy || _activeShift is null || _authentication.CurrentToken is not { } token) return;
        if (pendingCheckout is null && (pendingOrder is not null || cart.Count != 0))
        { StatusText.Text="Finish the current checkout before collecting a QR order."; return; }
        if (pendingCheckout is { IsQrCollection:false }) return;
        try
        {
            if (pendingCheckout is null)
            {
                var created = PendingCheckout.ForQr(_configuration,QrReferenceTextBox.Text.Trim(),_activeShift.ShiftId,token.Subject ?? "");
                SetBusy("Checking original QR order…");
                await _api.CollectQrAsync(token,created,preview:true); // Read-only: invalid references cannot strand local custody.
                _localStore.SavePendingCheckout(created); // Persist operation and custody before the claim can commit.
                pendingCheckout=created;
            }
            SetBusy("Verifying original QR collection…");
            await ResumeQrCollectionAsync();
        }
        catch (Exception)
        { StatusText.Text="QR collection could not be verified. Check the reference, original cashier/shift and access. Keep this original recovery; do not collect or recreate the order."; }
        finally { UpdateOperationalState(); }
    }

    private async Task ResumeQrCollectionAsync()
    {
        var checkout=pendingCheckout ?? throw new InvalidOperationException();
        if (_activeShift?.ShiftId != checkout.QrShiftId) throw new InvalidOperationException("Original collecting shift required.");
        var collection=await _api.CollectQrAsync(_authentication.CurrentToken!,checkout);
        if (collection.Status=="Paid")
        {
            _localStore.ClearCheckoutAndSettlement(); pendingCheckout=null;pendingOrder=null;settlementIdempotencyKey=null;settlementUncertain=false;
            customerState.Complete();PublishCustomerDisplay();
            StatusText.Text="Original QR order is already Paid. Do not collect again.";
            await PreviewPaidReceiptAsync(collection.OrderId);return;
        }
        // Reverification must not replace retained uncertain tender fields.
        var retained=_localStore.LoadPendingSettlement();
        if (retained is null)
            _localStore.SavePendingSettlement(new(collection.OrderId,collection.TotalAmount,collection.Currency,checkout.SettlementKey,Pricing:collection.Pricing));
        var confirmed=checkout with { Lines=collection.Lines.Select(l=>new CheckoutLine(l.ProductId,l.Quantity)).ToArray() };
        _localStore.SavePendingCheckout(confirmed);pendingCheckout=confirmed;
        pendingOrder=new(collection.OrderId,PosOrderStatus.KitchenAccepted,collection.TotalAmount,collection.Currency,Pricing:collection.Pricing);
        settlementIdempotencyKey=checkout.SettlementKey;
        settlementUncertain=retained?.OutcomeUncertain ?? false;
        customerState.Quote(new("",collection.Pricing,collection.Lines),collection.Currency);
        CustomerOrderReceived(pendingOrder);
        QrCollectionDetailsText.Text=$"Table {collection.TableCode} · Original Order {collection.OrderId:D}\n"
            +string.Join("\n",collection.Lines.Select(l=>$"{l.Quantity} × {l.Name} @ {collection.Currency} {l.UnitPrice:N2}"))
            +"\n"+collection.Pricing.Summary;
        PricingBreakdownText.Text=collection.Pricing.Summary;
        PaymentTab.IsSelected=true;
        StatusText.Text="Original QR order loaded. Verify cash or manual PromptPay received before confirming payment.";
    }
}
