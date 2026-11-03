using System.Windows;
using System.Windows.Controls;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly PosQrReviewState qrReviewState = new();
    private CancellationTokenSource? qrReviewCancellation;
    private bool qrReviewLoading;
    private Guid? qrReviewOrderNavigation;

    private void ClearQrRequests()
    {
        qrReviewCancellation?.Cancel();
        qrReviewCancellation = null; qrReviewLoading = false;
        qrReviewState.Clear();
        QrRequestsGrid.ItemsSource = null;
        QrDecisionConfirmed.IsChecked = false;
        QrRequestDetails.Text = "Select a request to review its items and totals.";
        QrRequestsNotice.Text = "Refresh requests to review this branch.";
        ClearQrAcceptedOrders();
        FindQrReference.IsExpanded = false;
        QrReferenceTextBox.Clear(); QrCollectionDetailsText.Text = "";
        UpdateQrRequestControls();
    }

    private async void QrRequestFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!viewInitialized) return;
        ClearQrRequests();
        await LoadQrRequestsAsync();
    }
    private async void RefreshQrRequests_Click(object sender, RoutedEventArgs e) => await LoadQrRequestsAsync();
    private void QrRequestSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!viewInitialized) return;
        QrDecisionConfirmed.IsChecked = false;
        var item = QrRequestsGrid.SelectedItem as PosQrRequest;
        QrRequestDetails.Text = item is null ? "Select a request to review its items and totals."
            : $"Table {item.TableCode} · {item.StatusLabel}\nRequest {item.Id:D}\nDeadline {item.DeadlineUtc.LocalDateTime:g}\n\n{item.Details}";
        UpdateQrRequestControls();
    }
    private void QrDecisionConfirmation_Changed(object sender, RoutedEventArgs e) { if (viewInitialized) UpdateQrRequestControls(); }
    private void QrRejectionReason_Changed(object sender, SelectionChangedEventArgs e)
    { if (viewInitialized) { QrDecisionConfirmed.IsChecked = false; UpdateQrRequestControls(); } }

    private void UpdateQrRequestControls()
    {
        if (!viewInitialized) return;
        bool enabled = IsSignedIn() && !busy && !qrReviewLoading;
        QrRequestFilter.IsEnabled = RefreshQrRequestsButton.IsEnabled = QrRequestsGrid.IsEnabled = enabled;
        var item = QrRequestsGrid.SelectedItem as PosQrRequest;
        QrDecisionConfirmed.IsEnabled = QrRejectionReason.IsEnabled = enabled && item?.Status is "pending" or "price_review";
        AcceptQrRequestButton.Content = item?.Status == "price_review" ? "Refresh guest price review" : "Accept original request";
        AcceptQrRequestButton.IsEnabled = RejectQrRequestButton.IsEnabled = qrReviewState.CanDecide(item?.Id, IsSignedIn(), busy || qrReviewLoading, QrDecisionConfirmed.IsChecked == true);
        ViewAcceptedQrOrderButton.IsEnabled = enabled && item?.Status == "accepted" && item.OrderId == item.Id;
        UpdateQrAcceptedControls();
    }

    private async Task LoadQrRequestsAsync()
    {
        if (!QrRequestsTab.IsSelected || QrReviewView.IsChecked != true || !IsSignedIn() || busy || qrReviewLoading || _authentication.CurrentToken is not { } token) return;
        // Keep a selected snapshot stable while an operator reviews it; automatic polling skips selection.
        string filter = (QrRequestFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "pending";
        ClearQrRequests();
        int generation = qrReviewState.Generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);
        qrReviewCancellation = cancellation; qrReviewLoading = true; UpdateQrRequestControls();
        QrRequestsNotice.Text = "Loading QR requests…";
        try
        {
            var page = await _api.ListQrRequestsAsync(token, filter, cancellation.Token);
            if (!IsSignedIn() || !qrReviewState.Apply(generation, page)) return;
            QrRequestsGrid.ItemsSource = qrReviewState.Items;
            QrRequestsNotice.Text = page.Items.Count == 0 ? "No matching QR requests."
                : $"{page.Items.Count} requests shown." + (page.HasMore ? " More requests remain; refresh after decisions to review the next batch." : "");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation == qrReviewState.Generation) ShowQrReviewError(error);
        }
        finally
        {
            if (ReferenceEquals(qrReviewCancellation, cancellation)) { qrReviewCancellation = null; qrReviewLoading = false; }
            if (!windowCancellation.IsCancellationRequested) UpdateQrRequestControls();
        }
    }

    private async void AcceptQrRequest_Click(object sender, RoutedEventArgs e) => await DecideQrRequestAsync(false);
    private async void RejectQrRequest_Click(object sender, RoutedEventArgs e) => await DecideQrRequestAsync(true);
    private async Task DecideQrRequestAsync(bool reject)
    {
        if (!qrReviewState.CanDecide((QrRequestsGrid.SelectedItem as PosQrRequest)?.Id, IsSignedIn(), busy || qrReviewLoading, QrDecisionConfirmed.IsChecked == true)
            || QrRequestsGrid.SelectedItem is not PosQrRequest item || _authentication.CurrentToken is not { } token) return;
        string? reason = reject ? (QrRejectionReason.SelectedItem as ComboBoxItem)?.Tag as string : null;
        if (reject && reason is null) return;
        int generation = qrReviewState.Generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);
        qrReviewCancellation = cancellation; qrReviewLoading = true;
        SetBusy("Checking original QR request decision…"); UpdateQrRequestControls();
        try
        {
            var result = await _api.DecideQrRequestAsync(token, item.Id, item.Version, reason, cancellation.Token);
            if (!IsSignedIn() || generation != qrReviewState.Generation) return;
            qrReviewState.Clear();
            qrReviewState.Apply(qrReviewState.Generation, new([result], false));
            QrRequestsGrid.ItemsSource = qrReviewState.Items; QrRequestsGrid.SelectedItem = result;
            QrDecisionConfirmed.IsChecked = false;
            QrRequestsNotice.Text = result.Status switch
            {
                "accepted" => "Original request accepted. Preparation continues separately. View the original order in Awaiting payment; acceptance does not collect payment.",
                "price_review" => "Not accepted: the original guest must confirm updated prices. Refresh requests after confirmation.",
                _ => "Original request rejected. No new order was created."
            };
        }
        catch (Exception error)
        {
            if (generation == qrReviewState.Generation) ShowQrReviewError(error);
        }
        finally
        {
            if (ReferenceEquals(qrReviewCancellation, cancellation)) { qrReviewCancellation = null; qrReviewLoading = false; }
            if (!windowCancellation.IsCancellationRequested) UpdateOperationalState();
        }
    }

    private void ShowQrReviewError(Exception error)
    {
        ClearQrRequests(); // Includes uncertain outcomes: force a reload before another decision.
        if (error is PosApiException { StatusCode: 401 }) { LockSession("Sign in again to review QR requests."); return; }
        QrRequestsNotice.Text = error is PosApiException { StatusCode: 403 }
            ? "Your account cannot perform this QR request action for the branch."
            : error is PosApiException { StatusCode: 404 }
                ? "QR review or acceptance is unavailable, or the original request no longer exists."
                : "The outcome is unavailable or changed. Refresh the original requests before retrying; do not create a replacement order.";
    }

    private void ViewAcceptedQrOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewAcceptedQrOrderButton.IsEnabled || QrRequestsGrid.SelectedItem is not PosQrRequest { OrderId: { } id }) return;
        // Navigate only. Existing Orders/preview/claim/payment guards remain authoritative.
        qrReviewOrderNavigation = id;
        if ((QrRequestsGrid.SelectedItem as PosQrRequest)?.OrderStatus == "paid") QrHistoryView.IsChecked = true;
        else QrAwaitingView.IsChecked = true;
    }
}
