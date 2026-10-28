using System.Windows;
using System.Windows.Input;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly LockPromptState lockPrompt=new();
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
        RenderLockPrompt(true);return true;
    }
    private bool HandleLockPromptKey(KeyEventArgs e)
    {
        lockPrompt.SetLocked(reauthenticationRequired&&!displaySafetyBlocked);
        Key key=e.Key==Key.System?e.SystemKey:e.Key;
        if(!lockPrompt.KeyDown((int)key,key==Key.Escape,signInCancellation is not null))return false;
        RenderLockPrompt(true);return true;
    }
    private void BackToScreensaver_Click(object sender,RoutedEventArgs e)
    {
        if(lockPrompt.Hide(signInCancellation is not null))RenderLockPrompt(true);
    }
}
