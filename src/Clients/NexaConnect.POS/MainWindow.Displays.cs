using System.Windows;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly CustomerDisplayState customerState = new();
    private PosDisplayCoordinator? displays;
    private bool displaySafetyBlocked;
    private readonly IPosDisplayPlatform displayPlatform = new WindowsDisplays();
    internal bool UseStaffFullscreen { get; init; } = true;

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
        var saved = UseStaffFullscreen ? inputPreferences.LoadDisplayRoles() : null;
        bool restored = false;
        if (saved is not null)
        {
            // At startup the public display is allowed only a fresh welcome snapshot.
            displays.Present(CustomerDisplaySnapshot.Welcome with { ShopName = _configuration.ShopName });
            try { restored = displays.Restore(saved); }
            catch (InvalidOperationException) { }
        }
        if (UseStaffFullscreen && !restored) displays.InitializeStaffFullscreen();
        RefreshDisplays_Click(this, new RoutedEventArgs());
        if (saved is not null && !displaySafetyBlocked)
            DisplayStatusText.Text = restored
                ? "Saved staff and customer displays restored."
                : "Saved displays no longer match this setup. Confirm the staff screen and apply the display roles again.";
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
            var roles = displays?.ActiveRoles;
            StaffDisplayPicker.SelectedItem = monitors.SingleOrDefault(monitor => monitor.Device == roles?.Staff);
            CustomerDisplayPicker.SelectedItem = monitors.SingleOrDefault(monitor => monitor.Device == roles?.Customer);
            DisplayStatusText.Text = displays?.Active == true
                ? "Customer display active. Refreshing this list does not reassign windows."
                : "Single-screen staff mode. Select two distinct extended-desktop displays to enable the customer view.";
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
        var previous = inputPreferences.LoadDisplayRoles();
        if (previous is not null && (previous.StaffDevice != staff.Device || previous.CustomerDevice != customer.Device)
            && !SaveDisplayRoles(null))
        {
            DisplayStatusText.Text = "The previous display roles could not be cleared. Check local preferences before changing roles.";
            return;
        }
        PublishCustomerDisplay();
        try
        {
            if (displays is not null && displays.Apply(staff, customer, out var saved))
            {
                DisplayStatusText.Text = SaveDisplayRoles(saved)
                    ? saved is null
                        ? "Customer display active for this launch. Windows could not identify both displays for safe restore."
                        : "Customer display active. Display roles are saved on this terminal."
                    : "Customer display active, but the local display settings could not be saved.";
            }
            else
                DisplayStatusText.Text = "Assignment refused. Refresh displays and select two distinct extended-desktop displays.";
        }
        catch { DisplayStatusText.Text = "Display assignment unavailable. Continue on the staff screen."; }
        RenderTerminalOverview();
    }

    private void DisableCustomerDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked) return;
        displays?.Disable();
        DisplayStatusText.Text = SaveDisplayRoles(null)
            ? "Customer display closed. Single-screen mode will be used next time."
            : "Customer display closed for this launch. The saved assignment could not be cleared.";
        RenderTerminalOverview();
    }

    private bool SaveDisplayRoles(PosDisplayRoles? roles)
    {
        try { inputPreferences.SaveDisplayRoles(roles); return true; }
        catch (Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
        { return false; }
    }

    private void PublishCustomerDisplay()
    {
        // A presentation fault must never escape into a financial callback, undo
        // its durable recovery write, or change whether its network send happens.
        try { displays?.Present(customerState.Visible(IsSignedIn(), reauthenticationRequired || displaySafetyBlocked) with { ShopName = _configuration.ShopName }); }
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
    private void ClosePos_Click(object sender, RoutedEventArgs e) => Close();
}
