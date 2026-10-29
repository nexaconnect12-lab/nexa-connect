using System.Windows;
using System.Windows.Controls;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private PosDisplayDensity displayDensity;
    private bool updatingDensity;

    private void Density_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!viewInitialized || updatingDensity) return;
        var requested = DensityPicker.SelectedIndex == 1 ? PosDisplayDensity.Touch : PosDisplayDensity.Standard;
        try
        {
            if (busy || !IsSignedIn() || reauthenticationRequired || displaySafetyBlocked) return;
            inputPreferences.Save(new(touchFirst, requested));
            displayDensity = requested;
            ApplyDisplayDensity();
        }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
        {
            DensityStatusText.Text = "Display density could not be saved. The previous layout is still active. Check local folder access and retry.";
        }
        finally
        {
            updatingDensity = true;
            try { DensityPicker.SelectedIndex = displayDensity == PosDisplayDensity.Touch ? 1 : 0; }
            finally { updatingDensity = false; }
        }
    }

    private void ApplyDisplayDensity()
    {
        var metrics = PosDensityMetrics.For(displayDensity);
        Resources["PosControlHeight"] = metrics.ControlHeight;
        Resources["PosBodyFontSize"] = metrics.BodyFontSize;
        Resources["PosButtonPadding"] = new Thickness(metrics.ButtonHorizontalPadding, metrics.ButtonVerticalPadding, metrics.ButtonHorizontalPadding, metrics.ButtonVerticalPadding);
        Resources["PosFieldPadding"] = new Thickness(metrics.FieldPadding);
        Resources["PosCardPadding"] = new Thickness(metrics.CardPadding);
        Resources["PosMenuWidth"] = metrics.MenuWidth;
        Resources["PosMenuHeight"] = metrics.MenuHeight;
        DensityStatusText.Text = displayDensity == PosDisplayDensity.Touch
            ? "Touch density: larger controls and spacing. Automatic keyboards are controlled separately above."
            : "Standard density (default): efficient keyboard/mouse layout. Automatic keyboards are controlled separately above.";
        UpdateResponsiveLayout();
        RenderTerminalOverview();
    }

    private void DensityWindow_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();

    private void UpdateResponsiveLayout()
    {
        if (ShiftCashLayout is null || WorkflowDock is null) return;
        double width = ActualWidth > 0 ? ActualWidth : Width;
        bool stack = PosResponsiveLayout.StackCash(width);
        ShiftCashLayout.ColumnDefinitions[1].Width = stack ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(CashReconciliationCard, stack ? 0 : 1);
        Grid.SetRow(CashReconciliationCard, stack ? 1 : 0);
        ShiftSetupCard.Margin = stack ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 16, 0);
        double height = ActualHeight > 0 ? ActualHeight : Height;
        bool compact = PosResponsiveLayout.CompactHeight(height);
        InputViewport.Margin = compact ? new Thickness(12, 8, 12, 8) : new Thickness(24);
        WorkflowDock.Margin = compact ? new Thickness(12, 0, 12, 8) : new Thickness(24, 0, 24, 8);
        WorkflowAmountColumn.Width = new GridLength(stack ? 180 : 230);
        WorkflowDescription.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SessionGuidanceText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        WorkflowStatusScroll.MaxHeight = compact ? 48 : 54;
        if (touchTarget is { } field && InputKeyboard.Visibility == Visibility.Visible)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => {
                if (touchTarget == field && InputKeyboard.Visibility == Visibility.Visible) field.BringIntoView();
            }));
    }
}
