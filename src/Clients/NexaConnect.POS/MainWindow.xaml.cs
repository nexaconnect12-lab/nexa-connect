using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace NexaConnect.POS;

public partial class MainWindow : Window
{
    private readonly PosAuthentication _authentication;
    private readonly PosApiClient _api;
    private readonly LocalPosStore _localStore;
    private readonly PosClientConfiguration _configuration;
    private LocalShiftState? _activeShift;
    private readonly ObservableCollection<PosMenuItem> menu = new();
    private readonly ObservableCollection<CartLine> cart = new();
    private readonly LocalOutboxStore outbox = new();
    private Guid? cashSessionId;

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
        cashSessionId = _localStore.LoadCashSession()?.CashSessionId;
        MenuList.ItemsSource = menu;
        CartList.ItemsSource = cart;
        _authentication.StatusChanged += OnStatusChanged;
        StatusText.Text = "Ready to sign in.";
        UpdateOperationalState();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
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
        catch (Exception exception)
        {
            StatusText.Text = exception is HttpRequestException
                ? "Sign-in failed: Keycloak is unreachable. Check that http://localhost:8080 is running."
                : $"Sign-in failed: {exception.Message}";
        }
        finally
        {
            UpdateOperationalState();
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
                : exception is InvalidOperationException configurationException
                    ? configurationException.Message
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

    private async void LoadMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null) return;
        try { SetBusy("Loading menu…"); menu.Clear(); foreach (var item in await _api.GetMenuAsync(_authentication.CurrentToken, _configuration.BranchId)) menu.Add(item); StatusText.Text = $"Loaded {menu.Count} menu items."; }
        catch (Exception exception) { StatusText.Text = exception is PosApiException api ? api.Message : "Menu could not be loaded. Check the Catalog service."; }
        finally { UpdateOperationalState(); }
    }

    private async void OpenCash_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null || _activeShift is null) return;
        if (!decimal.TryParse(OpeningCashTextBox.Text, out var amount) || amount < 0) { StatusText.Text = "Enter a valid opening cash amount."; return; }
        try { SetBusy("Opening cash session…"); var result = await _api.OpenCashSessionAsync(_authentication.CurrentToken, _activeShift.ShiftId, _configuration.StoreId, _configuration.Currency, amount); cashSessionId = result.CashSessionId; _localStore.SaveCashSession(new LocalCashSessionState(result.CashSessionId, _activeShift.ShiftId, DateTimeOffset.UtcNow)); StatusText.Text = "Cash session is open."; }
        catch (Exception exception) { StatusText.Text = exception is PosApiException api ? api.Message : "Cash session could not be opened."; }
        finally { UpdateOperationalState(); }
    }

    private async void CloseCash_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null || cashSessionId is null) return;
        if (HasQueuedCashMovements(cashSessionId.Value))
        {
            StatusText.Text = "Replay or resolve all queued movements for this cash session before closing it.";
            return;
        }
        if (!decimal.TryParse(ClosingCashTextBox.Text, out var amount) || amount < 0) { StatusText.Text = "Enter a valid closing cash amount."; return; }
        try { SetBusy("Closing cash session…"); await _api.CloseCashSessionAsync(_authentication.CurrentToken, cashSessionId.Value, amount); cashSessionId = null; _localStore.ClearCashSession(); StatusText.Text = "Cash session is closed."; }
        catch (Exception exception) { StatusText.Text = exception is PosApiException api ? api.Message : "Cash session could not be closed."; }
        finally { UpdateOperationalState(); }
    }

    private async void RecordMovement_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null || cashSessionId is null) return;
        if (_configuration.TerminalId == Guid.Empty) { StatusText.Text = "Configure a valid terminal identifier first."; return; }
        if (!decimal.TryParse(MovementAmountTextBox.Text, out var amount) || amount <= 0) { StatusText.Text = "Enter a positive movement amount."; return; }
        var type = (MovementTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "sale";
        var reason = MovementReasonTextBox.Text.Trim();
        LocalOutboxOperation operation;
        try
        {
            operation = outbox.Enqueue(
                "cash-movement",
                $"api/pos/v1/cash-sessions/{cashSessionId.Value:D}/movements",
                "POST",
                JsonSerializer.Serialize(new { movementType = type, amount, reasonCode = reason }),
                _configuration.TerminalId);
        }
        catch
        {
            StatusText.Text = "Cash movement was not sent because it could not be saved to the offline queue.";
            UpdateOperationalState();
            return;
        }

        try
        {
            UpdateOperationalState();
            SetBusy("Recording cash movement…");
            await _api.RecordCashMovementAsync(
                _authentication.CurrentToken,
                cashSessionId.Value,
                _configuration.TerminalId,
                operation.OperationId,
                type,
                amount,
                reason);
            outbox.Remove(operation.OperationId);
            StatusText.Text = "Cash movement recorded.";
        }
        catch (Exception exception)
        {
            if (exception is PosApiException { StatusCode: 400 or 403 or 409 } api)
            {
                outbox.MarkTerminalFailure(operation.OperationId, api.StatusCode);
                StatusText.Text = $"{api.Message} Movement retained as rejected for operator review.";
            }
            else
            {
                StatusText.Text = exception is PosApiException transientApi
                    ? $"{transientApi.Message} Movement remains queued for replay."
                    : "Cash movement remains queued for replay.";
            }
        }
        finally { UpdateOperationalState(); }
    }

    private async void EnrollTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null) return;
        try { SetBusy("Enrolling terminal…"); await _api.EnrollTerminalAsync(_authentication.CurrentToken, _configuration, TerminalCodeTextBox.Text.Trim(), "pos"); StatusText.Text = "Terminal enrolled."; }
        catch (Exception exception) { StatusText.Text = exception is PosApiException api ? api.Message : "Terminal enrollment failed."; }
        finally { UpdateOperationalState(); }
    }

    private async void ReplayOutbox_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null) return;
        try { SetBusy("Replaying offline operations…"); var replayed = await new PosOutboxReplayer(_configuration, outbox).ReplayAsync(_authentication.CurrentToken); StatusText.Text = $"Replayed {replayed} offline operation(s)."; }
        catch (Exception exception) { StatusText.Text = exception is PosApiException api ? api.Message : "Offline replay stopped; operations remain queued."; }
        finally { UpdateOperationalState(); }
    }

    private void RetryRejectedOutbox_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null
            || _authentication.CurrentToken.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            StatusText.Text = "Sign in before retrying rejected offline operations.";
            UpdateOperationalState();
            return;
        }
        int retried = outbox.RetryTerminalFailures();
        StatusText.Text = retried == 0
            ? "There are no rejected offline operations to retry."
            : $"Returned {retried} rejected offline operation(s) to the replay queue.";
        UpdateOperationalState();
    }

    private void MenuList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (MenuList.SelectedItem is not PosMenuItem item || !item.Available) return;
        var existing = cart.FirstOrDefault(line => line.ProductId == item.ProductId);
        if (existing is null) cart.Add(new CartLine(item)); else existing.Quantity++;
        CartList.Items.Refresh(); UpdateCartTotal();
    }

    private void RemoveCart_Click(object sender, RoutedEventArgs e)
    {
        if (CartList.SelectedItem is CartLine line) { if (line.Quantity > 1) line.Quantity--; else cart.Remove(line); CartList.Items.Refresh(); UpdateCartTotal(); }
    }

    private async void PlaceOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null || cart.Count == 0) return;
        try { SetBusy("Placing order…"); var result = await _api.PlaceOrderAsync(_authentication.CurrentToken, _configuration, cart.Select(line => (line.ProductId, line.Quantity)).ToArray()); cart.Clear(); UpdateCartTotal(); StatusText.Text = $"Order {result.OrderId:D} completed with status {result.Status}."; }
        catch (Exception exception) { StatusText.Text = exception is PosApiException api ? api.Message : "Order could not be placed. The cart was kept for retry."; }
        finally { UpdateOperationalState(); }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        if (_activeShift is not null || cashSessionId is not null)
        {
            StatusText.Text = "Close the active shift and cash session before signing out.";
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
        CloseCashButton.IsEnabled = false;
        RecordMovementButton.IsEnabled = false;
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
        LoadMenuButton.IsEnabled = signedIn && hasActiveShift;
        PlaceOrderButton.IsEnabled = signedIn && hasActiveShift && cashSessionId is not null && cart.Count > 0;
        OpenCashButton.IsEnabled = signedIn && hasActiveShift && cashSessionId is null;
        CloseCashButton.IsEnabled = signedIn && cashSessionId is not null
            && !HasQueuedCashMovements(cashSessionId.Value);
        RecordMovementButton.IsEnabled = signedIn && cashSessionId is not null;
        EnrollTerminalButton.IsEnabled = signedIn;
        IReadOnlyList<LocalOutboxOperation> operations = outbox.Load();
        int rejected = operations.Count(operation => operation.TerminalFailureStatusCode is not null);
        ReplayOutboxButton.IsEnabled = signedIn && operations.Count > rejected;
        RetryRejectedOutboxButton.IsEnabled = signedIn && rejected > 0;
        OutboxStatusText.Text = $"Offline queue: {operations.Count - rejected} pending, {rejected} rejected";
        SessionText.Text = signedIn ? (hasActiveShift ? "Signed in · Shift open" : "Signed in · Open a shift") : "Signed out";
        ActiveShiftText.Text = hasActiveShift
            ? $"Active shift: {_activeShift!.ShiftNumber} ({_activeShift.ShiftId:D})"
            : "No active shift on this terminal.";
    }

    private void OnStatusChanged(object? sender, string status) =>
        Dispatcher.Invoke(() => StatusText.Text = status);

    private bool HasQueuedCashMovements(Guid sessionId)
    {
        string path = $"api/pos/v1/cash-sessions/{sessionId:D}/movements";
        return outbox.Load().Any(operation =>
            string.Equals(operation.OperationType, "cash-movement", StringComparison.Ordinal)
            && string.Equals(operation.RelativeUri, path, StringComparison.OrdinalIgnoreCase));
    }

    protected override void OnClosed(EventArgs e)
    {
        _authentication.StatusChanged -= OnStatusChanged;
        _api.Dispose();
        base.OnClosed(e);
    }

    private void UpdateCartTotal() => TotalText.Text = cart.Sum(line => line.LineTotal).ToString("C2");

    private sealed class CartLine(PosMenuItem item)
    {
        public Guid ProductId { get; } = item.ProductId;
        public string Name { get; } = item.Name;
        public decimal UnitPrice { get; } = item.UnitPrice;
        public int Quantity { get; set; } = 1;
        public decimal LineTotal => UnitPrice * Quantity;
    }
}
