using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Input;

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
    private readonly ObservableCollection<PosCashReviewListItem> cashReviews = new();
    private readonly LocalOutboxStore outbox;
    private Guid? cashSessionId;
    private PosCashSessionSummary? cashSummary;
    private bool cashSummaryVerified;
    private PosCashReviewAccess? cashReviewAccess;
    private PosCashReviewDetail? selectedCashReview;
    private string? cashReviewCursor;
    private LocalPendingCashReviewState? pendingCashReviewAttempt;
    private PosOrderResult? pendingOrder;
    private PendingCheckout? pendingCheckout;
    private bool cardTokenRequired;
    private Guid? settlementIdempotencyKey;
    private bool settlementUncertain;
    private bool settlementInFlight;
    private bool viewInitialized;
    private bool busy;
    private bool reauthenticationRequired;
    private string? sessionLockMessage;
    private bool tokenRefreshInFlight;
    private DateTimeOffset lastOperatorActivityUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset nextTokenRefreshAttemptUtc = DateTimeOffset.MinValue;
    private readonly System.Windows.Threading.DispatcherTimer sessionTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private CancellationTokenSource? signInCancellation;

    public MainWindow(
        PosAuthentication authentication,
        PosApiClient api,
        LocalPosStore localStore,
        LocalOutboxStore outboxStore,
        PosClientConfiguration configuration)
    {
        InitializeComponent();

        _authentication = authentication;
        reauthenticationRequired = authentication.RequiresInteractiveSignIn;
        _api = api;
        _localStore = localStore;
        outbox = outboxStore;
        _configuration = configuration;
        _activeShift = _localStore.LoadActiveShift();
        cashSessionId = _localStore.LoadCashSession()?.CashSessionId;
        pendingCheckout = _localStore.LoadPendingCheckout();
        pendingCheckout?.Validate(configuration);
        pendingCashReviewAttempt = _localStore.LoadPendingCashReview(configuration);
        ConfigureTenderControls();
        LocalPendingSettlementState? savedSettlement = _localStore.LoadPendingSettlement();
        if (savedSettlement is not null && pendingCheckout is not null && savedSettlement.OrderId != pendingCheckout.OrderId)
            throw new InvalidDataException("Pending payment and checkout differ. Preserve recovery files and reconcile.");
        if (savedSettlement is not null)
        {
            pendingOrder = new PosOrderResult(savedSettlement.OrderId, PosOrderStatus.KitchenAccepted,
                savedSettlement.Amount, savedSettlement.Currency);
            settlementIdempotencyKey = savedSettlement.IdempotencyKey;
            settlementUncertain = savedSettlement.OutcomeUncertain;
            if (savedSettlement.Method is not null)
                TenderMethodComboBox.SelectedIndex = savedSettlement.Method == "promptpay_manual" ? 1 : 0;
            BankReferenceTextBox.Text = savedSettlement.BankReference ?? string.Empty;
            ReceiptConfirmedCheckBox.IsChecked = savedSettlement.ReceiptConfirmed;
        }
        viewInitialized = true;
        MenuList.ItemsSource = menu;
        System.Windows.Data.CollectionViewSource.GetDefaultView(menu).Filter = value => value is PosMenuItem item && CashierPresentation.MatchesMenu(item.Name, item.PreparationStation, MenuSearchTextBox.Text, StationComboBox.SelectedItem as string);
        StationComboBox.ItemsSource = new[] { "All stations" };
        StationComboBox.SelectedIndex = 0;
        ShiftNumberTextBox.Text = CashierPresentation.NewShiftNumber(DateTimeOffset.Now, Guid.NewGuid());
        CartList.ItemsSource = cart;
        CashReviewList.ItemsSource = cashReviews;
        CashReviewFromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
        CashReviewToDatePicker.SelectedDate = DateTime.Today;
        _authentication.StatusChanged += OnStatusChanged;
        sessionTimer.Tick += SessionTimer_Tick;
        sessionTimer.Start();
        StatusText.Text = pendingCashReviewAttempt is null
            ? "Ready to sign in. Service connectivity is checked when you perform an action."
            : "A cash-review decision needs verification. Sign in and load Cash review to reconcile it.";
        UpdateCartTotal();
        if (pendingOrder is not null) PaymentTab.IsSelected = true;
        else if (pendingCashReviewAttempt is not null) CashReviewTab.IsSelected = true;
        UpdateOperationalState();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        CardTokenBox.Clear();
        cardTokenRequired = false;
        if (signInCancellation is not null)
        {
            signInCancellation.Cancel();
            return;
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        signInCancellation = cancellation;
        SetBusy("Opening secure sign-in…");
        SignInButton.Content = "Cancel sign-in";
        SignInButton.IsEnabled = true;
        try
        {
            StatusText.Text = "Opening secure sign-in…";
            await _authentication.SignInAsync(reauthenticationRequired, cancellation.Token);
            reauthenticationRequired = false;
            sessionLockMessage = null;
            lastOperatorActivityUtc = DateTimeOffset.UtcNow;
            nextTokenRefreshAttemptUtc = DateTimeOffset.MinValue;
            StatusText.Text = "Signed in. POS session is ready.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Sign-in cancelled or timed out. Click Sign in to try again.";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception is HttpRequestException
                ? "Sign-in failed: Keycloak is unreachable. Check that http://localhost:8080 is running."
                : $"Sign-in failed: {exception.Message}";
        }
        finally
        {
            signInCancellation = null;
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
        if (_activeShift is null)
        {
            return;
        }
        if (!IsSignedIn())
        {
            reauthenticationRequired = true;
            StatusText.Text = "Your session expired. Sign in again, then close the saved shift.";
            UpdateOperationalState();
            return;
        }
        if (pendingOrder is not null || pendingCheckout is not null || cashSessionId is not null)
        {
            StatusText.Text = "Close the cash session and resolve pending payment before closing this shift.";
            return;
        }

        try
        {
            SetBusy("Closing shift…");
            await _api.CloseShiftAsync(_authentication.CurrentToken!, _activeShift.ShiftId);
            string shiftNumber = _activeShift.ShiftNumber;
            _activeShift = null;
            _localStore.ClearActiveShift();
            ShiftNumberTextBox.Text = CashierPresentation.NewShiftNumber(DateTimeOffset.Now, Guid.NewGuid());
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
        try { SetBusy("Loading menu…"); menu.Clear(); foreach (var item in await _api.GetMenuAsync(_authentication.CurrentToken, _configuration.BranchId)) menu.Add(item); StationComboBox.ItemsSource = new[] { "All stations" }.Concat(menu.Select(item => item.PreparationStation).Distinct().OrderBy(value => value)).ToArray(); StationComboBox.SelectedIndex = 0; StatusText.Text = $"Loaded {menu.Count} menu items."; }
        catch (Exception exception) { StatusText.Text = exception is PosApiException api ? api.Message : "Menu could not be loaded. Check the Catalog service."; }
        finally { UpdateOperationalState(); }
    }

    private async void OpenCash_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null || _activeShift is null) return;
        if (!decimal.TryParse(OpeningCashTextBox.Text, out var amount) || amount < 0) { StatusText.Text = "Enter a valid opening cash amount."; return; }
        try
        {
            SetBusy("Opening cash session…");
            var result = await _api.OpenCashSessionAsync(_authentication.CurrentToken, _activeShift.ShiftId,
                _configuration.StoreId, _configuration.Currency, amount);
            cashSessionId = result.CashSessionId;
            cashSummary = null;
            cashSummaryVerified = false;
            RenderCashSummary();
            _localStore.SaveCashSession(new LocalCashSessionState(
                result.CashSessionId, _activeShift.ShiftId, DateTimeOffset.UtcNow));
            try
            {
                await RefreshCashSummaryAsync();
                StatusText.Text = "Cash session is open. Review reconciliation before closing.";
            }
            catch
            {
                StatusText.Text = "Cash session is open, but reconciliation could not be refreshed. Keep the session and try Refresh reconciliation.";
            }
        }
        catch (Exception exception) { StatusText.Text = exception is PosApiException api ? api.Message : "Cash session could not be opened."; }
        finally { UpdateOperationalState(); }
    }

    private async void CloseCash_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null || cashSessionId is null) return;
        if (pendingOrder is not null || pendingCheckout is not null)
        {
            StatusText.Text = "Confirm or reconcile the pending cash payment before closing this cash session.";
            return;
        }
        if (HasQueuedCashMovements(cashSessionId.Value))
        {
            StatusText.Text = "Replay or resolve all queued movements for this cash session before closing it.";
            return;
        }
        if (!decimal.TryParse(ClosingCashTextBox.Text, out var amount) || amount < 0) { StatusText.Text = "Enter a valid closing cash amount."; return; }
        try
        {
            SetBusy("Refreshing cash reconciliation…");
            long? reviewedVersion = cashSummary?.ConcurrencyVersion;
            PosCashSessionSummary current = await _api.GetCashSessionSummaryAsync(
                _authentication.CurrentToken, cashSessionId.Value);
            cashSummary = current;
            cashSummaryVerified = true;
            RenderCashSummary();
            if (reviewedVersion is null || reviewedVersion.Value != current.ConcurrencyVersion)
            {
                StatusText.Text = "Cash reconciliation changed or was not reviewed. Check the expected cash and variance, then choose Close cash session again.";
                return;
            }

            SetBusy("Closing cash session…");
            Guid closedSessionId = cashSessionId.Value;
            await _api.CloseCashSessionAsync(_authentication.CurrentToken, closedSessionId, amount,
                current.ConcurrencyVersion);
            try
            {
                cashSummary = await _api.GetCashSessionSummaryAsync(_authentication.CurrentToken, closedSessionId);
                cashSummaryVerified = true;
                RenderCashSummary();
            }
            catch
            {
                cashSummary = current with
                {
                    ActualClosingAmount = amount,
                    VarianceAmount = amount - current.ExpectedClosingAmount,
                    Status = "closed",
                    ClosedAtUtc = DateTimeOffset.UtcNow,
                    ConcurrencyVersion = current.ConcurrencyVersion + 1
                };
                cashSummaryVerified = false;
                RenderCashSummary();
            }
            cashSessionId = null;
            _localStore.ClearCashSession();
            StatusText.Text = cashSummaryVerified
                ? "Cash session is closed. The server-verified final variance is shown in reconciliation."
                : "Cash session is closed, but final reconciliation is not verified. Use Refresh reconciliation to load the authoritative result.";
        }
        catch (PosApiException exception) when (exception.StatusCode == 409)
        {
            try { await RefreshCashSummaryAsync(); }
            catch { }
            StatusText.Text = "Cash reconciliation changed or the session scope no longer matches. Review the refreshed state before closing.";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception is PosApiException api ? api.Message : "Cash session could not be closed.";
        }
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

        bool attemptStarted = false;
        try
        {
            UpdateOperationalState();
            SetBusy("Recording cash movement…");
            outbox.MarkAttempted(operation.OperationId);
            attemptStarted = true;
            await _api.RecordCashMovementAsync(
                _authentication.CurrentToken,
                cashSessionId.Value,
                _configuration.TerminalId,
                operation.OperationId,
                type,
                amount,
                reason);
            outbox.Remove(operation.OperationId);
            try
            {
                await RefreshCashSummaryAsync();
                StatusText.Text = "Cash movement recorded and reconciliation refreshed.";
            }
            catch
            {
                StatusText.Text = "Cash movement was recorded, but reconciliation could not be refreshed. Use Refresh reconciliation before closing.";
            }
        }
        catch (Exception exception)
        {
            if (attemptStarted && exception is PosApiException { StatusCode: 400 or 403 or 409 } api)
            {
                outbox.MarkTerminalFailure(operation.OperationId, api.StatusCode);
                StatusText.Text = $"{api.Message} Movement retained as rejected for operator review.";
            }
            else
            {
                if (attemptStarted) outbox.MarkRetryable(operation.OperationId);
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
        try
        {
            SetBusy("Replaying offline operations…");
            var replayed = await new PosOutboxReplayer(_configuration, outbox).ReplayAsync(_authentication.CurrentToken);
            bool summaryRefreshed = true;
            if (cashSessionId is not null)
            {
                try { await RefreshCashSummaryAsync(); }
                catch { summaryRefreshed = false; }
            }
            StatusText.Text = summaryRefreshed
                ? $"Replayed {replayed} offline operation(s)."
                : $"Replayed {replayed} offline operation(s), but reconciliation refresh failed. Refresh it before closing.";
        }
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

    private bool CanEditCart() => CashierPresentation.CanEditCart(
        _authentication.CurrentToken?.ExpiresAtUtc > DateTimeOffset.UtcNow,
        _activeShift is not null, busy, pendingOrder is not null || pendingCheckout is not null);

    private void AddMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditCart() || sender is not Button { DataContext: PosMenuItem item } || !item.Available) return;
        if (!CashierPresentation.MatchesCurrency(item.Currency, _configuration.Currency))
        {
            StatusText.Text = "This item's currency differs from the terminal currency. Ask your manager to check the menu and terminal setup.";
            return;
        }
        var existing = cart.FirstOrDefault(line => line.ProductId == item.ProductId);
        if (existing is null) cart.Add(new CartLine(item)); else if (existing.Quantity < int.MaxValue) existing.Quantity++;
        RefreshCart();
    }

    private void IncreaseQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (CanEditCart() && sender is Button { DataContext: CartLine line } && line.Quantity < int.MaxValue)
        { line.Quantity++; RefreshCart(); }
    }

    private void DecreaseQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (CanEditCart() && sender is Button { DataContext: CartLine line })
        { if (line.Quantity > 1) line.Quantity--; else cart.Remove(line); RefreshCart(); }
    }

    private void RefreshCart() { CartList.Items.Refresh(); UpdateCartTotal(); UpdateOperationalState(); }
    private void MenuFilter_Changed(object sender, TextChangedEventArgs e) => RefreshMenuFilter();
    private void StationFilter_Changed(object sender, SelectionChangedEventArgs e) => RefreshMenuFilter();
    private void RefreshMenuFilter()
    {
        if (!viewInitialized) return;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(menu);
        view.Refresh();
        MenuEmptyText.Text = menu.Count == 0 ? "Open a shift, then refresh the menu to start." : "No menu items match your search.";
        MenuEmptyText.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void PlaceOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null || busy || !PlaceOrderButton.IsEnabled) return;
        cardTokenRequired = false;
        if (pendingCheckout is null && _configuration.PaymentMethod == "cash_manual" && cashSessionId is null)
        {
            ShiftCashTab.IsSelected = true;
            StatusText.Text = "Open a THB cash session before sending this cash order.";
            return;
        }
        try
        {
            SetBusy(pendingCheckout is null ? "Sending order…" : "Verifying the original order…");
            if (pendingCheckout is null)
            {
                var created = PendingCheckout.Create(_configuration,
                    cart.Select(line => new CheckoutLine(line.ProductId, line.Quantity)).ToArray());
                _localStore.SavePendingCheckout(created); // Never send until recovery is durable.
                pendingCheckout = created;
            }
            string? cardToken = CardTokenBox.Password.Length == 0 ? null : CardTokenBox.Password;
            CardTokenBox.Clear();
            var result = await _api.PlaceOrderAsync(_authentication.CurrentToken, pendingCheckout, cardToken);
            cardTokenRequired = result.CardTokenRequired;
            bool clearCart = false;
            if (_configuration.PaymentMethod == "card_omise_test" && result.Status is PosOrderStatus.KitchenAccepted or PosOrderStatus.PaymentPending)
            {
                StatusText.Text = result.CardTokenRequired
                    ? "Original card order is waiting for a token. Generate a fresh unused test token and authorize this original order."
                    : "Original card order retained. Verify with the token field blank; server reconciliation owns uncertain payments.";
            }
            else if (result.Status == PosOrderStatus.KitchenAccepted)
            {
                pendingOrder = result;
                settlementIdempotencyKey = pendingCheckout.SettlementKey;
                settlementUncertain = false;
                PaymentTab.IsSelected = true;
                _localStore.SavePendingSettlement(new(result.OrderId, result.TotalAmount, result.Currency,
                    settlementIdempotencyKey.Value));
                StatusText.Text = "Order sent to the kitchen. Confirm payment when received.";
                clearCart = true;
            }
            else if (result.Status == PosOrderStatus.Paid)
            {
                _localStore.ClearPendingCheckout();
                pendingCheckout = null;
                StatusText.Text = "The original order is already Paid. Do not collect payment again.";
                clearCart = true;
            }
            else if (result.Status is PosOrderStatus.Rejected or PosOrderStatus.PaymentFailed)
            {
                _localStore.ClearPendingCheckout();
                pendingCheckout = null;
                StatusText.Text = "The original order was rejected and created no active sale. Its recovery lock is cleared; review the current cart before sending a new order.";
            }
            else
            {
                StatusText.Text = $"Original order is {result.Status}. Verify later or ask your manager to reconcile it. No new order was submitted.";
            }
            if (clearCart)
            {
                cart.Clear();
                UpdateCartTotal();
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = pendingCheckout is null
                ? "Checkout was not sent because local recovery could not be saved. Check terminal setup and storage."
                : exception is PosApiException api
                    ? $"{api.Message} Original checkout retained. Use Verify order; do not create another order."
                    : "Original checkout retained. Use Verify order to check its result; do not create another order.";
        }
        finally { CardTokenBox.Clear(); UpdateOperationalState(); }
    }
    private void CardToken_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && !busy) UpdateOperationalState();
    }
    private async void Paid_Click(object sender, RoutedEventArgs e)
    {
        if (_authentication.CurrentToken is null || pendingOrder is null || settlementIdempotencyKey is null
            || settlementInFlight || !PaidButton.IsEnabled) return;
        PosOrderResult currentOrder = pendingOrder;
        Guid currentIdempotencyKey = settlementIdempotencyKey.Value;
        LocalPendingSettlementState? savedSettlement = _localStore.LoadPendingSettlement();
        string method = savedSettlement?.Method ?? SelectedTenderMethod();
        if (method == "cash" && cashSessionId is null)
        {
            StatusText.Text = "Open a THB cash session before confirming a cash payment.";
            return;
        }
        string? bankReference = savedSettlement?.BankReference ?? (string.IsNullOrWhiteSpace(BankReferenceTextBox.Text)
            ? null
            : BankReferenceTextBox.Text.Trim());
        if (method == "promptpay_manual" && (!ReceiptConfirmedCheckBox.IsChecked.GetValueOrDefault()
            || bankReference is null))
        {
            StatusText.Text = "Verify the PromptPay receipt and enter its reference before marking the order Paid.";
            return;
        }
        if (MessageBox.Show(this,
                settlementUncertain ? "Verify the previous payment attempt? Do not collect payment again."
                : $"Confirm {currentOrder.Currency} {currentOrder.TotalAmount:N2} received by {(method == "cash" ? "cash" : "PromptPay")}?",
                "Confirm payment", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        settlementInFlight = true;
        SetBusy(settlementUncertain ? "Verifying the previous payment attempt…" : "Confirming payment…");
        try
        {
            // Persist uncertainty before sending: a process exit after the server commits
            // must reopen in verification mode with the exact original tender fields.
            var attempt = new LocalPendingSettlementState(currentOrder.OrderId, currentOrder.TotalAmount,
                currentOrder.Currency, currentIdempotencyKey, method,
                method == "promptpay_manual", method == "promptpay_manual" ? bankReference : null, true);
            ManualTenderResult result = await SettlementAttempt.ExecuteAsync(attempt,
                state => { _localStore.SavePendingSettlement(state); settlementUncertain = true; },
                () => _api.ConfirmManualSettlementAsync(
                    _authentication.CurrentToken, currentOrder.OrderId, currentIdempotencyKey, method,
                    currentOrder.TotalAmount, currentOrder.Currency, method == "promptpay_manual",
                    method == "promptpay_manual" ? bankReference : null),
                () => _localStore.ClearCheckoutAndSettlement());
            pendingCheckout = null;
            pendingOrder = null;
            settlementIdempotencyKey = null;
            settlementUncertain = false;
            BankReferenceTextBox.Clear();
            ReceiptConfirmedCheckBox.IsChecked = false;
            StatusText.Text = result.Replayed
                ? $"Order {result.OrderId:D} was already Paid; the original settlement was verified."
                : $"Order {result.OrderId:D} is Paid.";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            || exception is PosApiException { StatusCode: >= 500 })
        {
            settlementUncertain = true;
            StatusText.Text = "Payment result is uncertain. Use Verify payment to check the previous attempt. Do not collect again.";
        }
        catch (PosApiException exception)
        {
            StatusText.Text = exception.StatusCode switch
            {
                401 => "Sign in again before verifying this payment.",
                403 => "Your account lacks order.manual-payment.confirm for this branch.",
                409 => "The order changed or was settled elsewhere. Do not collect payment again; reconcile the order before continuing.",
                _ => exception.Message
            };
        }
        catch (Exception)
        {
            StatusText.Text = "Payment recovery could not be completed. Keep this order open and do not collect again. Preserve local recovery files for support.";
        }
        finally
        {
            settlementInFlight = false;
            UpdateOperationalState();
        }
    }

    private void TenderMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // WPF can raise SelectionChanged while InitializeComponent is still
        // constructing the named controls used below.
        if (!viewInitialized)
        {
            return;
        }

        UpdateTenderControls();
        UpdateOperationalState();
    }

    private async void LoadCashReviews_Click(object sender, RoutedEventArgs e) =>
        await LoadCashReviewsAsync(loadMore: false);

    private async void LoadMoreCashReviews_Click(object sender, RoutedEventArgs e) =>
        await LoadCashReviewsAsync(loadMore: true);

    private async Task LoadCashReviewsAsync(bool loadMore)
    {
        if (_authentication.CurrentToken is null) return;
        if (CashReviewFromDatePicker.SelectedDate is not DateTime fromDate ||
            CashReviewToDatePicker.SelectedDate is not DateTime toDate || fromDate.Date > toDate.Date ||
            toDate.Date.AddDays(1) - fromDate.Date > TimeSpan.FromDays(31))
        {
            StatusText.Text = "Choose a valid cash-review date range of at most 31 days.";
            return;
        }
        if (loadMore && string.IsNullOrWhiteSpace(cashReviewCursor)) return;
        try
        {
            SetBusy(loadMore ? "Loading more cash reviews…" : "Loading cash-review history…");
            if (!loadMore)
            {
                cashReviewAccess = await _api.GetCashReviewAccessAsync(_authentication.CurrentToken);
                if (!cashReviewAccess.CanRead)
                {
                    cashReviews.Clear();
                    selectedCashReview = null;
                    RenderCashReviewDetail();
                    CashReviewAccessText.Text = "Your account does not have pos.cash-review.read for this branch.";
                    StatusText.Text = "Your account is not authorized to read cash reviews.";
                    return;
                }
                cashReviews.Clear();
                selectedCashReview = null;
                cashReviewCursor = null;
                RenderCashReviewDetail();
            }
            DateTimeOffset fromUtc = LocalDateBoundary(fromDate.Date);
            DateTimeOffset toUtc = LocalDateBoundary(toDate.Date.AddDays(1));
            PosCashReviewPage page = await _api.GetCashReviewsAsync(_authentication.CurrentToken,
                fromUtc, toUtc, loadMore ? cashReviewCursor : null);
            foreach (PosCashReviewListItem item in page.Items) cashReviews.Add(item);
            cashReviewCursor = page.NextCursor;
            if (!loadMore && pendingCashReviewAttempt is not null)
            {
                PosCashReviewDetail pendingDetail = await _api.GetCashReviewAsync(
                    _authentication.CurrentToken, pendingCashReviewAttempt.CashSessionId);
                if (pendingDetail.History.Any(entry => entry.Id == pendingCashReviewAttempt.IdempotencyKey))
                {
                    if (TryClearPendingCashReviewRecovery())
                    {
                        CashReviewReasonTextBox.Clear();
                        StatusText.Text = "The pending cash-review decision was found in immutable history and recovery was cleared.";
                    }
                    else
                    {
                        StatusText.Text = "The decision is committed, but local recovery cleanup failed. Preserve the POS database and try again.";
                    }
                }
                else
                {
                    if (cashReviews.All(item => item.CashSessionId != pendingDetail.Session.CashSessionId))
                        cashReviews.Insert(0, pendingDetail.Session);
                    CashReviewList.SelectedItem = cashReviews.First(item =>
                        item.CashSessionId == pendingDetail.Session.CashSessionId);
                    CashReviewReasonTextBox.Text = pendingCashReviewAttempt.Reason;
                    StatusText.Text = "Pending cash-review recovery loaded. Use Verify decision with the original values.";
                }
            }
            CashReviewAccessText.Text = cashReviewAccess?.CanResolve == true
                ? "Supervisor access · decisions enabled"
                : "Read-only access · decisions require pos.cash-review.resolve";
            if (pendingCashReviewAttempt is null && !StatusText.Text.StartsWith("The pending cash-review", StringComparison.Ordinal))
                StatusText.Text = cashReviews.Count == 0
                    ? "No closed cash sessions were found in this date range."
                    : $"Loaded {cashReviews.Count} closed cash session(s).";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception is PosApiException api ? api.Message : "Cash-review history could not be loaded.";
        }
        finally { UpdateOperationalState(); }
    }

    private async void CashReviewList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!viewInitialized || _authentication.CurrentToken is null ||
            CashReviewList.SelectedItem is not PosCashReviewListItem selected) return;
        selectedCashReview = null;
        RenderCashReviewDetail();
        try
        {
            SetBusy("Loading cash-review detail…");
            PosCashReviewDetail detail = await _api.GetCashReviewAsync(
                _authentication.CurrentToken, selected.CashSessionId);
            if (CashReviewList.SelectedItem is PosCashReviewListItem current &&
                current.CashSessionId == detail.Session.CashSessionId)
            {
                selectedCashReview = detail;
                if (pendingCashReviewAttempt is not null &&
                    detail.History.Any(entry => entry.Id == pendingCashReviewAttempt.IdempotencyKey))
                {
                    if (TryClearPendingCashReviewRecovery()) CashReviewReasonTextBox.Clear();
                }
                else if (pendingCashReviewAttempt?.CashSessionId == detail.Session.CashSessionId)
                {
                    CashReviewReasonTextBox.Text = pendingCashReviewAttempt.Reason;
                }
                ReplaceCashReviewItem(detail.Session);
                RenderCashReviewDetail();
                StatusText.Text = "Cash-review detail loaded.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = exception is PosApiException api ? api.Message : "Cash-review detail could not be loaded.";
        }
        finally { UpdateOperationalState(); }
    }

    private async void InvestigateCashReview_Click(object sender, RoutedEventArgs e) =>
        await SubmitCashReviewAsync("investigate");

    private async void ApproveCashReview_Click(object sender, RoutedEventArgs e) =>
        await SubmitCashReviewAsync("approve");

    private async Task SubmitCashReviewAsync(string decision)
    {
        if (_authentication.CurrentToken is null || selectedCashReview is null ||
            cashReviewAccess?.CanResolve != true) return;
        if (pendingCashReviewAttempt is not null &&
            (pendingCashReviewAttempt.CashSessionId != selectedCashReview.Session.CashSessionId ||
             pendingCashReviewAttempt.Decision != decision))
        {
            StatusText.Text = "Verify the saved cash-review decision before making another decision.";
            return;
        }
        string reason = pendingCashReviewAttempt?.Reason ?? CashReviewReasonTextBox.Text.Trim();
        if (reason.Length is < 1 or > 200)
        {
            StatusText.Text = "Enter a supervisor reason from 1 to 200 characters.";
            return;
        }
        PosCashReviewListItem session = selectedCashReview.Session;
        if (pendingCashReviewAttempt is null &&
            (session.VarianceAmount == 0 || session.ReviewStatus == "balanced"))
        {
            StatusText.Text = "This session is balanced and does not require a decision.";
            return;
        }
        if (pendingCashReviewAttempt is null && session.ReviewStatus == "approved")
        {
            StatusText.Text = "This financial version is already approved.";
            return;
        }

        LocalPendingCashReviewState candidate = pendingCashReviewAttempt ??
            LocalPendingCashReviewState.Create(_configuration, session.CashSessionId, decision,
                reason, session.SessionVersion, session.ReviewVersion);
        string action = pendingCashReviewAttempt is null
            ? decision == "approve" ? "approve this variance" : "mark this variance for investigation"
            : "verify the saved decision using its original identity and values";
        if (MessageBox.Show($"Confirm that you want to {action}. The reason and your identity will be retained.",
                "Confirm cash review", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        try
        {
            if (pendingCashReviewAttempt is null)
            {
                _localStore.SavePendingCashReview(candidate, _configuration);
                pendingCashReviewAttempt = candidate;
            }
            SetBusy("Saving cash-review decision…");
            selectedCashReview = await _api.ResolveCashReviewAsync(_authentication.CurrentToken,
                candidate.CashSessionId, candidate.Decision, candidate.Reason,
                candidate.ExpectedSessionVersion, candidate.ExpectedReviewVersion, candidate.IdempotencyKey);
            bool recoveryCleared = TryClearPendingCashReviewRecovery();
            ReplaceCashReviewItem(selectedCashReview.Session);
            if (recoveryCleared) CashReviewReasonTextBox.Clear();
            RenderCashReviewDetail();
            StatusText.Text = !recoveryCleared
                ? "The decision is committed, but local recovery cleanup failed. Preserve the POS database and verify again."
                : decision == "approve"
                    ? "Cash variance approved and recorded in immutable history."
                    : "Cash variance marked for investigation and recorded in immutable history.";
        }
        catch (PosApiException exception) when (exception.StatusCode is 400 or 409)
        {
            bool recoveryCleared = TryClearPendingCashReviewRecovery();
            try
            {
                selectedCashReview = await _api.GetCashReviewAsync(_authentication.CurrentToken,
                    candidate.CashSessionId);
                ReplaceCashReviewItem(selectedCashReview.Session);
                RenderCashReviewDetail();
            }
            catch { }
            StatusText.Text = !recoveryCleared
                ? "The decision was rejected, but local recovery cleanup failed. Preserve the POS database and verify again."
                : exception.StatusCode == 409
                ? "The cash review changed. Review the refreshed values before making another decision."
                : exception.Message;
        }
        catch (PosApiException exception) when (exception.StatusCode is 403 or 404)
        {
            StatusText.Text = "The review result remains uncertain because access or scope changed. Recovery was retained; restore read access and reconcile immutable history.";
        }
        catch
        {
            StatusText.Text = "The review result is uncertain. Recovery was retained; sign in and use Verify decision with the original values.";
        }
        finally { UpdateOperationalState(); }
    }

    private void ReplaceCashReviewItem(PosCashReviewListItem item)
    {
        int index = cashReviews.ToList().FindIndex(existing => existing.CashSessionId == item.CashSessionId);
        if (index >= 0) cashReviews[index] = item;
    }

    private bool TryClearPendingCashReviewRecovery()
    {
        try
        {
            _localStore.ClearPendingCashReview();
            pendingCashReviewAttempt = null;
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or
            UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            return false;
        }
    }

    private void RenderCashReviewDetail()
    {
        if (!viewInitialized || selectedCashReview is null)
        {
            if (viewInitialized)
            {
                CashReviewDetailText.Text = "Select a closed cash session.";
                CashReviewMovementList.ItemsSource = null;
                CashReviewHistoryList.ItemsSource = null;
            }
            return;
        }
        PosCashReviewListItem session = selectedCashReview.Session;
        CashReviewDetailText.Text =
            $"{session.ShiftNumber} · {session.Currency}\nExpected {session.ExpectedClosingAmount:N2} · Counted {session.ActualClosingAmount:N2} · Variance {session.VarianceAmount:N2}\n{session.ReviewStatus} · financial v{session.SessionVersion} · review v{session.ReviewVersion}";
        CashReviewMovementList.ItemsSource = selectedCashReview.Movements;
        CashReviewHistoryList.ItemsSource = selectedCashReview.History;
    }

    private static DateTimeOffset LocalDateBoundary(DateTime localDate)
    {
        DateTime unspecified = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    private async void RefreshReconciliation_Click(object sender, RoutedEventArgs e)
    {
        if (!IsSignedIn() || (cashSessionId is null && cashSummary is null)) return;
        try
        {
            SetBusy("Refreshing cash reconciliation…");
            await RefreshCashSummaryAsync();
            StatusText.Text = "Cash reconciliation refreshed. Review it before closing the drawer.";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception is PosApiException api
                ? api.Message
                : "Cash reconciliation could not be refreshed.";
        }
        finally { UpdateOperationalState(); }
    }

    private async Task RefreshCashSummaryAsync()
    {
        Guid? reconciliationSessionId = cashSessionId ?? cashSummary?.CashSessionId;
        if (_authentication.CurrentToken is null || reconciliationSessionId is null) return;
        cashSummary = await _api.GetCashSessionSummaryAsync(
            _authentication.CurrentToken, reconciliationSessionId.Value);
        cashSummaryVerified = true;
        RenderCashSummary();
    }

    private void ClosingCash_Changed(object sender, TextChangedEventArgs e) => RenderCashSummary();

    private void RenderCashSummary()
    {
        if (!viewInitialized || cashSummary is null)
        {
            if (viewInitialized)
            {
                ReconciliationStatusText.Text = cashSessionId is null
                    ? "Open a cash session to view reconciliation."
                    : "Refresh reconciliation to load this cash session.";
                ReconciliationOpeningText.Text = "—";
                ReconciliationMovementsText.Text = "—";
                ReconciliationExpectedText.Text = "—";
                ReconciliationVarianceText.Text = "—";
                ReconciliationVarianceText.Foreground = System.Windows.Media.Brushes.DimGray;
                CashMovementList.ItemsSource = null;
            }
            return;
        }

        ReconciliationStatusText.Text = cashSummary.Status == "closed"
            ? cashSummaryVerified
                ? $"Closed · server verified · version {cashSummary.ConcurrencyVersion}"
                : $"Closed · verification pending · provisional version {cashSummary.ConcurrencyVersion}"
            : $"Open · version {cashSummary.ConcurrencyVersion} · refresh before close";
        ReconciliationOpeningText.Text = CashierPresentation.Money(cashSummary.OpeningAmount, cashSummary.Currency);
        ReconciliationMovementsText.Text = CashierPresentation.Money(cashSummary.NetMovementAmount, cashSummary.Currency);
        ReconciliationExpectedText.Text = CashierPresentation.Money(cashSummary.ExpectedClosingAmount, cashSummary.Currency);
        decimal? counted = decimal.TryParse(ClosingCashTextBox.Text, out decimal value) && value >= 0 ? value : null;
        decimal? variance = cashSummary.Status == "closed"
            ? cashSummary.VarianceAmount
            : counted - cashSummary.ExpectedClosingAmount;
        ReconciliationVarianceText.Text = variance is null
            ? "Enter counted cash"
            : CashierPresentation.Money(variance.Value, cashSummary.Currency);
        ReconciliationVarianceText.Foreground = variance is 0 ? System.Windows.Media.Brushes.SeaGreen
            : variance is null ? System.Windows.Media.Brushes.DimGray
            : System.Windows.Media.Brushes.Firebrick;
        CashMovementList.ItemsSource = cashSummary.Movements;
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        CardTokenBox.Clear();
        if (_activeShift is not null || cashSessionId is not null || pendingOrder is not null ||
            pendingCheckout is not null || pendingCashReviewAttempt is not null)
        {
            StatusText.Text = pendingCashReviewAttempt is not null
                ? "Verify the pending cash-review decision before signing out."
                : "Resolve the pending payment, shift, and cash session before signing out.";
            return;
        }

        _authentication.SignOut();
        reauthenticationRequired = false;
        sessionLockMessage = null;
        cashSummary = null;
        cashSummaryVerified = false;
        cashReviews.Clear();
        cashReviewAccess = null;
        selectedCashReview = null;
        cashReviewCursor = null;
        RenderCashSummary();
        RenderCashReviewDetail();
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
        busy = true;
        WorkspaceTabs.IsEnabled = false;
        SignOutButton.IsEnabled = false;
        StatusText.Text = message;
        SignInButton.IsEnabled = false;
        OpenShiftButton.IsEnabled = false;
        CloseShiftButton.IsEnabled = false;
        CloseCashButton.IsEnabled = false;
        RecordMovementButton.IsEnabled = false;
        PaidButton.IsEnabled = false;
        LoadCashReviewsButton.IsEnabled = false;
        LoadMoreCashReviewsButton.IsEnabled = false;
        InvestigateCashReviewButton.IsEnabled = false;
        ApproveCashReviewButton.IsEnabled = false;
    }

    private void UpdateOperationalState()
    {
        busy = false;
        WorkspaceTabs.IsEnabled = true;
        bool signedIn = IsSignedIn();
        bool hasStoredSession = _authentication.CurrentToken is not null;
        bool hasActiveShift = _activeShift is not null;
        SignInButton.Content = "Sign in";
        SignInButton.IsEnabled = !signedIn;
        SignOutButton.IsEnabled = hasStoredSession;
        OpenShiftButton.IsEnabled = signedIn && !hasActiveShift;
        CloseShiftButton.IsEnabled = hasActiveShift;
        LoadMenuButton.IsEnabled = signedIn && hasActiveShift;
        PlaceOrderButton.IsEnabled = CashierPresentation.CanAttemptOrder(
            signedIn, hasActiveShift, pendingOrder is not null, pendingCheckout is not null, cart.Count);
        PlaceOrderButton.Content = pendingCheckout is null ? "Send order · Continue to payment" : "Verify original order";
        OmiseTestCardPanel.Visibility = _configuration.PaymentMethod == "card_omise_test" ? Visibility.Visible : Visibility.Collapsed;
        CardTokenBox.IsEnabled = signedIn && hasActiveShift && !busy && _configuration.PaymentMethod == "card_omise_test"
            && (pendingCheckout is null || cardTokenRequired);
        if (pendingCheckout is not null && CardTokenBox.Password.Length > 0) PlaceOrderButton.Content = "Authorize original test card order";
        PlaceOrderButton.ToolTip = pendingCheckout is null && _configuration.PaymentMethod == "cash_manual" && cashSessionId is null
            ? "Open a THB cash session before sending. Selecting Send order will take you to Shift & cash."
            : "Send the current order and continue to payment.";
        OpenCashButton.IsEnabled = signedIn && hasActiveShift && cashSessionId is null;
        CloseCashButton.IsEnabled = signedIn && cashSessionId is not null
            && pendingOrder is null && pendingCheckout is null && !HasQueuedCashMovements(cashSessionId.Value);
        RefreshReconciliationButton.IsEnabled = signedIn && (cashSessionId is not null || cashSummary is not null);
        RecordMovementButton.IsEnabled = signedIn && cashSessionId is not null;
        LoadCashReviewsButton.IsEnabled = signedIn;
        LoadMoreCashReviewsButton.IsEnabled = signedIn && !string.IsNullOrWhiteSpace(cashReviewCursor);
        bool hasMatchingPendingReview = pendingCashReviewAttempt is not null &&
            selectedCashReview?.Session.CashSessionId == pendingCashReviewAttempt.CashSessionId;
        bool pendingReviewMatchesSelection = pendingCashReviewAttempt is null || hasMatchingPendingReview;
        bool canResolveCashReview = signedIn && cashReviewAccess?.CanResolve == true &&
            pendingReviewMatchesSelection &&
            selectedCashReview is not null && (hasMatchingPendingReview ||
                selectedCashReview.Session.VarianceAmount != 0 &&
                selectedCashReview.Session.ReviewStatus is not ("balanced" or "approved"));
        InvestigateCashReviewButton.Content = pendingCashReviewAttempt?.Decision == "investigate"
            ? "Verify decision" : "Mark investigating";
        ApproveCashReviewButton.Content = pendingCashReviewAttempt?.Decision == "approve"
            ? "Verify decision" : "Approve variance";
        InvestigateCashReviewButton.IsEnabled = canResolveCashReview &&
            (pendingCashReviewAttempt?.Decision == "investigate" ||
             pendingCashReviewAttempt is null && selectedCashReview!.Session.ReviewStatus != "investigating");
        ApproveCashReviewButton.IsEnabled = canResolveCashReview &&
            (pendingCashReviewAttempt is null || pendingCashReviewAttempt.Decision == "approve");
        CashReviewReasonTextBox.IsEnabled = canResolveCashReview && pendingCashReviewAttempt is null;
        MenuList.IsEnabled = CanEditCart();
        CartList.IsEnabled = CanEditCart();
        UpdateTenderControls();
        RefreshMenuFilter();
        ContextText.Text = $"Branch {_configuration.BranchId.ToString("N")[..8]} · Terminal {_configuration.TerminalId.ToString("N")[..8]} · {_configuration.Currency} · Connectivity checked per action";
        string method = SelectedTenderMethod();
        PaidButton.Content = settlementUncertain ? "Verify payment" : "Confirm payment received";
        PaidButton.IsEnabled = _configuration.PaymentMethod != "card_omise_test" && signedIn && hasActiveShift && pendingOrder is not null && !settlementInFlight
            && (method != "cash" || cashSessionId is not null)
            && (method != "promptpay_manual" || PromptPayQrImage.Source is not null);
        PendingOrderText.Text = pendingOrder is null
            ? "No order awaiting payment."
            : $"Order {pendingOrder.OrderId:D} · {pendingOrder.Currency} {pendingOrder.TotalAmount:N2} · awaiting payment";
        EnrollTerminalButton.IsEnabled = signedIn;
        IReadOnlyList<LocalOutboxOperation> operations = outbox.Load();
        int rejected = operations.Count(operation => operation.TerminalFailureStatusCode is not null);
        bool movementsPending = operations.Count > 0;
        ReplayOutboxButton.IsEnabled = signedIn && operations.Count > rejected;
        RetryRejectedOutboxButton.IsEnabled = signedIn && rejected > 0;
        DateTimeOffset? lastAttempt = operations.Max(operation => operation.LastAttemptAtUtc);
        string lastAttemptText = lastAttempt is null
            ? "no attempts yet"
            : $"last attempt {lastAttempt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        OutboxStatusText.Text = $"Offline queue: {operations.Count - rejected} pending, {rejected} rejected · {lastAttemptText}";
        OutboxOperationList.ItemsSource = operations;
        SessionText.Text = signedIn ? (hasActiveShift ? "Signed in · Shift open" : "Signed in · Open a shift") : "Signed out";
        ActiveShiftText.Text = hasActiveShift
            ? $"Shift {_activeShift!.ShiftNumber} · Cash session {(cashSessionId is null ? "closed" : "open")}"
            : "No active shift on this terminal.";
        string guidance = CashierPresentation.SessionGuidance(
            signedIn, hasActiveShift, cashSessionId is not null, pendingCheckout is not null,
            pendingOrder is not null, movementsPending);
        SessionGuidanceText.Text = guidance;
        SignOutButton.ToolTip = pendingCashReviewAttempt is null
            ? guidance
            : "Verify the pending cash-review decision before signing out.";
        CloseShiftButton.ToolTip = guidance;
        if (reauthenticationRequired && sessionLockMessage is not null)
            StatusText.Text = sessionLockMessage;
    }

    private bool IsSignedIn()
    {
        PosTokenSet? token = _authentication.CurrentToken;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return !reauthenticationRequired && token is not null && token.ExpiresAtUtc > now &&
            !PosSessionPolicy.IsAbsoluteExpired(token, now) &&
            !PosSessionPolicy.IsIdleExpired(lastOperatorActivityUtc, now,
                TimeSpan.FromMinutes(_configuration.SessionIdleTimeoutMinutes));
    }

    private async void SessionTimer_Tick(object? sender, EventArgs e)
    {
        PosTokenSet? token = _authentication.CurrentToken;
        if (busy || reauthenticationRequired || token is null)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (PosSessionPolicy.IsAbsoluteExpired(token, now))
        {
            _authentication.RequireInteractiveSignIn();
            LockSession("The maximum POS session time was reached. Sign in again; saved operational and review recovery has been retained.");
            return;
        }
        if (PosSessionPolicy.IsIdleExpired(lastOperatorActivityUtc, now,
            TimeSpan.FromMinutes(_configuration.SessionIdleTimeoutMinutes)))
        {
            _authentication.RequireInteractiveSignIn();
            LockSession("POS locked after inactivity. Sign in again; saved operational and review recovery has been retained.");
            return;
        }
        bool shouldRefresh = PosSessionPolicy.ShouldRefresh(token, now,
            TimeSpan.FromSeconds(_configuration.TokenRefreshLeadSeconds));
        if (token.ExpiresAtUtc <= now && !shouldRefresh)
        {
            _authentication.RequireInteractiveSignIn();
            LockSession("Your access token expired and cannot be renewed. Sign in again; saved operational and review recovery has been retained.");
            return;
        }
        if (tokenRefreshInFlight || now < nextTokenRefreshAttemptUtc || !shouldRefresh)
        {
            return;
        }

        tokenRefreshInFlight = true;
        try
        {
            await _authentication.RefreshAsync();
            nextTokenRefreshAttemptUtc = DateTimeOffset.MinValue;
            if (!busy) UpdateOperationalState();
        }
        catch (PosReauthenticationRequiredException)
        {
            LockSession("Your identity session ended. Sign in again; saved operational and review recovery has been retained.");
        }
        catch (HttpRequestException)
        {
            nextTokenRefreshAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(15);
            if (token.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                LockSession("The access token expired while identity was unavailable. Sign in again when connectivity returns; saved operational state is retained.");
        }
        catch (OperationCanceledException)
        {
            // Window shutdown can cancel an in-flight refresh.
        }
        catch (Exception)
        {
            LockSession("The POS could not securely renew this session. Sign in again; saved operational and review recovery has been retained.");
        }
        finally
        {
            tokenRefreshInFlight = false;
        }
    }

    private void LockSession(string message)
    {
        CardTokenBox.Clear();
        cardTokenRequired = false;
        reauthenticationRequired = true;
        sessionLockMessage = message;
        if (!busy) UpdateOperationalState();
        StatusText.Text = message;
    }

    private bool RecordOperatorActivity()
    {
        if (reauthenticationRequired) return true;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_authentication.CurrentToken is not null &&
            PosSessionPolicy.IsIdleExpired(lastOperatorActivityUtc, now,
                TimeSpan.FromMinutes(_configuration.SessionIdleTimeoutMinutes)))
        {
            _authentication.RequireInteractiveSignIn();
            LockSession("POS locked after inactivity. Sign in again; saved operational and review recovery has been retained.");
            return false;
        }
        lastOperatorActivityUtc = now;
        return true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!RecordOperatorActivity()) e.Handled = true;
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (!RecordOperatorActivity()) e.Handled = true;
        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewTouchDown(TouchEventArgs e)
    {
        if (!RecordOperatorActivity()) e.Handled = true;
        base.OnPreviewTouchDown(e);
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

    private string SelectedTenderMethod() =>
        (TenderMethodComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "cash";

    private void ConfigureTenderControls()
    {
        TenderMethodComboBox.SelectedIndex = _configuration.PaymentMethod == "promptpay_manual" ? 1 : 0;
        string? configuredPath = _configuration.PromptPayQrImagePath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string path = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                PromptPayQrImage.Source = bitmap;
            }
        }
        UpdateTenderControls();
    }

    private void UpdateTenderControls()
    {
        bool promptPay = SelectedTenderMethod() == "promptpay_manual";
        bool editable = _localStore.LoadPendingSettlement()?.Method is null;
        TenderMethodComboBox.IsEnabled = editable;
        PromptPayQrPanel.Visibility = promptPay ? Visibility.Visible : Visibility.Collapsed;
        ReceiptConfirmedCheckBox.IsEnabled = promptPay && editable;
        BankReferenceTextBox.IsEnabled = promptPay && editable;
        PromptPayQrText.Text = PromptPayQrImage.Source is null
            ? "PromptPay QR is not configured. Do not confirm payment until the restaurant QR image is installed."
            : "Ask the customer to scan this restaurant PromptPay QR, then verify the receipt.";
    }

    protected override void OnClosed(EventArgs e)
    {
        signInCancellation?.Cancel();
        sessionTimer.Stop();
        sessionTimer.Tick -= SessionTimer_Tick;
        _authentication.StatusChanged -= OnStatusChanged;
        _api.Dispose();
        base.OnClosed(e);
    }

    private void UpdateCartTotal() => TotalText.Text = CashierPresentation.Money(cart.Sum(line => line.LineTotal), _configuration.Currency);

    private sealed class CartLine(PosMenuItem item)
    {
        public Guid ProductId { get; } = item.ProductId;
        public string Name { get; } = item.Name;
        public decimal UnitPrice { get; } = item.UnitPrice;
        public int Quantity { get; set; } = 1;
        public decimal LineTotal => UnitPrice * Quantity;
    }
}
