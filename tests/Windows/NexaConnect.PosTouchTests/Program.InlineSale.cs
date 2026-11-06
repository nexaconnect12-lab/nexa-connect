using System.Collections;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.POS;

internal static partial class Program
{
    private static InlineSaleFixture? inlineSale;

    private static void VerifyInlineSale(MainWindow window)
    {
        var fixture = new InlineSaleFixture();
        var shift = typeof(MainWindow).GetField("_activeShift", Private)!;
        var cash = typeof(MainWindow).GetField("cashSessionId", Private)!;
        var store = (LocalPosStore)typeof(MainWindow).GetField("_localStore", Private)!.GetValue(window)!;
        var cart = (IList)typeof(MainWindow).GetField("cart", Private)!.GetValue(window)!;
        inlineSale = fixture;
        try
        {
            shift.SetValue(window, new LocalShiftState(Guid.NewGuid(), "INLINE", DateTimeOffset.UtcNow));
            cash.SetValue(window, Guid.NewGuid());
            cart.Clear();
            ((TabItem)window.FindName("CheckoutTab")).IsSelected = true;
            ((ComboBox)window.FindName("CheckoutPaymentPicker")).SelectedIndex = 0;
            ((ComboBox)window.FindName("TenderMethodComboBox")).SelectedIndex = 0;
            Invoke(window, "AddMenuItem_Click", new Button { DataContext = fixture.Item }, new RoutedEventArgs());
            var place = (Button)window.FindName("PlaceOrderButton");
            var paid = (Button)window.FindName("PaidButton");
            Require(Equals(place.Content, "Review order total") && place.IsEnabled, "Menu starts with a pricing review");
            place.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(() => Equals(place.Content, "Send order · Continue to payment") && place.IsEnabled);
            Require(fixture.Placed == 0 && store.LoadPendingCheckout() is null
                && ((TextBlock)window.FindName("QuoteLinesText")).Text.Contains("Inline sale")
                && ((TextBlock)window.FindName("WorkflowAmount")).Text == CashierPresentation.Money(50m, "THB"),
                "review is inline and does not send or persist a command");
            Invoke(window, "AddMenuItem_Click", new Button { DataContext = fixture.Item }, new RoutedEventArgs());
            Require(Equals(place.Content, "Review order total"), "cart edit discards the old quote");
            place.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(() => Equals(place.Content, "Send order · Continue to payment") && place.IsEnabled);
            place.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(() => paid.IsEnabled && fixture.Placed == 1);
            Require(store.LoadPendingCheckout() is { PricingFingerprint: InlineSaleFixture.Fingerprint },
                "send persists the reviewed fingerprint before placement");
            paid.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(() => fixture.Settled == 1 && store.LoadPendingCheckout() is null
                && !(bool)typeof(MainWindow).GetField("busy", Private)!.GetValue(window)!);
            paid.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(fixture.Settled == 1, "the payment action sends once without a second confirmation or collection");
        }
        finally { inlineSale = null; }
        Console.WriteLine("PASS: Menu reviews the server quote inline, cart edits discard it, and a single payment click settles the original fixture order.");

        void Wait(Func<bool> condition)
        {
            for (int i = 0; i < 200 && !condition(); i++) { Pump(window); Thread.Sleep(5); }
            Require(condition(), "synthetic inline sale action completed");
        }
    }

    private sealed class InlineSaleFixture
    {
        public const string Fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        public PosMenuItem Item { get; } = new(Guid.NewGuid(), "Inline sale", 50m, "THB", "Kitchen", true);
        public int Placed { get; private set; }
        public int Settled { get; private set; }
        private Guid order;
        private decimal amount;

        public HttpResponseMessage? Respond(HttpRequestMessage request)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Post) return null;
            using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var body = json.RootElement;
            if (path.EndsWith("/quote"))
            {
                int quantity = body.GetProperty("lines")[0].GetProperty("quantity").GetInt32();
                amount = quantity * Item.UnitPrice;
                return Json(new PosOrderQuote(Fingerprint, new(1, 0, false, 0, amount, amount, 0, 0, amount),
                    [new(Item.ProductId, Item.Name, Item.UnitPrice, quantity)]));
            }
            if (path.EndsWith("/place"))
            {
                Require(body.GetProperty("pricingFingerprint").GetString() == Fingerprint, "send uses the reviewed pricing");
                order = body.GetProperty("orderId").GetGuid();
                Placed++;
                return Json(new PosOrderResult(order, PosOrderStatus.KitchenAccepted, amount, "THB"));
            }
            if (path.EndsWith("/manual-settlement"))
            {
                Require(path.Contains(order.ToString("D")) && body.GetProperty("amount").GetDecimal() == amount
                    && body.GetProperty("method").GetString() == "cash", "settlement uses the original cash order and total");
                Settled++;
                return Json(new ManualTenderResult(Guid.NewGuid(), order, "Paid", "cash", amount, "THB", DateTimeOffset.UtcNow, false));
            }
            return null;
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
