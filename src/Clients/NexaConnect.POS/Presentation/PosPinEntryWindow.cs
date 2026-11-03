using System.Windows;
using System.Windows.Controls;

namespace NexaConnect.POS;

// A staff-owned modal. No PIN ever enters general text fields, recovery, logs or the customer projection.
internal sealed class PosPinEntryWindow : Window
{
    private readonly PasswordBox entry = new() { MaxLength = 6, FontSize = 28, Margin = new Thickness(8) };
    public string? Value { get; private set; }
    public PosPinEntryWindow(Window owner, string title)
    {
        Owner = owner; Title = title; Width = 340; Height = 480; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontSize = 20 });
        panel.Children.Add(entry);
        var keys = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        foreach (string label in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Clear", "0", "Back" })
        {
            var button = new Button { Content = label, MinHeight = 48, Margin = new Thickness(3), Focusable = false };
            button.Click += (_, _) => {
                if (label == "Clear") entry.Clear();
                else if (label == "Back") { if (entry.Password.Length > 0) entry.Password = entry.Password[..^1]; }
                else if (entry.Password.Length < 6) entry.Password += label;
            };
            keys.Children.Add(button);
        }
        panel.Children.Add(keys);
        var submit = new Button { Content = "Continue", MinHeight = 48, Margin = new Thickness(3), IsDefault = true };
        submit.Click += (_, _) => { if (PosSessionPin.Valid(entry.Password)) { Value = entry.Password; entry.Clear(); DialogResult = true; } };
        panel.Children.Add(submit);
        panel.Children.Add(new Button { Content = "Cancel", MinHeight = 40, Margin = new Thickness(3), IsCancel = true });
        Content = panel; Loaded += (_, _) => entry.Focus(); Closed += (_, _) => entry.Clear();
    }
    public string? TakeValue() { var value = Value; Value = null; return value; }
}
