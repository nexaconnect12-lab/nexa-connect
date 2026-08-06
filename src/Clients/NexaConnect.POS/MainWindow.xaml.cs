using System.Windows;

namespace NexaConnect.POS;

public partial class MainWindow : Window
{
    private readonly PosAuthentication _authentication;
    private readonly PosApiClient _api;
    private readonly LocalPosStore _localStore;
    private readonly PosClientConfiguration _configuration;
    private LocalShiftState? _activeShift;

    public MainWindow(
        PosAuthentication authentication,
        PosApiClient api,
        LocalPosStore localStore,
        PosClientConfiguration configuration)
    {
        InitializeComponent();
        _authentication = authentication;
        _api = api;
        _localStore = localStore;
        _configuration = configuration;
        _activeShift = _localStore.LoadActiveShift();
        _authentication.StatusChanged += OnStatusChanged;
        StatusText.Text = "Ready to sign in.";
        UpdateOperationalState();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Opening secure sign-in…";
            await _authentication.SignInAsync();
            StatusText.Text = "Signed in. POS session is ready.";
            UpdateOperationalState();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Sign-in was cancelled.";
        }
        catch (Exception)
        {
            StatusText.Text = "Sign-in failed. Check the identity service and try again.";
        }
    }

    private async void OpenShift_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ValidateTerminalConfiguration();
            SetBusy("Opening shift…");
            PosShift shift = await _api.OpenShiftAsync(
                _authentication.CurrentToken!,
                _configuration.BranchId,
                _configuration.StoreId,
                _configuration.TerminalId,
                ShiftNumberTextBox.Text.Trim());
            _activeShift = new LocalShiftState(shift.ShiftId, ShiftNumberTextBox.Text.Trim(), DateTimeOffset.UtcNow);
            _localStore.SaveActiveShift(_activeShift);
            StatusText.Text = $"Shift {_activeShift.ShiftNumber} is open.";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception is PosApiException apiException
                ? apiException.Message
                : "Shift could not be opened. Check the POS service and configuration.";
        }
        finally
        {
            UpdateOperationalState();
        }
    }

    private async void CloseShift_Click(object sender, RoutedEventArgs e)
    {
        if (_activeShift is null || _authentication.CurrentToken is null)
        {
            return;
        }

        try
        {
            SetBusy("Closing shift…");
            await _api.CloseShiftAsync(_authentication.CurrentToken, _activeShift.ShiftId);
            string shiftNumber = _activeShift.ShiftNumber;
            _activeShift = null;
            _localStore.ClearActiveShift();
            StatusText.Text = $"Shift {shiftNumber} is closed.";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception is PosApiException apiException
                ? apiException.Message
                : "Shift could not be closed. Keep the terminal online and try again.";
        }
        finally
        {
            UpdateOperationalState();
        }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        if (_activeShift is not null)
        {
            StatusText.Text = "Close the active shift before signing out.";
            return;
        }

        _authentication.SignOut();
        StatusText.Text = "Signed out. Stored credentials were cleared.";
        UpdateOperationalState();
    }

    private void ValidateTerminalConfiguration()
    {
        if (_configuration.BranchId == Guid.Empty ||
            _configuration.StoreId == Guid.Empty ||
            _configuration.TerminalId == Guid.Empty)
        {
            throw new InvalidOperationException("Configure the POS branch, store, and terminal identifiers first.");
        }

        if (string.IsNullOrWhiteSpace(ShiftNumberTextBox.Text))
        {
            throw new InvalidOperationException("Enter a shift number first.");
        }
    }

    private void SetBusy(string message)
    {
        StatusText.Text = message;
        SignInButton.IsEnabled = false;
        OpenShiftButton.IsEnabled = false;
        CloseShiftButton.IsEnabled = false;
    }

    private void UpdateOperationalState()
    {
        bool signedIn = _authentication.CurrentToken is not null &&
            _authentication.CurrentToken.ExpiresAtUtc > DateTimeOffset.UtcNow;
        bool hasActiveShift = _activeShift is not null;
        SignInButton.IsEnabled = !signedIn;
        SignOutButton.IsEnabled = signedIn;
        OpenShiftButton.IsEnabled = signedIn && !hasActiveShift;
        CloseShiftButton.IsEnabled = signedIn && hasActiveShift;
        ActiveShiftText.Text = hasActiveShift
            ? $"Active shift: {_activeShift!.ShiftNumber} ({_activeShift.ShiftId:D})"
            : "No active shift on this terminal.";
    }

    private void OnStatusChanged(object? sender, string status) =>
        Dispatcher.Invoke(() => StatusText.Text = status);

    protected override void OnClosed(EventArgs e)
    {
        _authentication.StatusChanged -= OnStatusChanged;
        _api.Dispose();
        base.OnClosed(e);
    }
}
