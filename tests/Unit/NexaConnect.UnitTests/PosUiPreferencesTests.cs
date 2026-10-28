using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public class PosUiPreferencesTests
{
    private static string DirectoryForTest() => Path.Combine(Path.GetTempPath(), "NexaConnect-InputTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void New_terminal_defaults_to_keyboard_and_mouse_without_writing_files()
    {
        var path = DirectoryForTest();
        Assert.False(new PosUiPreferences(path).LoadTouchFirst());
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Explicit_touch_choice_survives_new_instance_and_can_be_disabled()
    {
        var path = DirectoryForTest(); var preferences = new PosUiPreferences(path);
        preferences.SaveTouchFirst(true);
        Assert.True(new PosUiPreferences(path).LoadTouchFirst());
        preferences.SaveTouchFirst(false);
        Assert.False(new PosUiPreferences(path).LoadTouchFirst());
        Assert.Single(Directory.GetFiles(path));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"Version\":2,\"TouchFirst\":true}")]
    [InlineData("{\"Version\":1,\"TouchFirst\":\"true\"}")]
    [InlineData("{}")]
    [InlineData("null")]
    public void Invalid_or_unsupported_preferences_fall_back_to_off(string content)
    {
        var path = DirectoryForTest(); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "input.json"), content);
        Assert.False(new PosUiPreferences(path).LoadTouchFirst());
    }

    [Fact]
    public void Oversized_file_falls_back_to_off()
    {
        var path = DirectoryForTest(); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "input.json"), new string(' ', 1025));
        Assert.False(new PosUiPreferences(path).LoadTouchFirst());
    }

    [Fact]
    public void Failed_save_preserves_the_last_valid_choice()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows file replacement/share semantics.
        var path = DirectoryForTest(); var preferences = new PosUiPreferences(path);
        preferences.SaveTouchFirst(true);
        var file = Path.Combine(path, "input.json");
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Record.Exception(() => preferences.SaveTouchFirst(false));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.True(preferences.LoadTouchFirst());
        Assert.Single(Directory.GetFiles(path));
    }

    [Fact]
    public void Separate_terminals_or_windows_profiles_do_not_share_choices()
    {
        var root = DirectoryForTest();
        var config = new PosClientConfiguration("https://id.example/realms/test", "nexaconnect-pos", "nexaconnect-pos://oauth/callback", "openid",
            "https://pos.example/", "https://order.example/", Guid.NewGuid(), Guid.NewGuid(), "THB", "cash_manual", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var one = PosUiPreferences.ForTerminal(config, root);
        one.SaveTouchFirst(true);
        Assert.True(PosUiPreferences.ForTerminal(config, root).LoadTouchFirst());
        foreach (var other in new[] { config with { OrganizationId = Guid.NewGuid() }, config with { RestaurantId = Guid.NewGuid() },
            config with { BranchId = Guid.NewGuid() }, config with { StoreId = Guid.NewGuid() }, config with { TerminalId = Guid.NewGuid() } })
            Assert.False(PosUiPreferences.ForTerminal(other, root).LoadTouchFirst());
        Assert.False(PosUiPreferences.ForTerminal(config, DirectoryForTest()).LoadTouchFirst());
    }

    [Fact]
    public void Linked_preference_directory_defaults_off_and_refuses_save_without_changing_target()
    {
        var root = DirectoryForTest(); var target = Path.Combine(root, "target"); var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target); var original = new PosUiPreferences(target); original.SaveTouchFirst(true);
        if (OperatingSystem.IsWindows())
        {
            // Junction creation needs no elevation; only these freshly owned temporary paths are used.
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, target);
        var linked = new PosUiPreferences(link);
        Assert.False(linked.LoadTouchFirst());
        Assert.Throws<InvalidDataException>(() => linked.SaveTouchFirst(false));
        Assert.True(original.LoadTouchFirst());
    }
}
