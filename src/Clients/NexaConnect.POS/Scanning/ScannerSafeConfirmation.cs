using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace NexaConnect.POS;

public sealed class ScannerSafeConfirmation : Window
{
    public static bool AllowsKey(Key key)=>key is Key.Tab or Key.Space or Key.Escape;
    public ScannerSafeConfirmation(Window owner,string message,string title)
    {
        Owner=owner;Title=title;Width=600;MaxWidth=Math.Max(300,SystemParameters.WorkArea.Width-40);
        MaxHeight=Math.Max(300,SystemParameters.WorkArea.Height-40);SizeToContent=SizeToContent.Height;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
        var stack=new StackPanel{Margin=new Thickness(24)};
        stack.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,FontSize=16,Margin=new Thickness(0,0,0,16)});
        stack.Children.Add(new TextBlock{Text="Scan mode: use a deliberate click/tap, or Tab then Space to confirm. Scanner Enter cannot confirm.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        var buttons=new WrapPanel();
        var cancel=new Button{Content="Cancel",MinHeight=52,MinWidth=120,Margin=new Thickness(0,0,12,0)};
        var confirm=new Button{Content="Confirm",MinHeight=52,MinWidth=120};
        cancel.Click+=(_,_)=>DialogResult=false;confirm.Click+=(_,_)=>DialogResult=true;
        buttons.Children.Add(cancel);buttons.Children.Add(confirm);stack.Children.Add(buttons);
        Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,PanningMode=PanningMode.VerticalOnly};
        Loaded+=(_,_)=>cancel.Focus();
        PreviewTextInput+=(_,e)=>e.Handled=true;
        PreviewKeyDown+=(_,e)=>{if(!AllowsKey(e.Key)||e.IsRepeat||(Keyboard.Modifiers&(ModifierKeys.Alt|ModifierKeys.Control))!=0)e.Handled=true;else if(e.Key==Key.Escape){e.Handled=true;DialogResult=false;}};
    }
    public static bool Ask(Window owner,string message,string title,Func<bool> current)
    {
        var dialog=new ScannerSafeConfirmation(owner,message,title);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
        timer.Tick+=(_,_)=>{if(!current())dialog.DialogResult=false;};
        try{if(!current())return false;timer.Start();return dialog.ShowDialog()==true&&current();}
        finally{timer.Stop();}
    }
}
