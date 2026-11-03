using System.Windows;
using System.Windows.Controls;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly PosOrderListState qrAcceptedState = new();
    private CancellationTokenSource? qrAcceptedCancellation;
    private bool qrAcceptedLoading;
    private string? qrAcceptedCursor;

    private async void QrWorkspaceView_Changed(object sender, RoutedEventArgs e)
    {
        if (!viewInitialized) return;
        Guid? original = qrReviewOrderNavigation; qrReviewOrderNavigation = null;
        HideTouchInput(); ClearBarcodeInput(); ClearQrRequests();
        QrRequestsScroll.Visibility = QrReviewView.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        QrAcceptedOrdersPanel.Visibility = QrReviewView.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        QrAcceptedHeading.Text = QrHistoryView.IsChecked == true ? "Paid QR orders and receipts" : "Accepted QR orders awaiting payment · preparing orders become collectible when ready";
        if (QrReviewView.IsChecked == true) await LoadQrRequestsAsync();
        else await LoadQrAcceptedAsync(null, original);
    }

    private void ClearQrAcceptedOrders()
    {
        qrAcceptedCancellation?.Cancel(); qrAcceptedCancellation = null; qrAcceptedLoading = false;
        qrAcceptedState.Clear(); qrAcceptedCursor = null;
        QrAcceptedGrid.ItemsSource = null;
        QrAcceptedDetails.Text = "Select an order to review its status and total.";
        QrAcceptedNotice.Text = "Refresh orders to view this branch.";
        UpdateQrAcceptedControls();
    }

    private void UpdateQrAcceptedControls()
    {
        if (!viewInitialized) return;
        bool signedIn = IsSignedIn(), history = QrHistoryView.IsChecked == true;
        bool enabled = signedIn && !busy && !qrAcceptedLoading;
        QrReviewView.IsEnabled = QrAwaitingView.IsEnabled = QrHistoryView.IsEnabled = signedIn && !busy;
        QrAcceptedGrid.IsEnabled = RefreshQrAcceptedButton.IsEnabled = enabled;
        MoreQrAcceptedButton.IsEnabled = enabled && qrAcceptedState.NextCursor is not null;
        var item = QrAcceptedGrid.SelectedItem as PosBranchOrder;
        QrAcceptedDetails.Text = item is null ? "Select an order to review its status and total."
            : $"Table {item.TableCode} · {item.OrderNumber}\nOriginal Order {item.OrderId:D}\n{item.StatusLabel} · {item.CollectionLabel} · {item.Currency} {item.TotalAmount:N2}";
        bool pending = pendingCheckout is not null || pendingOrder is not null;
        LoadQrPaymentButton.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        ViewQrReceiptButton.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        LoadQrPaymentButton.IsEnabled = !history && qrAcceptedState.CanCollect(item?.OrderId, signedIn, _activeShift is not null,
            busy || qrAcceptedLoading, pending, cart.Count != 0, _configuration.PaymentMethod != "card_omise_test", _configuration.Currency);
        ViewQrReceiptButton.IsEnabled = PosQrWorkspacePolicy.CanViewReceipt(item, history, signedIn, busy || qrAcceptedLoading);
        QrAcceptedGuidance.Text = history ? "Paid orders cannot be collected again. View receipt rechecks access through the existing receipt workflow."
            : qrAcceptedState.CollectionGuidance(item?.OrderId, signedIn, _activeShift is not null, busy || qrAcceptedLoading,
                pending, cart.Count != 0, _configuration.PaymentMethod != "card_omise_test", _configuration.Currency);
    }

    private async void RefreshQrAccepted_Click(object sender, RoutedEventArgs e) => await LoadQrAcceptedAsync(null);
    private async void MoreQrAccepted_Click(object sender, RoutedEventArgs e)
    { if (qrAcceptedState.NextCursor is { } cursor) await LoadQrAcceptedAsync(cursor); }
    private void QrAcceptedSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!viewInitialized) return;
        UpdateQrAcceptedControls();
    }

    private async Task LoadQrAcceptedAsync(string? cursor, Guid? select = null)
    {
        if (!QrRequestsTab.IsSelected || QrReviewView.IsChecked == true || !IsSignedIn() || busy || qrAcceptedLoading || _authentication.CurrentToken is not { } token) return;
        bool history = QrHistoryView.IsChecked == true;
        ClearQrAcceptedOrders(); int generation = qrAcceptedState.Generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);
        qrAcceptedCancellation = cancellation; qrAcceptedLoading = true; UpdateQrAcceptedControls();
        QrAcceptedNotice.Text = "Loading QR orders…";
        try
        {
            var page = await _api.ListOrdersAsync(token, "qr", PosQrWorkspacePolicy.OrderFilter(history), cursor, cancellation.Token);
            PosQrWorkspacePolicy.ValidatePage(page, history);
            if (!IsSignedIn() || !qrAcceptedState.Apply(generation, page)) return;
            qrAcceptedCursor = cursor; QrAcceptedGrid.ItemsSource = qrAcceptedState.Items;
            QrAcceptedNotice.Text = page.Items.Count == 0 ? "No matching QR orders. Pending guest requests must be accepted first."
                : $"{page.Items.Count} QR orders shown." + (page.NextCursor is null ? "" : " More results are available on the next page.");
            if (select is { } original)
            {
                QrAcceptedGrid.SelectedItem = qrAcceptedState.Items.FirstOrDefault(i => i.OrderId == original);
                if (QrAcceptedGrid.SelectedItem is null) QrAcceptedNotice.Text = "Original order is not on this page. Refresh or use Next page; do not create a replacement.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation != qrAcceptedState.Generation) return;
            ClearQrAcceptedOrders();
            if (error is PosApiException { StatusCode: 401 }) LockSession("Sign in again to view QR orders.");
            else QrAcceptedNotice.Text = error is PosApiException { StatusCode: 403 }
                ? "Your account cannot view QR orders for this branch." : "QR orders are unavailable. Refresh the original orders to retry.";
        }
        finally
        {
            if (ReferenceEquals(qrAcceptedCancellation, cancellation)) { qrAcceptedCancellation = null; qrAcceptedLoading = false; }
            if (!windowCancellation.IsCancellationRequested) UpdateQrAcceptedControls();
        }
    }

    private void LoadQrPayment_Click(object sender, RoutedEventArgs e)
    {
        UpdateQrAcceptedControls();
        if (!LoadQrPaymentButton.IsEnabled || QrAcceptedGrid.SelectedItem is not PosBranchOrder item) return;
        Guid original = item.OrderId; ClearQrAcceptedOrders();
        QrReferenceTextBox.Text = original.ToString("D");
        CollectQr_Click(sender, e); // Existing read-only preview, durable custody and payment workflow.
    }

    private async void ViewQrReceipt_Click(object sender, RoutedEventArgs e)
    {
        UpdateQrAcceptedControls();
        if (!ViewQrReceiptButton.IsEnabled || QrAcceptedGrid.SelectedItem is not PosBranchOrder item) return;
        SetBusy("Loading original paid QR receipt…");
        try { await PreviewPaidReceiptAsync(item.OrderId); }
        catch (Exception) { StatusText.Text = "Receipt unavailable. Review the original paid order; do not collect payment again."; }
        finally { UpdateOperationalState(); }
    }
    private void FindQrReference_Collapsed(object sender, RoutedEventArgs e)
    { if (viewInitialized) { HideTouchInput(); QrReferenceTextBox.Clear(); QrCollectionDetailsText.Text = ""; } }
}
