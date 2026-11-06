using System.Windows.Controls;
using System.Windows;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private sealed record CheckoutPaymentChoice(string Method,string Label);
    private sealed record CheckoutReview(PendingCheckout Checkout, PosOrderQuote Quote,
        Guid? OriginalOrder, Guid? Shift, Guid? CashSession);
    private CheckoutReview? checkoutReview;
    private bool renderingCheckoutPayment;
    private string CheckoutMethod => pendingCheckout?.PaymentMethod
        ?? (CheckoutPaymentPicker.SelectedItem as CheckoutPaymentChoice)?.Method ?? _configuration.PaymentMethod;
    private bool CardCheckout => CheckoutMethod == "card_omise_test";

    private void InitializeCheckoutPayments()
    {
        var choices=new List<CheckoutPaymentChoice>();
        if(_configuration.PaymentMethod != "card_omise_test")
            choices.Add(new(_configuration.PaymentMethod,"Cash / manual PromptPay"));
        if(_configuration.EnableOmiseTestCheckout)choices.Add(new("card_omise_test","Omise sandbox card — test only"));
        CheckoutPaymentPicker.ItemsSource=choices;
        SelectCheckoutPayment(pendingCheckout?.PaymentMethod ?? _configuration.PaymentMethod);
    }
    private void SelectCheckoutPayment(string method)
    {
        renderingCheckoutPayment=true;
        try{CheckoutPaymentPicker.SelectedItem=CheckoutPaymentPicker.Items.OfType<CheckoutPaymentChoice>().Single(c=>c.Method==method);}
        finally{renderingCheckoutPayment=false;}
    }
    private void RenderCheckoutPayments()
    {
        CheckoutPaymentStatus.Visibility = pendingCheckout is not null || CardCheckout
            ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if(pendingCheckout is not null)SelectCheckoutPayment(pendingCheckout.PaymentMethod);
        else if(!IsSignedIn())SelectCheckoutPayment(_configuration.PaymentMethod);
        if(!CardCheckout){if(CardTokenBox.Password.Length>0)CardTokenBox.Clear();cardTokenRequired=false;}
        CheckoutPaymentPicker.IsEnabled=IsSignedIn()&&!busy&&!displaySafetyBlocked&&pendingCheckout is null&&pendingOrder is null;
        CheckoutPaymentStatus.Text=pendingCheckout is not null
            ? "Original order payment method retained. Finish verification before changing methods."
            : _configuration.EnableOmiseTestCheckout
            ? "Sandbox card is configured locally; server permission and provider configuration are still required. QR collection uses cash/manual PromptPay."
            : "Omise sandbox is disabled in this terminal profile. Server setup and a matching profile are required; no secret keys are entered in POS.";
    }
    private void CheckoutPayment_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!viewInitialized||renderingCheckoutPayment)return;
        if(!IsSignedIn()||busy||displaySafetyBlocked||pendingCheckout is not null||pendingOrder is not null)
        {SelectCheckoutPayment(pendingCheckout?.PaymentMethod ?? _configuration.PaymentMethod);return;}
        CardTokenBox.Clear();cardTokenRequired=false;
        ClearCheckoutReview();
        CustomerCartChanged();UpdateOperationalState();
    }

    private void ClearCheckoutReview()
    {
        if (checkoutReview is null) return;
        checkoutReview = null;
        QuoteLinesScroll.Visibility = Visibility.Collapsed;
        QuoteLinesText.Text = "";
        PricingBreakdownText.Text = "Review server pricing before confirming the order.";
    }

    private bool CheckoutReviewIsCurrent(CheckoutReview review) =>
        review.OriginalOrder == pendingCheckout?.OrderId && review.Checkout.PaymentMethod == CheckoutMethod
        && review.Shift == _activeShift?.ShiftId && review.CashSession == cashSessionId;

    private async Task ReviewCheckoutAsync()
    {
        if (_authentication.CurrentToken is not { } token) return;
        var original = pendingCheckout;
        var shift = _activeShift?.ShiftId;
        var cash = cashSessionId;
        int session = receiptSessionGeneration;
        var checkout = original ?? PendingCheckout.Create(_configuration,
            cart.Select(line => new CheckoutLine(line.ProductId, line.Quantity)).ToArray(), CheckoutMethod);
        SetBusy("Loading server pricing…");
        var quote = await _api.QuoteAsync(token, checkout, windowCancellation.Token);
        var review = new CheckoutReview(checkout with { PricingFingerprint = quote.Fingerprint, NeedsPricingReview = false },
            quote, original?.OrderId, shift, cash);
        if (!PrintSessionCurrent(session, token.Subject) || !CheckoutReviewIsCurrent(review)) return;
        checkoutReview = review;
        QuoteLinesText.Text = string.Join("\n", quote.Lines.Select(line => $"{line.Quantity} × {line.Name} @ THB {line.UnitPrice:N2}"));
        QuoteLinesScroll.Visibility = Visibility.Visible;
        PricingBreakdownText.Text = quote.Pricing.Summary;
        customerState.Quote(quote, _configuration.Currency);
        PublishCustomerDisplay();
        StatusText.Text = "Review the server total, then send the order.";
    }
}
