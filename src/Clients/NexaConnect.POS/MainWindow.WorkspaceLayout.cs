using System.Windows;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private void WorkspaceOptions_Click(object sender, RoutedEventArgs e)
    {
        bool open = WorkspaceOptionsPanel.Visibility != Visibility.Visible;
        HideTouchInput();
        WorkspaceOptionsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceOptionsButton.Content = open ? "Less" : "More";
        WorkspaceOptionsButton.Focus();
    }

    private void MenuOptions_Collapsed(object sender, RoutedEventArgs e)
    {
        if (!viewInitialized || !ReferenceEquals(e.OriginalSource, MenuOptionsExpander)) return;
        HideTouchInput();
        MenuSearchTextBox.Focus();
    }

    private void MenuArea_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMenuTileWidth();

    private void UpdateMenuTileWidth()
    {
        if (MenuList is null || MenuList.ActualWidth < 100) return;
        double available = Math.Max(100, MenuList.ActualWidth - 22);
        double target = displayDensity == PosDisplayDensity.Touch ? 184 : 172;
        int columns = Math.Max(1, (int)Math.Floor((available + 8) / (target + 8)));
        double width = Math.Floor(Math.Clamp(available / columns - 8, 100, 224));
        if (Resources["PosMenuWidth"] is not double previous || Math.Abs(previous - width) > 0.5)
            Resources["PosMenuWidth"] = width;
    }
}
