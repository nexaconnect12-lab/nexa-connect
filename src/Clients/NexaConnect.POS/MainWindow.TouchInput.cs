using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private TextBox? touchTarget;
    private static readonly HashSet<string> AmountFields =
        ["OpeningCashTextBox", "ClosingCashTextBox", "MovementAmountTextBox", "RefundAmountTextBox"];

    private void InitializeTouchInput()
    {
        OperationalSurface.AddHandler(Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((_, e) => {
                if (e.NewFocus is TextBox box) ShowTouchInput(box);
                else HideTouchInput();
            }), true);
        OperationalSurface.AddHandler(Mouse.PreviewMouseUpEvent,
            new MouseButtonEventHandler((_, e) => {
                DependencyObject? node = e.OriginalSource as DependencyObject;
                while (node is not null && node is not TextBox && node is not PasswordBox)
                    node = node is FrameworkContentElement content ? content.Parent
                        : node is Visual ? VisualTreeHelper.GetParent(node) : null;
                if (node is TextBox box) ShowTouchInput(box);
                else if (node is PasswordBox && !busy && IsSignedIn()) OpenWindowsKeyboard();
            }), true);
        InputKeyboard.KeyPressed += EditTouchInput;
        InputKeyboard.Dismissed += HideTouchInput;
        Deactivated += (_, _) => HideTouchInput();
    }

    private void ShowTouchInput(TextBox box)
    {
        if (!viewInitialized || busy || !IsSignedIn() || displaySafetyBlocked || box.IsReadOnly || !box.IsEnabled || !box.IsVisible) return;
        if (touchTarget == box && InputKeyboard.Visibility == Visibility.Visible) return;
        touchTarget = box;
        string label = System.Windows.Automation.AutomationProperties.GetName(box);
        InputKeyboard.Configure(string.IsNullOrWhiteSpace(label) ? "Text entry" : label, AmountFields.Contains(box.Name));
        InputKeyboard.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => {
            if (touchTarget == box && InputKeyboard.Visibility == Visibility.Visible) box.BringIntoView();
        }));
    }

    private void HideTouchInput()
    {
        touchTarget = null;
        InputKeyboard.Visibility = Visibility.Collapsed;
    }

    private void EditTouchInput(string key)
    {
        var box = touchTarget;
        if (box is null || busy || !IsSignedIn() || displaySafetyBlocked || !IsActive || !box.IsVisible || !box.IsEnabled || box.IsReadOnly)
        { HideTouchInput(); return; }
        int start = box.SelectionStart, length = box.SelectionLength;
        if (key == "Clear") { start = 0; length = box.Text.Length; key = ""; }
        if (key == "⌫")
        {
            if (length == 0) { int previous = TouchInputEdit.PreviousElement(box.Text, start); length = start - previous; start = previous; }
            key = "";
        }
        bool amount = AmountFields.Contains(box.Name);
        string separator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (amount && key == ".") key = separator;
        var next = TouchInputEdit.Replace(box.Text, start, length, key, box.MaxLength, amount, separator);
        if (next is null) return;
        box.Text = next;
        box.Select(start + key.Length, 0);
        box.BringIntoView();
    }

    private void WindowsKeyboard_Click(object sender, RoutedEventArgs e) => OpenWindowsKeyboard();

    private void CancellationPreset_Click(object sender, RoutedEventArgs e)
    {
        if (!busy && IsSignedIn() && CancellationReasonTextBox.IsEnabled && sender is Button { Content: string reason }
            && reason is "Customer request" or "Item unavailable")
            CancellationReasonTextBox.Text = reason;
    }

    private void OpenWindowsKeyboard()
    {
        // Windows owns external browser credential entry. Never send keys or capture input.
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "osk.exe");
            if (!System.IO.File.Exists(path) || Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }) is null)
                throw new InvalidOperationException();
        }
        catch
        {
            ReportWindowsKeyboardFailure();
        }
    }

    private void ReportWindowsKeyboardFailure()
    {
        const string guidance = "Windows keyboard could not open. Use Windows Settings → Accessibility → Keyboard → On-screen keyboard, then retry sign-in.";
        StatusText.Text = guidance;
        if (reauthenticationRequired)
        {
            sessionLockMessage = guidance;
            LockStatusText.Text = guidance;
        }
    }
}
