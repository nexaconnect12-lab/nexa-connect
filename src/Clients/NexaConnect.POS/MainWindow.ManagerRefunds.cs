using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.Contracts.POS;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private LocalManualRefundApproval? pendingManualRefundApproval;
    private Guid? managerRefundOrder;
    private ManualRefundPreviewV1? managerRefundPreview;
    private sealed record ReadyCashierRow(string Label, ManualRefundCashierV1 Cashier);

    private async void RegisterRefundAvailability_Click(object sender, RoutedEventArgs e)
    {
        if (_activeShift is not { } shift) return;
        await RunAssignedRefundAsync(async (token, ct) =>
        {
            await _api.RegisterManualRefundAvailabilityAsync(token, new(_configuration.TerminalId, shift.ShiftId), ct);
            if (AssignedRefundSessionCurrent(token, ct))
                AssignedRefundStatus.Text = "You are available for manager assignments for five minutes. Cash requires your open drawer.";
        });
    }

    private async void LoadManagerRefund_Click(object sender, RoutedEventArgs e)
    {
        if (managerRefundOrder is not { } order) return;
        await RunAssignedRefundAsync(async (token, ct) =>
        {
            managerRefundPreview = null;
            ManagerRefundCashier.ItemsSource = null;
            ManagedRefundList.ItemsSource = null;
            var history = await _api.ManagedManualRefundsAsync(token, order, ct);
            if (!AssignedRefundSessionCurrent(token, ct)) return;
            ShowManagedRefunds(history);
            var preview = await _api.ManualRefundPreviewAsync(token, order, ct);
            var cashiers = await _api.ManualRefundCashiersAsync(token, order, ct);
            if (!AssignedRefundSessionCurrent(token, ct)) return;
            managerRefundPreview = preview;
            ManagerRefundCashier.ItemsSource = cashiers.Items.Select(row =>
                new ReadyCashierRow($"Terminal {row.TerminalCode} · Shift {row.ShiftNumber}", row)).ToArray();
            ManagerRefundStatus.Text = $"{(preview.Method == "cash" ? "Cash" : "Manual PromptPay")} · Paid {preview.Currency} {preview.PaidAmount:0.00##} · Available {preview.Currency} {preview.AvailableAmount:0.00##}"
                + (cashiers.Items.Count == 0 ? "\nAsk an eligible cashier to mark their shift available, then reload." : "");
        }, manager: true);
    }

    private async void ApproveManagerRefund_Click(object sender, RoutedEventArgs e)
    {
        if (pendingManualRefundApproval is not null || managerRefundPreview is not { } preview
            || ManagerRefundCashier.SelectedItem is not ReadyCashierRow row
            || (ManagerRefundReason.SelectedItem as ComboBoxItem)?.Tag is not string reason) return;
        if (!decimal.TryParse(ManagerRefundAmount.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
            || amount <= 0 || amount > preview.AvailableAmount || decimal.Round(amount, 2) != amount)
        {
            ManagerRefundStatus.Text = "Enter an amount within the available balance, using whole satang.";
            return;
        }
        await RunAssignedRefundAsync(async (token, ct) =>
        {
            var saved = new LocalManualRefundApproval(preview.OrderId, token.Subject!,
                new(Guid.NewGuid(), amount, reason, row.Cashier.Subject, row.Cashier.ShiftId));
            _localStore.SaveManualRefundApproval(saved);
            pendingManualRefundApproval = saved;
            try { await SendManagerRefundApprovalAsync(saved, token, ct); }
            catch (PosApiException error) when (AssignedRefundSessionCurrent(token, ct) && error.StatusCode is 400 or 403 or 404 or 409)
            {
                _localStore.ClearManualRefundApproval();
                pendingManualRefundApproval = null;
                managerRefundPreview = null;
                ManagerRefundCashier.ItemsSource = null;
                ManagerRefundStatus.Text = "Approval was rejected. Reload options and check the current cashier availability and approval limit.";
            }
        }, manager: true);
    }

    private async void VerifyManagerRefund_Click(object sender, RoutedEventArgs e)
    {
        if (pendingManualRefundApproval is not { } saved || saved.ManagerSubject != _authentication.CurrentToken?.Subject) return;
        await RunAssignedRefundAsync(async (token, ct) =>
        {
            var status = await _api.ManualRefundOperationAsync(token, saved.OrderId, saved.Request.OperationId, null, null, ct);
            if (AssignedRefundSessionCurrent(token, ct)) ConfirmManagerRefundApproval(saved, status);
        }, manager: true);
    }

    private async void RetryManagerRefund_Click(object sender, RoutedEventArgs e)
    {
        if (pendingManualRefundApproval is not { } saved || saved.ManagerSubject != _authentication.CurrentToken?.Subject) return;
        await RunAssignedRefundAsync((token, ct) => SendManagerRefundApprovalAsync(saved, token, ct), manager: true);
    }

    private async Task SendManagerRefundApprovalAsync(LocalManualRefundApproval saved, PosTokenSet token, CancellationToken ct)
    {
        var status = await _api.ApproveManualRefundAsync(token, saved.OrderId, saved.Request, ct);
        if (AssignedRefundSessionCurrent(token, ct)) ConfirmManagerRefundApproval(saved, status);
    }

    private void ConfirmManagerRefundApproval(LocalManualRefundApproval saved, ManualRefundStatusV1 status)
    {
        if (status.Amount != saved.Request.Amount || status.Reason != saved.Request.Reason)
            throw new InvalidDataException("Saved approval differs from the original operation.");
        _localStore.ClearManualRefundApproval();
        pendingManualRefundApproval = null;
        managerRefundOrder = saved.OrderId;
        managerRefundPreview = null;
        ManagerRefundCashier.ItemsSource = null;
        ShowManagedRefunds(new([status], false));
        ManagerRefundStatus.Text = $"Original refund status: {status.Status}. Reload approval options for the current balance and history.";
    }

    private async void ReleaseManagerRefund_Click(object sender, RoutedEventArgs e)
    {
        if (ManagedRefundList.SelectedItem is not AssignedRefundRow { Refund: { Status: "approved" } approval }) return;
        await RunAssignedRefundAsync(async (token, ct) =>
        {
            var status = await _api.ReleaseManualRefundAsync(token, approval.OrderId, approval.OperationId, ct);
            EnsureOriginalRefundTerms(approval, status);
            if (!AssignedRefundSessionCurrent(token, ct)) return;
            managerRefundPreview = null;
            ShowManagedRefunds(new([status], false));
            ManagerRefundStatus.Text = "Unused approval released. Reload to see the restored balance.";
        }, manager: true);
    }

    private void ShowManagedRefunds(AssignedManualRefundsV1 history) =>
        ManagedRefundList.ItemsSource = history.Items.Select(item => new AssignedRefundRow(
            $"{item.Status} · {RefundAmount(item)} · {item.ApprovedAtUtc.LocalDateTime:g}", item)).ToArray();

    private void ManagedRefund_Changed(object sender, RoutedEventArgs e)
    {
        if (viewInitialized) RenderManagerRefundControls();
    }

    private void RenderManagerRefundControls()
    {
        bool available = IsSignedIn() && !busy && !displaySafetyBlocked;
        bool ownSaved = pendingManualRefundApproval is { } saved && saved.ManagerSubject == _authentication.CurrentToken?.Subject;
        RegisterRefundAvailabilityButton.IsEnabled = available && _activeShift is not null;
        LoadManagerRefundButton.IsEnabled = available && managerRefundOrder is not null;
        VerifyManagerRefundButton.IsEnabled = RetryManagerRefundButton.IsEnabled = available && ownSaved;
        ApproveManagerRefundButton.IsEnabled = available && pendingManualRefundApproval is null && managerRefundPreview is not null
            && ManagerRefundCashier.SelectedItem is ReadyCashierRow && !string.IsNullOrWhiteSpace(ManagerRefundAmount.Text);
        ReleaseManagerRefundButton.IsEnabled = available
            && ManagedRefundList.SelectedItem is AssignedRefundRow { Refund.Status: "approved" };
    }

    private void ClearManagerRefundView()
    {
        managerRefundOrder = null;
        managerRefundPreview = null;
        ManagerRefundCashier.ItemsSource = null;
        ManagedRefundList.ItemsSource = null;
        ManagerRefundAmount.Clear();
        ManagerRefundStatus.Text = "";
        RenderManagerRefundControls();
    }
}
