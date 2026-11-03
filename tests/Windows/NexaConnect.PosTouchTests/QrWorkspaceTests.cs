using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.POS;

internal static partial class Program
{
    private static bool qrWorkspaceTesting;
    private static PosBranchOrder[] qrOpenRows = [], qrPaidRows = [];
    private static int qrReceiptReads;
    private static TaskCompletionSource<HttpResponseMessage>? heldQrPage;

    private static Task<HttpResponseMessage> QrWorkspaceFixtureResponse(HttpRequestMessage request, PosClientConfiguration configuration)
    {
        Require(request.Method == HttpMethod.Get, "view switches and history never issue payment or collection commands");
        if (request.RequestUri!.AbsolutePath.EndsWith("/receipt"))
        {
            qrReceiptReads++; var paid = qrPaidRows.Single();
            Require(request.RequestUri.AbsolutePath.Contains(paid.OrderId.ToString("D")), "receipt reads original paid QR order");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PosReceipt(1,
                $"R-{paid.OrderId:N}".ToUpperInvariant(), paid.OrderId, configuration.OrganizationId, configuration.RestaurantId,
                configuration.BranchId, paid.OrderNumber, DateTimeOffset.UtcNow, "THB", "cash_manual",
                [new(Guid.NewGuid(), "Synthetic rice", 80, 1, 80)], null, 80, 0, 0, 80)) });
        }
        Require(request.RequestUri.Query.Contains("channel=qr"), "unified workspace always requests QR source");
        if (heldQrPage is not null) return heldQrPage.Task; // Intentionally ignore cancellation to prove the generation fence.
        bool history = request.RequestUri.Query.Contains("state=paid");
        var rows = history ? qrPaidRows : qrOpenRows;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PosBranchOrderPage(
            configuration.OrganizationId, configuration.RestaurantId, configuration.BranchId, rows, null)) });
    }

    private static void VerifyUnifiedQrHandoff(MainWindow window, PosClientConfiguration configuration, Guid original, bool preview)
    {
        qrWorkspaceTesting = true; qrReceiptReads = 0;
        var open = new PosBranchOrder(original, "QR-SYNTHETIC", DateTimeOffset.UtcNow, "qr", "KitchenAccepted", "THB", 80,
            "TEST-A1", "QR-" + original.ToString("N"), "awaiting_cashier", true);
        qrOpenRows = [open]; qrPaidRows = [open with { Status = "Paid", CollectionStatus = "paid", CanCollectQr = false }];
        var grid = (DataGrid)window.FindName("QrAcceptedGrid"); var load = (Button)window.FindName("LoadQrPaymentButton");
        var receipt = (Button)window.FindName("ViewQrReceiptButton");
        ((Button)window.FindName("ViewAcceptedQrOrderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
        Require(((TabItem)window.FindName("QrRequestsTab")).IsSelected && ((RadioButton)window.FindName("QrAwaitingView")).IsChecked == true, "acceptance handoff stays in the combined QR orders workspace");
        Require(grid.SelectedItem is PosBranchOrder item && item.OrderId == original, "handoff selects original UUID without typing");
        Require(!load.IsEnabled && typeof(MainWindow).GetField("pendingCheckout", Private)!.GetValue(window) is null, "navigation creates no custody and collection still requires shift");
        var shiftField = typeof(MainWindow).GetField("_activeShift", Private)!; var originalShift = shiftField.GetValue(window);
        shiftField.SetValue(window, new LocalShiftState(Guid.NewGuid(), "QR-FIXTURE", DateTimeOffset.UtcNow)); Invoke(window, "UpdateOperationalState");
        Require(load.IsEnabled, "existing eligible-row and open-shift guards permit explicit load for payment");
        typeof(MainWindow).GetField("pendingOrder", Private)!.SetValue(window, new PosOrderResult(Guid.NewGuid(), PosOrderStatus.KitchenAccepted, 80, "THB")); Invoke(window, "UpdateOperationalState");
        Require(!load.IsEnabled, "competing pending work blocks QR collection");
        typeof(MainWindow).GetField("pendingOrder", Private)!.SetValue(window, null); shiftField.SetValue(window, originalShift); Invoke(window, "UpdateOperationalState");
        var reference = (Expander)window.FindName("FindQrReference"); reference.IsExpanded = true;
        ((TextBox)window.FindName("QrReferenceTextBox")).Text = "synthetic-reference";
        ((RadioButton)window.FindName("QrHistoryView")).IsChecked = true; Pump(window); grid.SelectedIndex = 0; Pump(window);
        Require(((TextBlock)window.FindName("QrAcceptedDetails")).Text.Contains(original.ToString("D")), "selected paid row displays the original order identity and total");
        Require(!reference.IsExpanded && ((TextBox)window.FindName("QrReferenceTextBox")).Text.Length == 0, "view change collapses secondary lookup and clears its transient reference");
        Require(!load.IsEnabled && load.Visibility == Visibility.Collapsed && receipt.IsEnabled, "paid history offers receipt and never collection");
        foreach (var size in new[] { new Size(960, 640), new Size(1366, 768), new Size(1920, 1080) })
        {
            window.Width = size.Width; window.Height = size.Height; Pump(window);
            receipt.BringIntoView(); Pump(window);
            var viewport = (ScrollViewer)window.FindName("InputViewport");
            var bounds = receipt.TransformToAncestor(viewport).TransformBounds(new Rect(receipt.RenderSize));
            Require(bounds.Top >= -1 && bounds.Bottom <= viewport.ActualHeight + 1, "paid-history receipt action remains reachable at supported logical sizes");
        }
        if (preview) SavePreview(window, ".runstate/pos-qr-unified-paid.png");
        receipt.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
        Require(qrReceiptReads == 1 && ((TabItem)window.FindName("ReceiptsTab")).IsSelected, "explicit paid-history action opens original receipt without payment");

        ((TabItem)window.FindName("QrRequestsTab")).IsSelected = true; Pump(window);
        heldQrPage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Invoke(window, "RefreshQrAccepted_Click", window, new RoutedEventArgs()); Pump(window);
        var late = heldQrPage; heldQrPage = null;
        ((RadioButton)window.FindName("QrReviewView")).IsChecked = true; Pump(window);
        late.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PosBranchOrderPage(
            configuration.OrganizationId, configuration.RestaurantId, configuration.BranchId, qrPaidRows, null)) });
        for (int i = 0; i < 5; i++) { Thread.Sleep(10); Pump(window); }
        Require(grid.Items.Count == 0 && !receipt.IsEnabled, "late paid-page response cannot restore a different view's data or actions");
        qrWorkspaceTesting = false; qrOpenRows = []; qrPaidRows = [];
        Console.WriteLine("PASS: unified QR review/payment/history, original-ID navigation, shift/pending guards, paid receipt and late-response privacy; synthetic data only.");
    }
}
