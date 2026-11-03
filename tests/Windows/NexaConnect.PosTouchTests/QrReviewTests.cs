using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.POS;

internal static partial class Program
{
    private static QrRequestView? fixtureQrRequest;
    private static string qrFixtureOutcome = "accepted";
    private static int qrFixtureDecisions;
    private static async Task<HttpResponseMessage> QrReviewFixtureResponse(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = JsonContent.Create(new QrRequestList(fixtureQrRequest is null ? [] : [fixtureQrRequest], false)) };
        Require(request.Method == HttpMethod.Post && fixtureQrRequest is not null, "only explicit synthetic QR decisions are permitted");
        qrFixtureDecisions++;
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        Require(body.RootElement.GetProperty("expectedVersion").GetInt64() == fixtureQrRequest!.Version, "native decision retains reviewed version");
        if (qrFixtureOutcome == "conflict") return new(HttpStatusCode.Conflict);
        bool reject = request.RequestUri!.AbsolutePath.EndsWith("/reject");
        if (reject) Require(body.RootElement.GetProperty("reason").GetString() == "closing", "predefined rejection reason is preserved");
        fixtureQrRequest = fixtureQrRequest with { Version = fixtureQrRequest.Version + 1, Status = reject ? "rejected" : qrFixtureOutcome,
            OrderId = !reject && qrFixtureOutcome == "accepted" ? fixtureQrRequest.Id : null,
            OrderStatus = !reject && qrFixtureOutcome == "accepted" ? "submitted" : null };
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(fixtureQrRequest) };
    }
    private static QrRequestView NewFixtureQr() => new(Guid.NewGuid(), "TEST-A1", "pending", 1, DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMinutes(10), null, new(new string('b', 64), "THB", DateTimeOffset.UtcNow.AddMinutes(2),
            [new(Guid.NewGuid(), "Synthetic rice", 80, 1, 80)], 80, 80, 0, 0, 80, 0, false, 0));

    private static void VerifyQrReview(MainWindow window, PosClientConfiguration configuration, bool preview)
    {
        fixtureQrRequest = NewFixtureQr(); qrFixtureDecisions = 0;
        var tab = (TabItem)window.FindName("QrRequestsTab"); var grid = (DataGrid)window.FindName("QrRequestsGrid");
        var confirm = (CheckBox)window.FindName("QrDecisionConfirmed");
        var accept = (Button)window.FindName("AcceptQrRequestButton"); var reject = (Button)window.FindName("RejectQrRequestButton");
        tab.IsSelected = true; Pump(window);
        Require(grid.Items.Count == 1, "native queue loads without a shift or collection");
        grid.SelectedIndex = 0; Pump(window);
        Require(!accept.IsEnabled && !reject.IsEnabled, "decisions require explicit reviewed confirmation");
        Require(((TextBlock)window.FindName("QrRequestDetails")).Text.Contains("Synthetic rice"), "review displays original line and server totals");
        if (preview)
        {
            window.Width = 1366; window.Height = 768; Pump(window);
            SavePreview(window, ".runstate/pos-qr-review-1366.png");
        }
        foreach (var size in new[] { new Size(960, 640), new Size(1366, 768), new Size(1920, 1080) })
        {
            window.Width = size.Width; window.Height = size.Height; Pump(window);
            var scroll = (ScrollViewer)window.FindName("QrRequestsScroll");
            accept.BringIntoView(); Pump(window);
            var bounds = accept.TransformToAncestor(scroll).TransformBounds(new Rect(accept.RenderSize));
            Require(bounds.Top >= -1 && bounds.Bottom <= scroll.ActualHeight + 1, "QR decisions remain reachable at supported logical sizes");
        }
        if (preview) SavePreview(window, ".runstate/pos-qr-review-details.png");
        confirm.IsChecked = true; Require(accept.IsEnabled, "reviewed pending request permits explicit acceptance");
        accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
        Require(qrFixtureDecisions == 1 && fixtureQrRequest.Status == "accepted", "one explicit original acceptance command");
        Require(!accept.IsEnabled && !reject.IsEnabled && ((Button)window.FindName("ViewAcceptedQrOrderButton")).IsEnabled, "accepted request offers navigation, no new decision");
        Require(typeof(MainWindow).GetField("pendingCheckout", Private)!.GetValue(window) is null, "acceptance creates no checkout or payment custody");
        VerifyUnifiedQrHandoff(window, configuration, fixtureQrRequest.Id, preview);

        fixtureQrRequest = NewFixtureQr(); qrFixtureOutcome = "conflict";
        ((RadioButton)window.FindName("QrReviewView")).IsChecked = true;
        tab.IsSelected = true; Pump(window);
        Invoke(window, "RefreshQrRequests_Click", window, new RoutedEventArgs()); Pump(window); grid.SelectedIndex = 0; confirm.IsChecked = true;
        accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
        Require(grid.Items.Count == 0 && !accept.IsEnabled && !reject.IsEnabled, "conflict clears stale decisions until explicit reload");
        Require(((TextBlock)window.FindName("QrRequestsNotice")).Text.Contains("Refresh the original"), "uncertain original result gives safe reload guidance");

        qrFixtureOutcome = "price_review";
        Invoke(window, "RefreshQrRequests_Click", window, new RoutedEventArgs()); Pump(window); grid.SelectedIndex = 0; confirm.IsChecked = true;
        accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
        Require(fixtureQrRequest.Status == "price_review" && fixtureQrRequest.OrderId is null, "changed prices do not create an accepted order");
        Require((string)accept.Content == "Refresh guest price review" && !accept.IsEnabled, "guest consent cannot be bypassed by acceptance button");
        ((ComboBox)window.FindName("QrRejectionReason")).SelectedIndex = 1; confirm.IsChecked = true;
        reject.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
        Require(fixtureQrRequest.Status == "rejected", "explicit allowlisted rejection works");

        ((TabItem)window.FindName("CheckoutTab")).IsSelected = true; Pump(window);
        Require(grid.Items.Count == 0 && !((TextBlock)window.FindName("QrRequestDetails")).Text.Contains("Synthetic rice"), "tab exit clears request details");
        fixtureQrRequest = NewFixtureQr(); tab.IsSelected = true; Pump(window); grid.SelectedIndex = 0;
        typeof(Window).GetMethod("OnDeactivated", Private)!.Invoke(window, [EventArgs.Empty]); Pump(window);
        Require(grid.Items.Count == 0 && !confirm.IsChecked!.Value, "deactivation clears QR data and confirmation");
        ((TabItem)window.FindName("CheckoutTab")).IsSelected = true; fixtureQrRequest = null; qrFixtureOutcome = "accepted";
        Console.WriteLine("PASS: synthetic native QR review, explicit decisions, conflict reload, guest-price consent, rejection and privacy; no live orders/payments.");
    }
}
