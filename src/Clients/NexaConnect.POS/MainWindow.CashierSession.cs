using System.Windows;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private bool pinUnlockBusy;
    private string OperatorLabel => _authentication.CurrentToken?.DisplayName is {} name ? name
        : _authentication.CurrentToken?.Subject is {} subject
        ? $"Cashier {subject[..Math.Min(8, subject.Length)]}" : "No cashier signed in";
    private void RenderCashierReady()
    {
        bool signedIn = IsSignedIn();
        bool transaction = cart.Count != 0 || pendingCheckout is not null || pendingOrder is not null || pendingRefund is not null;
        bool saleEntry = ShiftCashTab.IsSelected;
        CashierReadyPanel.Visibility = saleEntry && !busy && !reauthenticationRequired && !displaySafetyBlocked && !transaction
            ? Visibility.Visible : Visibility.Collapsed;
        CashierReadyIdentity.Text = signedIn ? OperatorLabel : "Sign in to start work";
        CashierToolbarIdentity.Text = signedIn ? OperatorLabel : "Cashier";
        SignInButton.Visibility = signedIn && signInCancellation is null ? Visibility.Collapsed : Visibility.Visible;
        CashierRegisterStatus.Text = $"Register {_configuration.TerminalId.ToString("N")[..8]} · "
            + (_activeShift is null ? "Shift closed" : cashSessionId is null ? "Shift open · Cash drawer session closed" : "Shift and cash session open");
        StartSaleButton.Content = !signedIn ? "Sign in" : _activeShift is null ? "Open shift" : "Start sale";
        StartSaleButton.IsEnabled = !busy;
        SetSessionPinButton.IsEnabled = signedIn && !busy && !pinUnlockBusy && _configuration.EnablePinUnlock;
        SetSessionPinButton.Content = _authentication.HasSessionPin ? "Change cashier PIN" : "Set cashier PIN";
        SessionSecurityStatus.Text = $"Locks after {_configuration.SessionIdleTimeoutMinutes} minutes of inactivity. Full sign-in after {_configuration.SessionAbsoluteTimeoutHours} hours at most. "
            + "Your cashier PIN stays unchanged until changed or reset. Online session validation is required. Fingerprint and staff-card readers are not configured.";
        RenderUnlockChoice();
        LockedCashierIdentity.Text = $"{OperatorLabel} · Register {_configuration.TerminalId.ToString("N")[..8]} · Original work retained";
    }
    private void StartSale_Click(object sender, RoutedEventArgs e)
    {
        if (busy || reauthenticationRequired || displaySafetyBlocked) return;
        if (!IsSignedIn()) { SignIn_Click(sender, e); return; }
        if (_activeShift is null) { ShiftCashTab.IsSelected = true; ShiftNumberTextBox.Focus(); return; }
        CheckoutTab.IsSelected = true; MenuSearchTextBox.Focus();
    }
    private void SwitchCashier_Click(object sender, RoutedEventArgs e)
    {
        if (busy || pinUnlockBusy || !IsSignedIn() || displaySafetyBlocked) return;
        if (HasRetainedWork())
        { StatusText.Text = "Resolve the current shift, cash session and pending work before switching cashiers. Lock to pause this cashier instead."; return; }
        SignOut_Click(sender, e);
        reauthenticationRequired = true; RenderSessionLock(); SignIn_Click(sender, e);
    }
    private async void SetSessionPin_Click(object sender, RoutedEventArgs e)
    {
        if (busy || pinUnlockBusy || !IsSignedIn() || displaySafetyBlocked) return;
        if (!_authentication.CanEnrollSessionPin)
        { StatusText.Text = "Lock, choose full sign-in, then set or change your cashier PIN within two minutes."; return; }
        pinUnlockBusy = true; SetSessionPinButton.IsEnabled = false;
        try { await _authentication.LoadPinStatusAsync(windowCancellation.Token); }
        catch { if (!windowCancellation.IsCancellationRequested) StatusText.Text = "PIN service unavailable. Check connectivity and retry; nothing was changed."; return; }
        finally { pinUnlockBusy = false; if (!windowCancellation.IsCancellationRequested) RenderCashierReady(); }
        if (!IsSignedIn() || !_authentication.CanEnrollSessionPin || windowCancellation.IsCancellationRequested) return;
        string? currentPin = null;
        if (_authentication.HasSessionPin)
        {
            var current = new PosPinEntryWindow(this, "Enter your current cashier PIN");
            if (current.ShowDialog() != true) return;
            currentPin = current.TakeValue();
        }
        var first = new PosPinEntryWindow(this, "Choose a six-digit cashier PIN");
        if (first.ShowDialog() != true) return;
        string? pin = first.TakeValue();
        var confirm = new PosPinEntryWindow(this, "Confirm the cashier PIN");
        if (confirm.ShowDialog() != true) return;
        if (pin != confirm.TakeValue()) { StatusText.Text = "PINs did not match. Nothing was changed."; return; }
        pinUnlockBusy = true; SetSessionPinButton.IsEnabled = false;
        try
        {
            if (!IsSignedIn()) return;
            bool accepted = await _authentication.EnrollSessionPinAsync(pin!, currentPin, windowCancellation.Token);
            if (!windowCancellation.IsCancellationRequested) StatusText.Text = accepted
                ? "Cashier PIN saved securely. It remains the same across future full logins until changed or reset."
                : "PIN not changed. Check the current PIN or ask an administrator to reset a forgotten/revoked PIN. Reload before retrying.";
        }
        catch { if (!windowCancellation.IsCancellationRequested) StatusText.Text = "PIN save is unconfirmed. Reload before retrying; work is unchanged."; }
        finally { pinUnlockBusy = false; if (!windowCancellation.IsCancellationRequested) RenderCashierReady(); }
    }
    private async void PinUnlock_Click(object sender, RoutedEventArgs e)
    {
        if (pinUnlockBusy || busy || !reauthenticationRequired || windowCancellation.IsCancellationRequested
            || signInCancellation is not null || displaySafetyBlocked || !_authentication.CanQuickUnlock) return;
        pinUnlockBusy = true; RenderCashierReady();
        try
        {
            var prompt = new PosPinEntryWindow(this, "Unlock with your six-digit PIN");
            if (prompt.ShowDialog() != true || windowCancellation.IsCancellationRequested || displaySafetyBlocked) return;
            string pin = prompt.TakeValue()!;
            bool resumed = await _authentication.TryResumeWithSessionPinAsync(pin, windowCancellation.Token);
            if (windowCancellation.IsCancellationRequested) return;
            // A topology/security transition may supersede an in-flight PIN derivation.
            if (resumed && !displaySafetyBlocked && !_authentication.RequiresInteractiveSignIn)
            { reauthenticationRequired = false; sessionLockMessage = null; lastOperatorActivityUtc = DateTimeOffset.UtcNow; }
            else sessionLockMessage = _authentication.CanQuickUnlock ? "PIN not accepted or temporarily locked after repeated attempts. Try later or use full sign-in." : "PIN unlock unavailable. Full sign-in is required.";
        }
        catch { sessionLockMessage = "Online unlock unavailable. Check connectivity and retry, or use full sign-in. Saved work remains intact."; }
        finally { pinUnlockBusy = false; if (!windowCancellation.IsCancellationRequested) UpdateOperationalState(); }
    }
}
