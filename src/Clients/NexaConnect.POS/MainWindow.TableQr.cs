using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private DateTimeOffset? tableQrVisibleUntil;
    private PosTableQrFiles TableQrFiles => new(Path.Combine(AppContext.BaseDirectory, "table-qr"), _configuration.BranchId);

    private bool CanShowTableQr() => IsSignedIn() && !displaySafetyBlocked && QrRequestsTab.IsSelected
        && displays?.Active == true && cart.Count == 0 && pendingCheckout is null && pendingOrder is null;

    private void UpdateTableQrControls()
    {
        if (!viewInitialized) return;
        bool signedIn = IsSignedIn() && !displaySafetyBlocked;
        var now = DateTimeOffset.UtcNow;
        if (tableQrVisibleUntil is { } until && (until <= now || !CanShowTableQr()))
            ClearTableQr();
        LoadTableQrButton.IsEnabled = TableQrPicker.IsEnabled = signedIn && !busy;
        ShowTableQrButton.IsEnabled = !busy && CanShowTableQr()
            && TableQrPicker.SelectedItem is PosTableQrChoice choice && choice.ExpiresAtUtc > now;
        HideTableQrButton.IsEnabled = tableQrVisibleUntil is not null;
    }

    private void LoadTableQr_Click(object sender, RoutedEventArgs e)
    {
        if (!LoadTableQrButton.IsEnabled) return;
        ClearTableQr();
        try
        {
            var choices = TableQrFiles.List(DateTimeOffset.UtcNow);
            TableQrPicker.ItemsSource = choices;
            TableQrStatus.Text = choices.Count == 0
                ? "No current table QR files are available for this branch."
                : "Select a table, then show its code while the customer display is idle.";
        }
        catch (Exception e2) when (e2 is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            TableQrPicker.ItemsSource = null;
            TableQrStatus.Text = "Table QR files could not be read. Check the terminal’s table QR folder.";
        }
        UpdateTableQrControls();
    }

    private void TableQrSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!viewInitialized) return;
        ClearTableQr();
        UpdateTableQrControls();
    }

    private void ShowTableQr_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !CanShowTableQr() || TableQrPicker.SelectedItem is not PosTableQrChoice choice) return;
        ClearTableQr();
        var now = DateTimeOffset.UtcNow;
        try
        {
            var image = TableQrFiles.Load(choice, now);
            if (displays?.ShowTableQr(image, choice.TableCode) != true)
            {
                TableQrStatus.Text = "The customer screen is unavailable or busy. Check its display assignment.";
                return;
            }
            var limit = now.AddMinutes(2);
            tableQrVisibleUntil = choice.ExpiresAtUtc < limit ? choice.ExpiresAtUtc : limit;
            TableQrStatus.Text = $"Table {choice.TableCode} QR shown on the customer screen.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or System.Text.Json.JsonException or FormatException or NotSupportedException)
        { TableQrStatus.Text = "The selected table QR is unavailable, expired or invalid. Load the current export."; }
        finally { UpdateTableQrControls(); }
    }

    private void HideTableQr_Click(object sender, RoutedEventArgs e)
    {
        ClearTableQr();
        UpdateTableQrControls();
    }

    private void ClearTableQr()
    {
        if (tableQrVisibleUntil is null) return;
        tableQrVisibleUntil = null;
        displays?.HideTableQr();
        TableQrStatus.Text = "Table QR hidden.";
    }
}
