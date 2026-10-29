using System.Windows;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly CustomerDisplayState customerState = new();
    private PosDisplayCoordinator? displays;
    private bool displaySafetyBlocked;
    private readonly IPosDisplayPlatform displayPlatform = new WindowsDisplays();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        displays = new(this, () => {
            displaySafetyBlocked = true;
            OperationalSurface.Visibility = Visibility.Hidden;
            SessionLockOverlay.Visibility = Visibility.Collapsed;
            DisplaySafetyOverlay.Visibility = Visibility.Visible;
            // The shared lock retains original authentication and financial identities.
            LockSession("Display assignment changed. Stop customer use and restart POS on the staff screen to review display roles.");
        }, displayPlatform);
        RefreshDisplays_Click(this, new RoutedEventArgs());
        InitializeGallery();
        Loaded+=(_,_)=>{RenderGallery();RenderLockPrompt(true);};
    }

    private void RefreshDisplays_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var monitors = displayPlatform.Read();
            RecordDisplayDiscovery(monitors.Count);
            StaffDisplayPicker.ItemsSource = monitors;
            CustomerDisplayPicker.ItemsSource = monitors;
            StaffDisplayPicker.SelectedIndex = CustomerDisplayPicker.SelectedIndex = -1;
            DisplayStatusText.Text = displays?.Active == true
                ? "Customer display active. Refreshing this list does not reassign windows."
                : "Single-screen staff mode. Select two distinct extended-desktop displays to enable the customer view. Assignments reset on restart.";
        }
        catch { RecordDisplayDiscovery(null); DisplayStatusText.Text = "Displays could not be read. Customer display remains unavailable."; }
        RenderTerminalOverview();
    }

    private void ApplyDisplays_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked) return;
        if (StaffDisplayPicker.SelectedItem is not PosMonitor staff || CustomerDisplayPicker.SelectedItem is not PosMonitor customer)
        { DisplayStatusText.Text = "Select the staff and customer displays explicitly."; return; }
        if (!ConfirmStaffAction(
            "Confirm the selected staff screen is private and Windows is using Extend, not Duplicate. The customer screen must not be used to authenticate or operate staff controls.",
            "Assign display roles",true)) return;
        if (busy || !IsSignedIn() || displaySafetyBlocked) return;
        PublishCustomerDisplay();
        try
        {
            DisplayStatusText.Text = displays?.Apply(staff.Device, customer.Device) == true
                ? "Customer display active. Staff content is constrained to the selected staff monitor."
                : "Assignment refused. Use two distinct, non-overlapping extended-desktop displays. Check resolution/DPI and restart if the safety screen is shown.";
        }
        catch { DisplayStatusText.Text = "Display assignment unavailable. Continue on the staff screen."; }
        RenderTerminalOverview();
    }

    private void DisableCustomerDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked) return;
        displays?.Disable();
        DisplayStatusText.Text = "Customer display closed and cleared. Continue in single-screen staff mode.";
        RenderTerminalOverview();
    }

    private void PublishCustomerDisplay()
    {
        // A presentation fault must never escape into a financial callback, undo
        // its durable recovery write, or change whether its network send happens.
        try { displays?.Present(customerState.Visible(IsSignedIn(), reauthenticationRequired || displaySafetyBlocked)); }
        catch { /* The coordinator fails closed; financial recovery stays independent. */ }
    }
    private void CustomerCartChanged()
    {
        customerState.Cart(_configuration.Currency, cart.Select(line => new CustomerDisplayLine(line.Name, line.Quantity, line.UnitPrice)));
        PublishCustomerDisplay();
    }
    private void CustomerOrderReceived(PosOrderResult result)
    {
        customerState.Order(result, CardCheckout ? "card_omise_test" : SelectedTenderMethod());
        PublishCustomerDisplay();
    }
    private void CloseForDisplaySafety_Click(object sender, RoutedEventArgs e) => Close();
}
