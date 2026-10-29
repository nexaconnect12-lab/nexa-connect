using System.Windows.Controls;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private sealed record CheckoutPaymentChoice(string Method,string Label);
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
        CustomerCartChanged();UpdateOperationalState();
    }
}
