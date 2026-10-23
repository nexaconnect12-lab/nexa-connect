using System.Windows;
using System.Windows.Interop;

namespace NexaConnect.POS;

public partial class CustomerDisplayWindow : Window
{
    private readonly IPosDisplayPlatform platform;
    public event EventHandler? DisplayConfigurationChanged;
    public CustomerDisplayWindow() : this(new WindowsDisplays()) { }
    internal CustomerDisplayWindow(IPosDisplayPlatform platform)
    { this.platform = platform; InitializeComponent(); DataContext = CustomerDisplaySnapshot.Welcome; }
    public void Present(CustomerDisplaySnapshot snapshot) => DataContext = snapshot;
    public void Screensaver(System.Windows.Media.Imaging.BitmapSource? image,string title,string caption,bool locked)
    {
        bool show=locked&&image is not null;
        SaverImage.Source=show?image:null;SaverTitle.Text=show?title:"";SaverCaption.Text=show?caption:"";
        SaverView.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        TransactionView.Visibility=show?Visibility.Collapsed:Visibility.Visible;
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        platform.MakeCustomerReadOnly(new WindowInteropHelper(this).Handle,
            () => DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty));
    }
}
