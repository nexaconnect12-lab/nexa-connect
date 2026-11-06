using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private CancellationTokenSource? manualRefundReview;

    private void ChooseManualRefundOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!IsSignedIn() || busy || displaySafetyBlocked) return;
        OpenPaidOrdersForReview("Review manual payment");
    }

    private async void ReviewManualRefund_Click(object sender, RoutedEventArgs e)
    {
        if (!IsSignedIn() || busy || displaySafetyBlocked
            || OrdersGrid.SelectedItem is not PosBranchOrder { Status: "Paid" } order) return;
        await ReviewManualRefundAsync(order.OrderId);
    }

    private async Task ReviewManualRefundAsync(Guid orderId)
    {
        if (!IsSignedIn() || busy || displaySafetyBlocked || _authentication.CurrentToken is not { } token) return;
        ClearManualRefundReview();
        int generation = receiptSessionGeneration;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        manualRefundReview = deadline;
        SetBusy("Loading original sales receipt…");
        try
        {
            var receipt = await _api.GetReceiptAsync(token, orderId, deadline.Token);
            string? tender = receipt.Tender switch
            {
                "cash" or "cash_manual" => "Cash",
                "promptpay_manual" => "Manual PromptPay",
                _ => null
            };
            ShowManualRefundReview(tender is null
                ? "This receipt is for a different tender. Use Provider refund to review a captured card payment."
                : $"{tender} · Original paid amount {CashierPresentation.Money(receipt.TotalAmount, receipt.Currency)}\n"
                    + $"Receipt {receipt.ReceiptNumber} · Paid {receipt.PaidAtUtc.LocalDateTime:g}");
        }
        catch (Exception error) when (error is HttpRequestException or System.IO.IOException
            or System.IO.InvalidDataException or System.Text.Json.JsonException or OperationCanceledException or PosApiException)
        {
            ShowManualRefundReview("Could not verify the original receipt. Check your branch access and try again.");
        }
        finally
        {
            if (ReferenceEquals(manualRefundReview, deadline)) manualRefundReview = null;
            UpdateOperationalState();
        }

        void ShowManualRefundReview(string message)
        {
            if (!ReferenceEquals(manualRefundReview, deadline) || !OrdersTab.IsSelected
                || !PrintSessionCurrent(generation, token.Subject)) return;
            ReceiptsTab.IsSelected = true;
            ManualRefundSection.IsSelected = true;
            ManualRefundReviewText.Text = message;
            StatusText.Text = "Original payment review finished.";
        }
    }

    private void ReceiptSections_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (viewInitialized && ReferenceEquals(e.Source, ReceiptSections) && !ManualRefundSection.IsSelected)
            ClearManualRefundReview();
    }

    private void ClearManualRefundReview()
    {
        var review = manualRefundReview;
        manualRefundReview = null;
        review?.Cancel();
        ManualRefundReviewText.Text = "";
    }
}
