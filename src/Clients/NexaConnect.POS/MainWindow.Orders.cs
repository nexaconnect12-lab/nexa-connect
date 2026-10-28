using System.Windows;
using System.Windows.Controls;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly PosOrderListState orderListState = new();
    private CancellationTokenSource? orderListCancellation;
    private bool orderListLoading;
    private string? orderListCursor;
    private readonly System.Windows.Threading.DispatcherTimer orderListTimer = new() { Interval = TimeSpan.FromSeconds(15) };

    private async void OrderListTimer_Tick(object? sender, EventArgs e)
    {
        if (IsActive && OrdersTab.IsSelected && IsSignedIn() && !busy && !orderListLoading)
            await LoadOrdersAsync(orderListCursor, automatic: true);
    }

    private void ClearOrderList()
    {
        orderListCancellation?.Cancel();
        orderListState.Clear();
        orderListCursor = null;
        OrdersGrid.ItemsSource = null;
        OrdersNoticeText.Text = "Refresh orders to view the current branch. Guest requests appear after staff accepts them.";
    }

    private async void WorkspaceTab_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!viewInitialized || !ReferenceEquals(e.Source, WorkspaceTabs)) return;
        HideTouchInput();
        RenderWorkflow();
        if (OrdersTab.IsSelected) await LoadOrdersAsync(null);
        else ClearOrderList();
    }

    private async void OrdersFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!viewInitialized) return;
        ClearOrderList(); UpdateOrderListControls();
        await LoadOrdersAsync(null);
    }

    private void OrdersSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (viewInitialized) UpdateOrderListControls();
    }

    private async void RefreshOrders_Click(object sender, RoutedEventArgs e) => await LoadOrdersAsync(null);
    private async void MoreOrders_Click(object sender, RoutedEventArgs e)
    {
        if (orderListState.NextCursor is { } cursor) await LoadOrdersAsync(cursor);
    }

    private async Task LoadOrdersAsync(string? cursor, bool automatic = false)
    {
        if (!IsSignedIn() || busy || orderListLoading || _authentication.CurrentToken is not { } token) return;
        Guid? selected = automatic ? (OrdersGrid.SelectedItem as PosBranchOrder)?.OrderId : null;
        ClearOrderList();
        int generation = orderListState.Generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);
        orderListCancellation = cancellation;
        orderListLoading = true;
        UpdateOrderListControls();
        try
        {
            OrdersNoticeText.Text = "Loading branch orders…";
            string channel = (OrdersChannelFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
            string state = (OrdersStateFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
            var page = await _api.ListOrdersAsync(token, channel, state, cursor, cancellation.Token);
            if (!IsSignedIn() || !orderListState.Apply(generation, page)) return;
            OrdersGrid.ItemsSource = orderListState.Items;
            orderListCursor = cursor;
            if (selected is not null) OrdersGrid.SelectedItem = orderListState.Items.FirstOrDefault(i => i.OrderId == selected);
            OrdersNoticeText.Text = page.Items.Count == 0 ? "No matching orders. Guest requests appear after staff accepts them."
                : $"{page.Items.Count} orders shown. Select an awaiting-cashier QR order to collect it."
                    + (page.NextCursor is null ? "" : " More results are available on the next page.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (PosApiException error)
        {
            if (generation != orderListState.Generation || windowCancellation.IsCancellationRequested) return;
            ClearOrderList();
            if (error.StatusCode == 401) LockSession("Sign in again to view branch orders.");
            else OrdersNoticeText.Text = error.StatusCode == 403 ? "Your account cannot view orders for this branch." : "Orders are unavailable. Refresh to retry.";
        }
        catch (Exception)
        {
            if (generation != orderListState.Generation || windowCancellation.IsCancellationRequested) return;
            ClearOrderList(); OrdersNoticeText.Text = "Orders could not be loaded. Refresh to retry.";
        }
        finally
        {
            if (ReferenceEquals(orderListCancellation, cancellation))
            { orderListCancellation = null; orderListLoading = false; }
            if (!windowCancellation.IsCancellationRequested) UpdateOrderListControls();
        }
    }

    private void UpdateOrderListControls()
    {
        bool enabled = IsSignedIn() && !busy && !orderListLoading;
        RefreshOrdersButton.IsEnabled = OrdersChannelFilter.IsEnabled = OrdersStateFilter.IsEnabled = enabled;
        MoreOrdersButton.IsEnabled = enabled && orderListState.NextCursor is not null;
        OrdersGrid.IsEnabled = enabled;
        CollectSelectedQrButton.IsEnabled = orderListState.CanCollect((OrdersGrid.SelectedItem as PosBranchOrder)?.OrderId,
            IsSignedIn(), _activeShift is not null, busy || orderListLoading, pendingCheckout is not null || pendingOrder is not null,
            cart.Count != 0, _configuration.PaymentMethod != "card_omise_test", _configuration.Currency);
        OrdersCollectionGuidance.Text = orderListState.CollectionGuidance((OrdersGrid.SelectedItem as PosBranchOrder)?.OrderId,
            IsSignedIn(), _activeShift is not null, busy || orderListLoading, pendingCheckout is not null || pendingOrder is not null,
            cart.Count != 0, _configuration.PaymentMethod != "card_omise_test", _configuration.Currency);
    }

    private void CollectSelectedQr_Click(object sender, RoutedEventArgs e)
    {
        UpdateOrderListControls();
        if (!CollectSelectedQrButton.IsEnabled || OrdersGrid.SelectedItem is not PosBranchOrder selected) return;
        ClearOrderList();
        QrReferenceTextBox.Text = selected.OrderId.ToString("D");
        CollectQr_Click(sender, e); // Existing preview, durable identity and claim workflow remain authoritative.
    }
}
