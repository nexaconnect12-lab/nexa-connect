using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly LockPromptState lockPrompt=new();
    private bool openPinAfterWakeKey;
    private void RenderUnlockChoice()
    {
        bool signingIn = signInCancellation is not null;
        bool offerPin = !signingIn && _authentication.CanQuickUnlock;
        PinUnlockButton.Visibility = offerPin ? Visibility.Visible : Visibility.Collapsed;
        PinUnlockButton.IsEnabled = offerPin && !busy && !pinUnlockBusy;
        ResumeButton.Content = signingIn ? "Cancel sign-in" : offerPin ? "Use full sign-in instead" : "Sign in to resume";
        ResumeButton.Style = (Style)FindResource(offerPin ? "SessionSignInLink" : "Primary");
        ResumeButton.IsEnabled = !pinUnlockBusy && (!busy || signingIn);
        if (pinUnlockBusy) LockedSignOutButton.IsEnabled = false;
        UnlockGuidanceText.Text = signingIn ? "Complete sign-in in your browser, or cancel to return."
            : pinUnlockBusy ? "Checking PIN…"
            : offerPin ? "Use your six-digit PIN to unlock this cashier's current session."
            : "PIN unlock is unavailable for this session. Sign in again to resume your saved work.";
    }
    private void RenderLockPrompt(bool focus=false)
    {
        bool changed=lockPrompt.SetLocked(reauthenticationRequired&&!displaySafetyBlocked);
        if(signInCancellation is not null)changed=lockPrompt.Reveal()||changed;
        LockAuthenticationPanel.Visibility=lockPrompt.Visible?Visibility.Visible:Visibility.Collapsed;
        StaffLockScrim.Visibility=LockAuthenticationPanel.Visibility;
        StaffFallbackBrand.Visibility=StaffGalleryImage.Source is null&&!lockPrompt.Visible?Visibility.Visible:Visibility.Collapsed;
        BackToScreensaverButton.IsEnabled=signInCancellation is null;
        if(IsLoaded&&(focus||changed)&&lockPrompt.Locked)
        {
            // Focus the panel, not Resume: the wake gesture must never start authentication.
            if(lockPrompt.Visible)LockAuthenticationPanel.Focus();else SessionLockOverlay.Focus();
        }
    }
    private bool RevealLockPrompt()
    {
        lockPrompt.SetLocked(reauthenticationRequired&&!displaySafetyBlocked);
        if(!lockPrompt.Reveal())return false;
        RenderLockPrompt(true);
        QueuePinUnlockPrompt();
        return true;
    }
    private bool HandleLockPromptKey(KeyEventArgs e)
    {
        lockPrompt.SetLocked(reauthenticationRequired&&!displaySafetyBlocked);
        Key key=e.Key==Key.System?e.SystemKey:e.Key;
        bool wasVisible = lockPrompt.Visible;
        if(!lockPrompt.KeyDown((int)key,key==Key.Escape,signInCancellation is not null))return false;
        if (!wasVisible && lockPrompt.Visible) openPinAfterWakeKey = true;
        RenderLockPrompt(true);return true;
    }
    private void QueuePinUnlockPrompt()
    {
        if (!_authentication.CanQuickUnlock || windowCancellation.IsCancellationRequested) return;
        int generation = receiptSessionGeneration;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (generation == receiptSessionGeneration && lockPrompt.Visible)
                PinUnlock_Click(this, new RoutedEventArgs());
        }, DispatcherPriority.Background, windowCancellation.Token);
    }
    private void BackToScreensaver_Click(object sender,RoutedEventArgs e)
    {
        if(lockPrompt.Hide(signInCancellation is not null))RenderLockPrompt(true);
    }
}
