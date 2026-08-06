using System.Windows;

namespace NexaConnect.POS;

public partial class MainWindow : Window
{
    private readonly PosAuthentication _authentication;

    public MainWindow(PosAuthentication authentication)
    {
        InitializeComponent();
        _authentication = authentication;
        _authentication.StatusChanged += OnStatusChanged;
        StatusText.Text = "Ready to sign in.";
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Opening secure sign-in…";
            await _authentication.SignInAsync();
            StatusText.Text = "Signed in. POS session is ready.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Sign-in was cancelled.";
        }
        catch (Exception)
        {
            StatusText.Text = "Sign-in failed. Check the identity service and try again.";
        }
    }

    private void OnStatusChanged(object? sender, string status) =>
        Dispatcher.Invoke(() => StatusText.Text = status);

    protected override void OnClosed(EventArgs e)
    {
        _authentication.StatusChanged -= OnStatusChanged;
        base.OnClosed(e);
    }
}
