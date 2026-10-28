using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public class PosDensityTests
{
    private static string Root() => Path.Combine(Path.GetTempPath(), "NexaConnect-DensityTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, PosDisplayDensity.Standard)]
    [InlineData(true, PosDisplayDensity.Standard)]
    [InlineData(false, PosDisplayDensity.Touch)]
    [InlineData(true, PosDisplayDensity.Touch)]
    public void Both_preferences_round_trip_independently(bool keyboard, PosDisplayDensity density)
    {
        var path = Root(); var preferences = new PosUiPreferences(path);
        preferences.Save(new(keyboard, density));
        Assert.Equal(new PosUiOptions(keyboard, density), new PosUiPreferences(path).Load());
        preferences.SaveTouchFirst(!keyboard);
        Assert.Equal(new PosUiOptions(!keyboard, density), preferences.Load());
    }

    [Fact]
    public void Legacy_file_retains_keyboard_choice_and_defaults_density_to_standard()
    {
        var path = Root(); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "input.json"), "{\"Version\":1,\"TouchFirst\":true}");
        Assert.Equal(new PosUiOptions(true), new PosUiPreferences(path).Load());
    }

    [Fact]
    public void Unknown_density_uses_standard_without_discarding_valid_keyboard_preference()
    {
        var path = Root(); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "input.json"), "{\"Version\":1,\"TouchFirst\":true,\"Density\":\"Unknown\"}");
        Assert.Equal(new PosUiOptions(true), new PosUiPreferences(path).Load());
    }

    [Fact]
    public void Invalid_density_cannot_overwrite_saved_settings()
    {
        var preferences = new PosUiPreferences(Root()); preferences.Save(new(true, PosDisplayDensity.Touch));
        Assert.Throws<ArgumentOutOfRangeException>(() => preferences.Save(new(false, (PosDisplayDensity)99)));
        Assert.Equal(new PosUiOptions(true, PosDisplayDensity.Touch), preferences.Load());
    }

    [Fact]
    public void Standard_is_default_and_touch_has_larger_targets()
    {
        Assert.Equal(new PosUiOptions(), new PosUiPreferences(Root()).Load());
        var standard = PosDensityMetrics.For(PosDisplayDensity.Standard); var touch = PosDensityMetrics.For(PosDisplayDensity.Touch);
        Assert.True(touch.ControlHeight >= 48 && touch.ControlHeight > standard.ControlHeight);
        Assert.True(touch.BodyFontSize > standard.BodyFontSize && touch.MenuWidth > standard.MenuWidth);
    }

    [Theory]
    [InlineData(960, 640, true, true)]
    [InlineData(1024, 768, true, false)]
    [InlineData(1280, 800, false, false)]
    [InlineData(1536, 864, false, false)]
    public void Responsive_policy_has_explicit_logical_size_breakpoints(double width, double height, bool stacked, bool compact)
    {
        Assert.Equal(stacked, PosResponsiveLayout.StackCash(width));
        Assert.Equal(compact, PosResponsiveLayout.CompactHeight(height));
    }
}
