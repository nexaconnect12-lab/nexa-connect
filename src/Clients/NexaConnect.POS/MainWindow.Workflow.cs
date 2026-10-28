using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private CashierDestination nextDestination;
    private bool workflowMovementsPending;

    private void RenderWorkflow()
    {
        if (!viewInitialized) return;
        bool visible = !reauthenticationRequired && !displaySafetyBlocked;
        WorkflowDock.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) { nextDestination = CashierDestination.None; return; }
        var hint = CashierWorkflow.Resolve(new(IsSignedIn(), _activeShift is not null, cashSessionId is not null,
            _configuration.PaymentMethod == "cash_manual", pendingCheckout is not null, pendingOrder is not null,
            settlementUncertain, workflowMovementsPending, pendingCashReviewAttempt is not null, pendingRefund is not null, cancellationStatus));
        nextDestination = hint.Destination;
        WorkflowTitle.Text = busy ? "Working…" : hint.Title;
        WorkflowDescription.Text = busy ? "Wait for the current action to finish. Do not repeat it." : hint.Description;
        WorkflowTitle.Foreground = new SolidColorBrush(hint.Attention ? Color.FromRgb(146, 64, 14) : Color.FromRgb(20, 83, 45));
        WorkflowNextButton.Content = hint.Action;
        WorkflowNextButton.IsEnabled = !busy;
        WorkflowNextButton.Visibility = CurrentDestination() == hint.Destination || hint.Destination == CashierDestination.None
            ? Visibility.Collapsed : Visibility.Visible;
        PlaceOrderButton.Visibility = CheckoutTab.IsSelected ? Visibility.Visible : Visibility.Collapsed;
        PaidButton.Visibility = PaymentTab.IsSelected ? Visibility.Visible : Visibility.Collapsed;
        var amount = CashierWorkflow.Amount(TotalText.Text, pendingCheckout is not null, pendingOrder?.TotalAmount, pendingOrder?.Currency ?? _configuration.Currency);
        WorkflowAmountLabel.Text = amount.Label;
        WorkflowAmount.Text = amount.Value;
        if (!busy) BusyProgress.Visibility = Visibility.Collapsed;
    }

    private CashierDestination CurrentDestination() => CheckoutTab.IsSelected ? CashierDestination.Checkout
        : PaymentTab.IsSelected ? CashierDestination.Payment : ShiftCashTab.IsSelected ? CashierDestination.ShiftCash
        : CashReviewTab.IsSelected ? CashierDestination.CashReview : ReceiptsTab.IsSelected ? CashierDestination.Receipts
        : TerminalTab.IsSelected ? CashierDestination.Terminal : CashierDestination.None;

    private void WorkflowNext_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked || reauthenticationRequired) return;
        var destination = nextDestination;
        HideTouchInput();
        TabItem? target = destination switch {
            CashierDestination.Checkout => CheckoutTab, CashierDestination.Payment => PaymentTab,
            CashierDestination.ShiftCash => ShiftCashTab, CashierDestination.Terminal => TerminalTab,
            CashierDestination.CashReview => CashReviewTab, CashierDestination.Receipts => ReceiptsTab, _ => null
        };
        if (target is not null) target.IsSelected = true;
    }

    private void BrowseOrders_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked || reauthenticationRequired) return;
        HideTouchInput(); OrdersTab.IsSelected = true;
    }

    private void ClearMenuSearch_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked || reauthenticationRequired) return;
        MenuSearchTextBox.Clear(); StationComboBox.SelectedIndex = 0; HideTouchInput();
    }
}
