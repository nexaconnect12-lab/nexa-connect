using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class LockPromptStateTests
{
    [Fact]
    public void Each_lock_starts_hidden_and_rendering_does_not_hide_an_open_prompt()
    {
        var state=new LockPromptState();Assert.False(state.Visible);
        Assert.True(state.SetLocked(true));Assert.False(state.Visible);
        Assert.True(state.Reveal());Assert.False(state.SetLocked(true));Assert.True(state.Visible);Assert.True(state.Locked);
        state.SetLocked(false);Assert.False(state.Visible);state.SetLocked(true);Assert.False(state.Visible);
    }
    [Fact]
    public void First_pointer_contact_is_consumed_without_unlocking_and_later_contacts_can_use_controls()
    {
        var state=new LockPromptState();state.SetLocked(true);
        Assert.True(state.Reveal());Assert.True(state.Visible);Assert.True(state.Locked);
        Assert.False(state.Reveal());Assert.True(state.Locked);
    }
    [Theory]
    [InlineData(13)][InlineData(32)][InlineData(65)][InlineData(27)]
    public void Wake_key_and_held_repeats_are_consumed_until_release(int key)
    {
        var state=new LockPromptState();state.SetLocked(true);
        Assert.True(state.KeyDown(key,key==27,false));Assert.True(state.Visible);Assert.True(state.Locked);
        Assert.True(state.KeyDown(key,key==27,false));Assert.True(state.Visible);
        Assert.True(state.KeyUp(key));Assert.False(state.KeyUp(key));
        Assert.False(state.KeyDown(13,false,false));Assert.True(state.Locked);
    }
    [Fact]
    public void Wake_chord_is_consumed_until_the_first_key_is_released()
    {
        var state=new LockPromptState();state.SetLocked(true);
        Assert.True(state.KeyDown(65,false,false));Assert.True(state.KeyDown(13,false,false));
        Assert.True(state.KeyUp(13));Assert.True(state.KeyDown(13,false,false));
        Assert.True(state.KeyUp(65));Assert.False(state.KeyDown(13,false,false));Assert.True(state.Locked);
    }
    [Fact]
    public void Escape_hides_but_a_held_escape_does_not_immediately_reopen_the_prompt()
    {
        var state=new LockPromptState();state.SetLocked(true);state.Reveal();
        Assert.True(state.KeyDown(27,true,false));Assert.False(state.Visible);Assert.True(state.Locked);
        Assert.True(state.KeyDown(27,true,false));Assert.False(state.Visible);
        Assert.True(state.KeyUp(27));Assert.True(state.KeyDown(27,true,false));Assert.True(state.Visible);
    }
    [Fact]
    public void Authentication_in_progress_keeps_cancel_controls_visible()
    {
        var state=new LockPromptState();state.SetLocked(true);state.Reveal();
        Assert.True(state.KeyDown(27,true,true));Assert.True(state.Visible);
        Assert.False(state.Hide(true));Assert.True(state.Visible);Assert.True(state.Locked);
    }
    [Fact]
    public void Dismissal_keeps_lock_authority_and_allows_a_later_wake()
    {
        var state=new LockPromptState();state.SetLocked(true);state.Reveal();
        Assert.True(state.Hide(false));Assert.False(state.Visible);Assert.True(state.Locked);
        Assert.True(state.Reveal());Assert.True(state.Locked);
    }
    [Fact]
    public void Unlocked_or_safety_blocked_state_has_no_prompt_or_consumed_keys()
    {
        var state=new LockPromptState();state.SetLocked(true);state.KeyDown(13,false,false);state.SetLocked(false);
        Assert.False(state.Visible);Assert.False(state.KeyUp(13));Assert.False(state.KeyDown(13,false,false));
        Assert.False(state.Reveal());Assert.False(state.Hide(false));Assert.False(state.Locked);
    }
}
