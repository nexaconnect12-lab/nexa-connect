using NexaConnect.POS;
namespace NexaConnect.UnitTests;
public sealed class PosSessionPinTests
{
    [Theory]
    [InlineData("")][InlineData("12345")][InlineData("1234567")][InlineData("１２３４５６")][InlineData("12345a")]
    public void Only_six_ascii_digits_are_accepted(string value) => Assert.Throws<ArgumentException>(() => new PosSessionPin(value));
    [Fact]
    public void Success_resets_failures_but_disposal_and_five_failures_are_terminal()
    {
        using var pin = new PosSessionPin("826491");
        for(int i=0;i<4;i++) Assert.False(pin.Verify("not a PIN"));
        Assert.True(pin.Verify("826491"));
        for(int i=0;i<5;i++) Assert.False(pin.Verify("not a PIN"));
        Assert.False(pin.Available); Assert.False(pin.Verify("826491"));
    }
}
