using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.Contracts.POS;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private LocalManualRefundReference? pendingManualRefund;
    private ManualRefundStatusV1? selectedManualRefund;
    private CancellationTokenSource? assignedRefundRequest;
    private bool manualRefundVerificationRequired;
    private sealed record AssignedRefundRow(string Label, ManualRefundStatusV1 Refund);

    private async void LoadAssignedRefunds_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked) return;
        ClearAssignedRefundView();
        await RunAssignedRefundAsync(async (token, ct) =>
        {
            var page = await _api.AssignedManualRefundsAsync(token, ct);
            if (!AssignedRefundSessionCurrent(token, ct)) return;
            AssignedRefundList.ItemsSource = page.Items.Select(item => new AssignedRefundRow(
                $"{item.Status} · {RefundTender(item)} · {RefundAmount(item)} · Order {item.OrderId.ToString("N")[..8]}", item)).ToArray();
            AssignedRefundStatus.Text = page.Items.Count == 0
                ? "No outstanding manual refunds are assigned to you."
                : page.MoreAvailable ? "Showing the first 25 assigned refunds. Resolve existing payouts before loading more."
                    : "Select the original approved refund or verify a saved payout.";
        });
    }

    private async void VerifySavedManualRefund_Click(object sender, RoutedEventArgs e)
    {
        if (pendingManualRefund is not { } saved || saved.CashierSubject != _authentication.CurrentToken?.Subject) return;
        await RunAssignedRefundAsync(async (token, ct) =>
        {
            var status = await _api.ManualRefundOperationAsync(token, saved.OrderId, saved.OperationId, null, null, ct);
            if (!AssignedRefundSessionCurrent(token, ct)) return;
            if (status.Status is "approved" or "completed" or "released") ClearSavedManualRefund();
            ApplyAssignedRefundStatus(status);
        });
    }

    private async void StartAssignedRefund_Click(object sender, RoutedEventArgs e)
    {
        if (selectedManualRefund is not { Status: "approved" } selected
            || !CanStartAssignedRefund(selected) || ManualRefundCustody(selected) is not { } custody) return;
        await RunAssignedRefundAsync(async (token, ct) =>
        {
            SaveManualRefundReference(selected, token);
            var status = await _api.ManualRefundOperationAsync(token, selected.OrderId, selected.OperationId, custody, null, ct);
            EnsureOriginalRefundTerms(selected, status);
            if (!AssignedRefundSessionCurrent(token, ct)) return;
            if (status.Status == "completed") ClearSavedManualRefund();
            ApplyAssignedRefundStatus(status, freshStart: status.Status == "started" && !status.Replayed);
        });
    }

    private async void CompleteAssignedRefund_Click(object sender, RoutedEventArgs e)
    {
        if (selectedManualRefund is not { Status: "started" } selected || !CanCompleteAssignedRefund(selected)
            || ManualRefundCustody(selected) is not { } custody) return;
        bool transfer = selected.Method == "promptpay_manual";
        var completion = new CompleteManualRefundV1(custody, transfer, transfer ? ManualRefundTransferReference.Text.Trim() : null);
        await RunAssignedRefundAsync(async (token, ct) =>
        {
            SaveManualRefundReference(selected, token);
            var status = await _api.ManualRefundOperationAsync(token, selected.OrderId, selected.OperationId, null, completion, ct);
            EnsureOriginalRefundTerms(selected, status);
            if (!AssignedRefundSessionCurrent(token, ct)) return;
            ClearSavedManualRefund();
            ApplyAssignedRefundStatus(status);
        });
    }

    private async Task RunAssignedRefundAsync(Func<PosTokenSet, CancellationToken, Task> action, bool manager = false)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked || !ManualRefundSection.IsSelected
            || _authentication.CurrentToken is not { Subject: { Length: > 0 } } token) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);
        assignedRefundRequest = request;
        try
        {
            SetBusy("Checking the original manual refund…");
            await action(token, request.Token);
            if (AssignedRefundSessionCurrent(token, request.Token)) StatusText.Text = "Manual refund check finished.";
        }
        catch (Exception error) when (error is PosApiException or HttpRequestException or IOException or InvalidDataException
            or JsonException or OperationCanceledException or Microsoft.Data.Sqlite.SqliteException
            or System.Security.Cryptography.CryptographicException)
        {
            if (AssignedRefundSessionCurrent(token, request.Token))
            {
                if (manager)
                {
                    ManagerRefundStatus.Text = pendingManualRefundApproval is null
                        ? "Manager refund action unavailable. Refresh the original order and check your access."
                        : "Approval outcome needs verification. Use Verify saved approval or retry that same request.";
                    StatusText.Text = ManagerRefundStatus.Text;
                }
                else
                {
                    ShowAssignedRefund(null);
                    AssignedRefundStatus.Text = pendingManualRefund is null
                    ? "Could not load the refund. Check branch access and try again."
                    : "Refund outcome needs verification. Use Verify saved refund before any further action.";
                    StatusText.Text = AssignedRefundStatus.Text;
                }
            }
        }
        finally
        {
            if (ReferenceEquals(assignedRefundRequest, request)) assignedRefundRequest = null;
            UpdateOperationalState();
        }
    }

    private bool AssignedRefundSessionCurrent(PosTokenSet token, CancellationToken ct) =>
        !ct.IsCancellationRequested && ReceiptsTab.IsSelected && ManualRefundSection.IsSelected
        && !windowCancellation.IsCancellationRequested && IsSignedIn() && !displaySafetyBlocked
        && _authentication.CurrentToken?.Subject == token.Subject;

    private void SaveManualRefundReference(ManualRefundStatusV1 refund, PosTokenSet token)
    {
        var reference = new LocalManualRefundReference(refund.OrderId, refund.OperationId, token.Subject!);
        _localStore.SaveManualRefund(reference);
        pendingManualRefund = reference;
        manualRefundVerificationRequired = true;
    }

    private void ClearSavedManualRefund()
    {
        _localStore.ClearManualRefund();
        pendingManualRefund = null;
    }

    private void AssignedRefund_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (viewInitialized) ShowAssignedRefund((AssignedRefundList.SelectedItem as AssignedRefundRow)?.Refund);
    }

    private void ManualRefundEvidence_Changed(object sender, RoutedEventArgs e)
    {
        if (viewInitialized) RenderAssignedRefundControls();
    }

    private void ApplyAssignedRefundStatus(ManualRefundStatusV1 refund, bool freshStart = false)
    {
        manualRefundVerificationRequired = false;
        // Discard the pre-command queue so an older row cannot restore stale controls.
        AssignedRefundList.ItemsSource = null;
        ShowAssignedRefund(refund, freshStart);
    }

    private void ShowAssignedRefund(ManualRefundStatusV1? refund, bool freshStart = false)
    {
        selectedManualRefund = refund;
        ManualRefundPayoutConfirmed.IsChecked = false;
        ManualRefundTransferReference.Clear();
        AssignedRefundStatus.Text = refund is null ? "" :
            $"Order {refund.OrderId:D}\n{RefundTender(refund)} · {RefundAmount(refund)} · {refund.Reason.Replace('_', ' ')}\n"
            + (refund.Status switch
            {
                "approved" when !WholeSatang(refund) => "This approval includes a fraction of one satang. Ask a manager to review the amount.",
                "approved" => "Review the original order and select Start approved payout before returning funds.",
                "started" when freshStart => "Payout started. Return the approved amount once, then record completion.",
                "started" => "A payout was already started. Record completion only if the amount was returned. If unsure, ask a manager to reconcile.",
                "released" => "The manager released this unused approval. Do not return funds.",
                _ => "Refund completed. Do not return funds again."
            });
        RenderAssignedRefundControls();
    }

    private void ClearAssignedRefundView()
    {
        var request = assignedRefundRequest;
        assignedRefundRequest = null;
        request?.Cancel();
        manualRefundVerificationRequired = pendingManualRefund is not null;
        AssignedRefundList.ItemsSource = null;
        ShowAssignedRefund(null);
    }

    private void RenderAssignedRefundControls()
    {
        bool available = IsSignedIn() && !busy && !displaySafetyBlocked;
        VerifySavedManualRefundButton.IsEnabled = available && pendingManualRefund is { } saved
            && saved.CashierSubject == _authentication.CurrentToken?.Subject;
        StartAssignedRefundButton.IsEnabled = available && selectedManualRefund is { Status: "approved" } approval
            && CanStartAssignedRefund(approval);
        AssignedRefundCompletion.Visibility = selectedManualRefund?.Status == "started" ? Visibility.Visible : Visibility.Collapsed;
        RefundTransferProof.Visibility = selectedManualRefund?.Method == "promptpay_manual" ? Visibility.Visible : Visibility.Collapsed;
        CompleteAssignedRefundButton.IsEnabled = available && selectedManualRefund is { Status: "started" } payout
            && CanCompleteAssignedRefund(payout);
    }

    private bool CanStartAssignedRefund(ManualRefundStatusV1 refund) => pendingManualRefund is null
        && pendingCheckout is null && pendingOrder is null && pendingRefund is null && cart.Count == 0
        && WholeSatang(refund) && ManualRefundCustody(refund) is not null;

    private bool CanCompleteAssignedRefund(ManualRefundStatusV1 refund) => ManualRefundCustody(refund) is not null
        && !manualRefundVerificationRequired
        && (pendingManualRefund is null || pendingManualRefund.OrderId == refund.OrderId && pendingManualRefund.OperationId == refund.OperationId
            && pendingManualRefund.CashierSubject == _authentication.CurrentToken?.Subject)
        && ManualRefundPayoutConfirmed.IsChecked == true
        && (refund.Method == "cash" || ManualRefundTransferReference.Text.Trim() is { Length: > 0 and <= 64 } reference
            && !reference.Any(char.IsControl));

    private ManualRefundCustodyV1? ManualRefundCustody(ManualRefundStatusV1 refund) =>
        _activeShift is not { } shift || refund.Method == "cash" && cashSessionId is null
            ? null : new(_configuration.TerminalId, shift.ShiftId, refund.Method == "cash" ? cashSessionId : null);

    private static string RefundTender(ManualRefundStatusV1 refund) => refund.Method == "cash" ? "Cash" : "Manual PromptPay";
    private static bool WholeSatang(ManualRefundStatusV1 refund) => decimal.Round(refund.Amount, 2) == refund.Amount;
    private static string RefundAmount(ManualRefundStatusV1 refund) =>
        $"{refund.Currency} {refund.Amount.ToString(WholeSatang(refund) ? "N2" : "N4")}";

    private static void EnsureOriginalRefundTerms(ManualRefundStatusV1 original, ManualRefundStatusV1 response)
    {
        if (response.Amount != original.Amount || response.Currency != original.Currency || response.Method != original.Method
            || response.Reason != original.Reason || response.ApprovedAtUtc.UtcTicks / 10 != original.ApprovedAtUtc.UtcTicks / 10
            || original.StartedAtUtc is { } started && response.StartedAtUtc?.UtcTicks / 10 != started.UtcTicks / 10)
            throw new InvalidDataException("The response changed the original approved refund terms.");
    }
}
