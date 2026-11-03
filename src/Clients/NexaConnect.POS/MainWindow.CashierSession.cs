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
        bool saleEntry = ShiftCashTab.IsSelected || CheckoutTab.IsSelected;
        CashierReadyPanel.Visibility = saleEntry && !busy && !reauthenticationRequired && !displaySafetyBlocked && !transaction
            ? Visibility.Visible : Visibility.Collapsed;
        CashierReadyIdentity.Text = signedIn ? OperatorLabel : "Sign in to start work";
        CashierRegisterStatus.Text = $"Register {_configuration.TerminalId.ToString("N")[..8]} · "
            + (_activeShift is null ? "Shift closed" : cashSessionId is null ? "Shift open · Cash drawer session closed" : "Shift and cash session open");
        StartSaleButton.Content = !signedIn ? "Sign in" : _activeShift is null ? "Open shift" : "Start sale";
        StartSaleButton.IsEnabled = !busy;
        SetSessionPinButton.IsEnabled = signedIn && !busy && _configuration.EnablePinUnlock;
        SetSessionPinButton.Content = _authentication.HasSessionPin ? "Change session unlock PIN" : "Set session unlock PIN";
        SessionSecurityStatus.Text = $"Locks after {_configuration.SessionIdleTimeoutMinutes} minutes of inactivity. Full sign-in after {_configuration.SessionAbsoluteTimeoutHours} hours at most. "
            + "PIN unlock needs a still-valid session. Fingerprint and staff-card readers are not configured.";
        PinUnlockButton.Visibility = _authentication.CanQuickUnlock ? Visibility.Visible : Visibility.Collapsed;
        PinUnlockButton.IsEnabled = !busy && !pinUnlockBusy && signInCancellation is null;
        if (pinUnlockBusy) { ResumeButton.IsEnabled = false; LockedSignOutButton.IsEnabled = false; }
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
    private void SetSessionPin_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !IsSignedIn() || displaySafetyBlocked) return;
        if (!_authentication.CanEnrollSessionPin)
        { StatusText.Text = "Lock, choose full Resume / Authenticate, then set the session PIN within two minutes."; return; }
        var first = new PosPinEntryWindow(this, "Choose a six-digit session PIN");
        if (first.ShowDialog() != true) return;
        string? pin = first.TakeValue();
        var confirm = new PosPinEntryWindow(this, "Confirm the session PIN");
        if (confirm.ShowDialog() != true) return;
        if (pin != confirm.TakeValue()) { StatusText.Text = "PINs did not match. Nothing was changed."; return; }
        try { if (!IsSignedIn()) return; _authentication.EnrollSessionPin(pin!); StatusText.Text = "Session PIN set. It unlocks only this cashier's live session; full sign-in clears it."; }
        catch { StatusText.Text = "PIN setup unavailable. Full sign-in is required; work is unchanged."; }
        RenderCashierReady();
    }
    private async void PinUnlock_Click(object sender, RoutedEventArgs e)
    {
        if (pinUnlockBusy || busy || signInCancellation is not null || displaySafetyBlocked || !_authentication.CanQuickUnlock) return;
        var prompt = new PosPinEntryWindow(this, "Unlock with your six-digit PIN");
        if (prompt.ShowDialog() != true) return;
        string pin = prompt.TakeValue()!;
        pinUnlockBusy = true; RenderCashierReady();
        try
        {
            bool resumed = await Task.Run(() => _authentication.TryResumeWithSessionPin(pin));
            if (windowCancellation.IsCancellationRequested) return;
            // A topology/security transition may supersede an in-flight PIN derivation.
            if (resumed && !displaySafetyBlocked && !_authentication.RequiresInteractiveSignIn)
            { reauthenticationRequired = false; sessionLockMessage = null; lastOperatorActivityUtc = DateTimeOffset.UtcNow; }
            else sessionLockMessage = _authentication.CanQuickUnlock ? "PIN not accepted. Try again or use full sign-in." : "PIN unlock unavailable. Full sign-in is required.";
        }
        catch { sessionLockMessage = "Secure unlock failed. Use full sign-in; saved work remains intact."; }
        finally { pinUnlockBusy = false; if (!windowCancellation.IsCancellationRequested) UpdateOperationalState(); }
    }
}
