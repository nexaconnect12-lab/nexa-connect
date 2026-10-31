using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NexaConnect.POS;

internal static partial class Program
{
    private static void VerifyCashierNavigation(MainWindow window)
    {
        var tabs=(TabControl)window.FindName("WorkspaceTabs");
        var railBrush=(SolidColorBrush)window.FindResource("PosNavigationCanvas");
        Require(((TabItem)window.FindName("TerminalOverviewTab")).IsSelected,"terminal overview is the initial inner section");
        foreach(var size in new[]{new Size(1000,680),new Size(1366,768),new Size(1920,1080)})
        {
            window.Width=size.Width;window.Height=size.Height;Pump(window);
            foreach(TabItem item in tabs.Items)
            {
                tabs.SelectedItem=item;Pump(window);
                item.BringIntoView();Pump(window);
                Require(item.Header is string label && !string.IsNullOrWhiteSpace(label),"all cashier destinations retain readable labels");
                var host=(ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost",tabs);
                var navigation=(FrameworkElement)tabs.Template.FindName("HeaderPanel",tabs);
                var content=host.TransformToAncestor(tabs).TransformBounds(new Rect(host.RenderSize));
                var rail=navigation.TransformToAncestor(tabs).TransformBounds(new Rect(navigation.RenderSize));
                Require(content.Left>=rail.Right && content.Width>=650 && content.Right<=tabs.ActualWidth+1,
                    "navigation never overlaps or clips the operational content at supported fixture sizes");
                Require(((FrameworkElement)item.Template.FindName("SelectedMarker",item)).Visibility==Visibility.Visible,
                    "selected destination has an additional shape indicator");
                var labelBlock=(TextBlock)item.Template.FindName("TabLabel",item);
                var surface=(Border)item.Template.FindName("TabSurface",item);
                Require(Contrast((SolidColorBrush)labelBlock.Foreground,(SolidColorBrush)surface.Background)>=4.5,
                    "selected navigation label remains readable against its flat surface");
                Require(Contrast((SolidColorBrush)labelBlock.Foreground,(SolidColorBrush)item.Foreground)>=4.5,
                    "light rail labels do not become the inherited operational text color");
                var other=tabs.Items.Cast<TabItem>().First(candidate=>candidate!=item);
                other.ApplyTemplate();
                Require(Contrast((SolidColorBrush)((TextBlock)other.Template.FindName("TabLabel",other)).Foreground,railBrush)>=4.5,
                    "unselected navigation labels retain text contrast against the dark rail");
            }
        }
        tabs.SelectedIndex=0;
        window.Width=1280;window.Height=850;
        ((ScrollViewer)window.FindName("InputViewport")).ScrollToTop();Pump(window);
        var picker=(ComboBox)window.FindName("StationComboBox");
        picker.ApplyTemplate();
        var popup=(Popup)picker.Template.FindName("PART_Popup",picker);
        popup.Opacity=0; // Keep the synthetic popup invisible to the workstation user.
        picker.IsDropDownOpen=true;Pump(window);
        Require(popup.IsOpen && popup.Child is FrameworkElement child && child.ActualWidth>=picker.ActualWidth,
            "restyled selection popup opens with the field width");
        picker.IsDropDownOpen=false;Pump(window);
    }
    private static double Contrast(SolidColorBrush first,SolidColorBrush second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value){double channel=value/255d;return channel<=0.04045?channel/12.92:Math.Pow((channel+0.055)/1.055,2.4);}
            return 0.2126*Linear(color.R)+0.7152*Linear(color.G)+0.0722*Linear(color.B);
        }
        double a=Luminance(first.Color),b=Luminance(second.Color);
        return (Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05);
    }
    private static void PreviewCashierShell(MainWindow window)
    {
        var menu=(IList)typeof(MainWindow).GetField("menu",Private)!.GetValue(window)!;
        var cart=(IList)typeof(MainWindow).GetField("cart",Private)!.GetValue(window)!;
        menu.Clear();cart.Clear();
        var samples=new[]{new PosMenuItem(Guid.NewGuid(),"Classic chicken rice",65m,"THB","Kitchen",true),
            new PosMenuItem(Guid.NewGuid(),"Basil chicken",75m,"THB","Kitchen",true),new PosMenuItem(Guid.NewGuid(),"Vegetable rice",60m,"THB","Kitchen",true),
            new PosMenuItem(Guid.NewGuid(),"Iced Thai tea",35m,"THB","Drinks",true),new PosMenuItem(Guid.NewGuid(),"Lime soda",30m,"THB","Drinks",true),
            new PosMenuItem(Guid.NewGuid(),"Bottled water",15m,"THB","Drinks",false)};
        foreach(var sample in samples)menu.Add(sample);
        var line=typeof(MainWindow).GetNestedType("CartLine",Private)!;
        foreach(var item in new[]{samples[0],samples[3]})cart.Add(Activator.CreateInstance(line,Private|BindingFlags.Public,null,[item],null)!);
        typeof(MainWindow).GetField("_activeShift",Private)!.SetValue(window,new LocalShiftState(Guid.NewGuid(),"PREVIEW",DateTimeOffset.UtcNow));
        typeof(MainWindow).GetField("cashSessionId",Private)!.SetValue(window,Guid.NewGuid());
        Invoke(window,"UpdateCartTotal");Invoke(window,"UpdateOperationalState");Invoke(window,"HideTouchInput");
        window.Width=1366;window.Height=768;
        ((TabControl)window.FindName("WorkspaceTabs")).SelectedIndex=0;
        ((TextBlock)window.FindName("StatusText")).Text="Layout preview with synthetic items. No order has been submitted.";
        ((ScrollViewer)window.FindName("InputViewport")).ScrollToTop();Pump(window);
        SavePreview(window,".runstate/pos-win11-cashier-preview.png");
    }
}
