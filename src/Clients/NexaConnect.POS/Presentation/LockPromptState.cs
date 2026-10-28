namespace NexaConnect.POS;

/// <summary>Presentation only: revealing a prompt never grants an authenticated workspace.</summary>
public sealed class LockPromptState
{
    public bool Locked { get; private set; }
    public bool Visible { get; private set; }
    private int? wakeKey;
    public bool SetLocked(bool locked)
    {
        if(Locked==locked)return false;
        Locked=locked;Visible=false;wakeKey=null;return true;
    }
    public bool Reveal()
    {
        if(!Locked||Visible)return false;
        Visible=true;return true;
    }
    public bool KeyDown(int key,bool escape,bool authenticationInProgress)
    {
        if(!Locked)return false;
        if(wakeKey.HasValue)return true; // Consume held/repeated wake gestures through key-up.
        if(!Visible){Visible=true;wakeKey=key;return true;}
        if(escape){if(!authenticationInProgress)Visible=false;wakeKey=key;return true;}
        return false;
    }
    public bool KeyUp(int key)
    {
        if(!Locked||!wakeKey.HasValue)return false;
        if(wakeKey==key)wakeKey=null;
        return true;
    }
    public bool Hide(bool authenticationInProgress)
    {
        if(!Locked||authenticationInProgress)return false;
        Visible=false;return true;
    }
}
