using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.POS;

internal static partial class Program
{
    private static bool refundSelectionTesting;
    private static readonly Guid refundSelectionOrder = Guid.NewGuid(), refundSelectionIntent = Guid.NewGuid();
    private static decimal refundSelectionReserved;
    private static int refundSelectionCommands;
    private static HttpStatusCode? refundSelectionDenial;
    private static bool refundSelectionUncertain;
    private static int refundVerificationStatus = 200;
    private static bool refundVerificationInterrupted;
    private static TaskCompletionSource<HttpResponseMessage>? refundVerificationDelayed;
    private static TaskCompletionSource<HttpResponseMessage>? refundSelectionDelayed;

    private static void VerifyRefundSelection(MainWindow window, PosClientConfiguration config)
    {
        refundSelectionTesting = true; refundSelectionReserved = 0; refundSelectionCommands = 0;
        var receipts = (TabItem)window.FindName("ReceiptsTab");
        var orders = (TabItem)window.FindName("OrdersTab");
        var amount = (TextBox)window.FindName("RefundAmountTextBox");
        var id = (TextBox)window.FindName("RefundPaymentIntentIdTextBox");
        var confirmation = (CheckBox)window.FindName("RefundSelectionConfirmed");
        var issue = (Button)window.FindName("RefundButton");
        var pendingField = typeof(MainWindow).GetField("pendingRefund", Private)!;
        var selectionField = typeof(MainWindow).GetField("refundPayment", Private)!;
        try
        {
            receipts.IsSelected = true; Pump(window);
            Invoke(window, "ChooseRefundPayment_Click", window, new RoutedEventArgs()); Pump(window);
            Require(orders.IsSelected && ((ComboBox)window.FindName("OrdersStateFilter")).SelectedIndex == 2,
                "refund picker reuses the paginated paid-orders workspace");
            Load();
            Require(receipts.IsSelected && id.Text == refundSelectionIntent.ToString("D"), "selection binds original intent without UUID typing");
            Require(((TabItem)window.FindName("ProviderRefundSection")).IsSelected,
                "original-payment selection opens the provider-refund section without issuing a command");
            string details = ((TextBlock)window.FindName("RefundSelectedPaymentText")).Text;
            Require(details.Contains("ORDER-REFUND") && details.Contains("Noodle soup") && details.Contains("100.00"), "readable original payment details are shown");
            Require(!issue.IsEnabled, "selection never automatically issues or confirms a refund");
            amount.Text = "25"; confirmation.IsChecked = true; Pump(window);
            Require(issue.IsEnabled, "reviewed amount can proceed to server authorization");
            amount.Text = "26"; Require(confirmation.IsChecked == false && !issue.IsEnabled, "amount edit requires fresh confirmation");
            confirmation.IsChecked = true;
            ((ComboBox)window.FindName("RefundReasonComboBox")).SelectedIndex = 1;
            Require(confirmation.IsChecked == false && !issue.IsEnabled, "reason edit requires fresh confirmation");
            amount.Text = "101"; confirmation.IsChecked = true;
            Require(!issue.IsEnabled, "snapshot rejects over-refund before command");
            amount.Text = "25"; confirmation.IsChecked = true;
            refundSelectionReserved = 90;
            Invoke(window, "Refund_Click", window, new RoutedEventArgs());
            Wait(() => !(bool)typeof(MainWindow).GetField("busy", Private)!.GetValue(window)!);
            Require(refundSelectionCommands == 0 && pendingField.GetValue(window) is null && selectionField.GetValue(window) is null,
                "fresh read detects changed balance and never sends a refund command");
            refundSelectionReserved = 0;
            foreach (var denial in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
            {
                Load(); amount.Text = "10"; confirmation.IsChecked = true; refundSelectionDenial = denial;
                Invoke(window, "Refund_Click", window, new RoutedEventArgs());
                Wait(() => !(bool)typeof(MainWindow).GetField("busy", Private)!.GetValue(window)!);
                Require(pendingField.GetValue(window) is null && selectionField.GetValue(window) is null
                    && ((TextBlock)window.FindName("RefundStatusText")).Text.Contains("not authorized"),
                    "definitive authorization rejection clears the unaccepted operation and requires fresh review");
                var store = (LocalPosStore)typeof(MainWindow).GetField("_localStore", Private)!.GetValue(window)!;
                Require(store.LoadPendingRefund() is null, "definitively denied operation is cleared durably");
            }
            Require(refundSelectionCommands == 2, "only two deliberately rejected synthetic POSTs were attempted");
            refundSelectionCommands = 0; refundSelectionDenial = null;
            Load(); amount.Text = "10"; confirmation.IsChecked = true; refundSelectionUncertain = true;
            Invoke(window, "Refund_Click", window, new RoutedEventArgs());
            Wait(() => !(bool)typeof(MainWindow).GetField("busy", Private)!.GetValue(window)!);
            var uncertain = (LocalPendingRefundState?)pendingField.GetValue(window);
            Require(uncertain is not null && refundSelectionCommands == 1, "lost response retains original operation");
            Invoke(window, "Refund_Click", window, new RoutedEventArgs());
            Require(ReferenceEquals(pendingField.GetValue(window), uncertain) && refundSelectionCommands == 1,
                "repeated issue cannot send a second uncertain command");
            ((LocalPosStore)typeof(MainWindow).GetField("_localStore", Private)!.GetValue(window)!).ClearPendingRefund();
            pendingField.SetValue(window, null); refundSelectionCommands = 0; refundSelectionUncertain = false;
            Invoke(window, "UpdateOperationalState");
            refundSelectionReserved = 0; Load(); amount.Text = "10"; confirmation.IsChecked = true;
            id.Text = Guid.NewGuid().ToString("D");
            Require(selectionField.GetValue(window) is null && !issue.IsEnabled, "changed advanced ID invalidates original review");
            Load(); orders.IsSelected = true; Pump(window);
            Require(selectionField.GetValue(window) is null && amount.Text.Length == 0, "leaving receipts clears transient payment selection");
            Load(); Invoke(window, "ClearReceiptView");
            Require(selectionField.GetValue(window) is null && !issue.IsEnabled, "privacy transition clears details and confirmation");

            var pending = new LocalPendingRefundState(refundSelectionIntent, Guid.NewGuid(), 12.3456m, "THB", "service_issue");
            pendingField.SetValue(window, pending); Invoke(window, "UpdateOperationalState"); Pump(window);
            Require(!((Button)window.FindName("ChooseRefundPaymentButton")).IsEnabled && !amount.IsEnabled
                && !((ComboBox)window.FindName("RefundReasonComboBox")).IsEnabled && !issue.IsEnabled
                && amount.Text == "12.3456", "pending original operation freezes selection and exact amount/reason without render recursion");
            var local = (LocalPosStore)typeof(MainWindow).GetField("_localStore", Private)!.GetValue(window)!;
            local.SavePendingRefund(pending);
            foreach (int status in new[] { 401, 403, 404, 503, 200 })
            {
                refundVerificationStatus = status;
                Invoke(window, "LoadRefund_Click", window, new RoutedEventArgs());
                Wait(() => !(bool)typeof(MainWindow).GetField("busy", Private)!.GetValue(window)!);
                string message = ((TextBlock)window.FindName("RefundStatusText")).Text;
                Require(message == ((TextBlock)window.FindName("StatusText")).Text && !message.StartsWith("Verifying"),
                    "verification finishes in both visible status locations");
                Require(status != 404 || message.Contains("not found or access denied"), "404 is disclosure-safe and actionable");
                Require(ReferenceEquals(pendingField.GetValue(window), pending) && local.LoadPendingRefund() == pending,
                    "verification denial/missing/unavailable and pending reads never discard original recovery");
            }
            refundVerificationInterrupted = true;
            Invoke(window, "LoadRefund_Click", window, new RoutedEventArgs());
            Wait(() => !(bool)typeof(MainWindow).GetField("busy", Private)!.GetValue(window)!);
            Require(((TextBlock)window.FindName("RefundStatusText")).Text.Contains("timed out or was interrupted")
                && local.LoadPendingRefund() == pending, "interrupted read retains recovery and offers verification retry only");
            refundVerificationInterrupted = false;
            var visibleStatus = (TextBlock)window.FindName("RefundStatusText");
            Require(visibleStatus.Parent is Grid && Grid.GetRow(visibleStatus) == 0,
                "refund status stays outside the clipped/scrolled form");
            refundVerificationDelayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Invoke(window, "LoadRefund_Click", window, new RoutedEventArgs());
            Invoke(window, "ClearReceiptView");
            visibleStatus.Text = "Privacy fixture"; ((TextBlock)window.FindName("StatusText")).Text = "Privacy fixture";
            refundVerificationDelayed.TrySetResult(new(HttpStatusCode.NotFound));
            Wait(() => !(bool)typeof(MainWindow).GetField("busy", Private)!.GetValue(window)!);
            Require(visibleStatus.Text == "Privacy fixture" && ((TextBlock)window.FindName("StatusText")).Text == "Privacy fixture",
                "late verification errors cannot restore private/status data after a privacy transition");
            refundVerificationDelayed = null;
            local.ClearPendingRefund();
            Invoke(window, "ClearReceiptView"); Require(ReferenceEquals(pendingField.GetValue(window), pending), "privacy preserves durable pending operation identity");
            pendingField.SetValue(window, null); Invoke(window, "UpdateOperationalState");

            refundSelectionDelayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var delayed = (Task)typeof(MainWindow).GetMethod("LoadRefundSelectionAsync", Private)!.Invoke(window, [refundSelectionOrder, true])!;
            Invoke(window, "ClearReceiptView");
            refundSelectionDelayed.TrySetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(new { orderId = refundSelectionOrder }) });
            Wait(() => delayed.IsCompleted);
            Require(selectionField.GetValue(window) is null && !issue.IsEnabled, "delayed selection cannot restore private details after privacy transition");
            Require(refundSelectionCommands == 0, "presentation checks issue no financial command");
            Console.WriteLine("PASS: readable refund selection, confirmation edits, stale balance, pending recovery and privacy fences (synthetic only).");
        }
        finally
        {
            refundSelectionDelayed = null; refundSelectionDenial = null; refundSelectionUncertain = false;
            refundVerificationDelayed = null; refundVerificationInterrupted = false; refundVerificationStatus = 200;
            refundSelectionTesting = false; pendingField.SetValue(window, null);
            Invoke(window, "ClearReceiptView"); ((TabItem)window.FindName("CheckoutTab")).IsSelected = true;
            Invoke(window, "UpdateOperationalState"); Pump(window);
        }
        void Load()
        {
            var task = (Task)typeof(MainWindow).GetMethod("LoadRefundSelectionAsync", Private)!.Invoke(window, [refundSelectionOrder, true])!;
            Wait(() => task.IsCompleted); task.GetAwaiter().GetResult();
            Require(selectionField.GetValue(window) is PosRefundPayment, "synthetic captured original loads");
        }
        void Wait(Func<bool> ready)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(5);
            while (!ready() && DateTime.UtcNow < until) Pump(window);
            Require(ready(), "refund selection task completed within fixture deadline");
        }
    }
    private static Task<HttpResponseMessage> RefundSelectionFixtureResponse(HttpRequestMessage request, PosClientConfiguration config, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get)
        {
            refundSelectionCommands++;
            if (refundSelectionDenial is {} denial) return Task.FromResult(new HttpResponseMessage(denial));
            if (refundSelectionUncertain) return Task.FromException<HttpResponseMessage>(new HttpRequestException("Synthetic lost response"));
            throw new InvalidOperationException("No financial command is allowed in refund presentation fixture.");
        }
        string path = request.RequestUri!.AbsolutePath;
        if (path.Contains("/refunds/by-operation/"))
        {
            Require(ct.CanBeCanceled, "verification carries a bounded cancellation token");
            if (refundVerificationDelayed is {} verification) return verification.Task.WaitAsync(ct);
            if (refundVerificationInterrupted) return Task.FromCanceled<HttpResponseMessage>(new CancellationToken(true));
            if (refundVerificationStatus != 200) return Task.FromResult(new HttpResponseMessage((HttpStatusCode)refundVerificationStatus));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PosPaymentRefund(
                Guid.NewGuid(), config.OrganizationId, config.RestaurantId, config.BranchId, refundSelectionOrder, refundSelectionIntent,
                Guid.Parse(path.Split('/')[^1]), 12.3456m, "THB", "service_issue", "processing", DateTimeOffset.UtcNow, null, null, null)) });
        }
        if (refundSelectionDelayed is {} delayed && path.EndsWith(refundSelectionOrder.ToString("D"))) return delayed.Task.WaitAsync(ct);
        object value;
        if (path.EndsWith("/orders")) value = new PosBranchOrderPage(config.OrganizationId, config.RestaurantId, config.BranchId, [], null);
        else if (path.EndsWith("/receipt")) value = new PosReceipt(1, $"R-{refundSelectionOrder:N}".ToUpperInvariant(), refundSelectionOrder,
            config.OrganizationId, config.RestaurantId, config.BranchId, "ORDER-REFUND", DateTimeOffset.UtcNow, "THB", "card_omise_test",
            [new(Guid.NewGuid(), "Noodle soup", 100, 1, 100)], null, 100, 0, 0, 100);
        else if (path.EndsWith("/refunds")) value = refundSelectionReserved == 0 ? Array.Empty<PosPaymentRefund>() : [new PosPaymentRefund(
            Guid.NewGuid(), config.OrganizationId, config.RestaurantId, config.BranchId, refundSelectionOrder, refundSelectionIntent,
            Guid.NewGuid(), refundSelectionReserved, "THB", "customer_request", "processing", DateTimeOffset.UtcNow, null, null, null)];
        else if (path.Contains("/intents/")) value = new { id = refundSelectionIntent, config.OrganizationId, config.RestaurantId,
            config.BranchId, orderId = refundSelectionOrder, amount = 100m, currency = "THB", paymentMethod = "card", status = "captured" };
        else value = new { orderId = refundSelectionOrder, config.OrganizationId, config.BranchId, status = "Paid",
            totalAmount = 100m, currency = "THB", paymentIntentId = refundSelectionIntent };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) });
    }
}
