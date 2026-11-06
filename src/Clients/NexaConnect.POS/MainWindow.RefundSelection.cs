using System.Windows;
using System.Windows.Controls;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private PosRefundPayment? refundPayment;
    private CancellationTokenSource? refundSelectionCancellation;
    private int refundSelectionGeneration;
    private bool settingRefundSelection;

    private void ClearRefundPaymentSelection()
    {
        refundSelectionGeneration++; refundSelectionCancellation?.Cancel(); refundPayment = null;
        RefundSelectionConfirmed.IsChecked = false;
        RefundSelectedPaymentText.Text = "Choose a paid order to review its original card payment.";
        if (pendingRefund is null) { RefundPaymentIntentIdTextBox.Clear(); RefundAmountTextBox.Clear(); }
    }
    private void ChooseRefundPayment_Click(object sender, RoutedEventArgs e)
    {
        if (!IsSignedIn() || busy || pendingRefund is not null || displaySafetyBlocked) return;
        ClearRefundPaymentSelection();
        OrdersChannelFilter.SelectedIndex = 0; OrdersStateFilter.SelectedIndex = 2;
        OrdersTab.IsSelected = true;
        OrdersNoticeText.Text = "Select the original paid order, then choose Review for refund. No refund is sent by selection.";
    }
    private async void SelectRefundPayment_Click(object sender, RoutedEventArgs e)
    {
        if (!IsSignedIn() || busy || pendingRefund is not null || OrdersGrid.SelectedItem is not PosBranchOrder { Status: "Paid" } row) return;
        await LoadRefundSelectionAsync(row.OrderId, true);
    }
    private async void LookupRefundPayment_Click(object sender, RoutedEventArgs e)
    {
        if (!Guid.TryParse(RefundPaymentIntentIdTextBox.Text, out var id) || id == Guid.Empty)
        { RefundStatusText.Text = "Choose a paid order, or enter a valid payment ID for lookup."; return; }
        await LoadRefundSelectionAsync(id, false);
    }
    private async Task LoadRefundSelectionAsync(Guid id, bool byOrder)
    {
        if (!IsSignedIn() || busy || pendingRefund is not null || _authentication.CurrentToken is not {} token) return;
        ClearRefundPaymentSelection(); int generation = refundSelectionGeneration, session = receiptSessionGeneration;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);
        refundSelectionCancellation = cancellation; SetBusy("Loading original payment for review…");
        try
        {
            var payment = await _api.FindRefundPaymentAsync(token, id, byOrder, cancellation.Token);
            if (generation != refundSelectionGeneration || !PrintSessionCurrent(session, token.Subject)) return;
            ReceiptsTab.IsSelected = true;
            ProviderRefundSection.IsSelected = true;
            settingRefundSelection = true;
            try
            {
                refundPayment = payment;
                RefundPaymentIntentIdTextBox.Text = payment.PaymentIntentId.ToString("D");
                RefundAmountTextBox.Clear(); RefundSelectionConfirmed.IsChecked = false;
                RefundSelectedPaymentText.Text = payment.Label + "\n" + payment.ReceiptNumber + "\n" + payment.Items
                    + $"\nAvailable to refund now: {payment.Currency} {payment.AvailableAmount:N2}. Rechecked before submission.";
                RefundStatusText.Text = payment.AvailableAmount > 0 ? "Enter the refund amount and reason, then confirm your review."
                    : "No amount is currently available; existing refunds may be completed or pending verification.";
            }
            finally { settingRefundSelection = false; }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch
        {
            if (generation == refundSelectionGeneration && PrintSessionCurrent(session, token.Subject))
            {
                ReceiptsTab.IsSelected = true;
                ProviderRefundSection.IsSelected = true;
                RefundStatusText.Text = "Payment cannot be selected. Check access and the original captured card payment. Cash/manual PromptPay and unverified payments are not supported here.";
            }
        }
        finally
        {
            if (ReferenceEquals(refundSelectionCancellation, cancellation)) refundSelectionCancellation = null;
            if (!windowCancellation.IsCancellationRequested) UpdateOperationalState();
        }
    }
    private void RefundInputs_Changed(object sender, RoutedEventArgs e)
    {
        if (!viewInitialized || settingRefundSelection) return;
        RefundSelectionConfirmed.IsChecked = false;
        if (ReferenceEquals(sender, RefundPaymentIntentIdTextBox) && refundPayment is {} selected
            && RefundPaymentIntentIdTextBox.Text != selected.PaymentIntentId.ToString("D"))
        { refundPayment = null; RefundSelectedPaymentText.Text = "Payment ID changed. Load and review the original payment again."; }
        RenderRefundSelectionControls();
    }
    private void RefundConfirmation_Changed(object sender, RoutedEventArgs e)
    { if (viewInitialized) RenderRefundSelectionControls(); }
    private void RenderRefundSelectionControls()
    {
        bool editable = IsSignedIn() && !busy && !displaySafetyBlocked && !reauthenticationRequired && pendingRefund is null;
        ChooseRefundPaymentButton.IsEnabled = LookupRefundPaymentButton.IsEnabled = editable;
        RefundPaymentIntentIdTextBox.IsReadOnly = !editable;
        RefundAmountTextBox.IsEnabled = RefundReasonComboBox.IsEnabled = editable;
        RefundSelectionConfirmed.IsEnabled = editable && refundPayment is not null;
        RefundButton.IsEnabled = editable && refundPayment is {} payment && RefundSelectionConfirmed.IsChecked == true
            && decimal.TryParse(RefundAmountTextBox.Text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var amount) && amount > 0 && amount <= payment.AvailableAmount
            && decimal.Round(amount, 4) == amount;
        if (pendingRefund is {} pending)
        {
            settingRefundSelection = true;
            try
            {
                RefundSelectedPaymentText.Text = $"Saved refund · {pending.Currency} {pending.Amount:N2}. Verify the original operation before another refund.";
                RefundAmountTextBox.Text = pending.Amount.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                foreach (ComboBoxItem item in RefundReasonComboBox.Items) if (Equals(item.Tag, pending.ReasonCode)) RefundReasonComboBox.SelectedItem = item;
            }
            finally { settingRefundSelection = false; }
        }
    }
}
