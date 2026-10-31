using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NexaConnect.POS;

/// <summary>Window placement only. Owns no APIs, authentication or financial state.</summary>
internal sealed class PosDisplayCoordinator : IDisposable
{
    private readonly Window staff;
    private readonly Action blocked;
    private readonly DisplayTopology topology = new();
    private readonly HwndSource source;
    private readonly IPosDisplayPlatform platform;
    private CustomerDisplayWindow? customer;
    private bool placing;
    private bool closing;
    private volatile bool disposed;
    private StaffFullscreen? staffFullscreen;
    private sealed record StaffFullscreen(ScreensaverPlacement Placement,DisplayBounds Bounds,WindowStyle Style,
        ResizeMode Resize,WindowState State,double MinWidth,double MinHeight);
    private CustomerDisplaySnapshot snapshot = CustomerDisplaySnapshot.Welcome;

    public PosDisplayCoordinator(Window staff, Action blocked, IPosDisplayPlatform platform)
    {
        this.staff = staff; this.blocked = blocked; this.platform = platform;
        source = HwndSource.FromHwnd(new WindowInteropHelper(staff).Handle);
        source.AddHook(WindowMessage);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanging += Changing;
    }
    public bool Active => topology.Active;
    public void StaffScreensaver(bool locked)
    {
        if(locked)
        {
            if(staffFullscreen is not null||topology.Blocked||staff.WindowState==WindowState.Minimized)return;
            var monitors=platform.Read();
            if(Active&&!topology.IsCurrent(monitors)){Trip();return;}
            var bounds=platform.WindowBounds(source.Handle);
            var placement=ScreensaverPlacement.Create(monitors,bounds,Active?topology.Staff!.Device:null);
            if(placement is null)return; // Ambiguous/overlapping monitors never enlarge staff content.
            staffFullscreen=new(placement,bounds,staff.WindowStyle,staff.ResizeMode,staff.WindowState,staff.MinWidth,staff.MinHeight);
            placing=true;
            try
            {
                staff.WindowState=WindowState.Normal;staff.MinWidth=0;staff.MinHeight=0;
                staff.ResizeMode=ResizeMode.NoResize;staff.WindowStyle=WindowStyle.None;
                platform.Place(source.Handle,placement.Monitor.Bounds);
                if(!placement.Allows(platform.WindowBounds(source.Handle),platform.Read()))Trip();
            }
            catch{Trip();}
            finally{placing=false;}
        }
        else if(staffFullscreen is { } saved)
        {
            staffFullscreen=null;placing=true;
            try
            {
                staff.WindowState=WindowState.Normal;staff.WindowStyle=saved.Style;staff.ResizeMode=saved.Resize;
                if(!topology.Blocked&&saved.Placement.IsCurrent(platform.Read()))
                {platform.Place(source.Handle,saved.Bounds);staff.WindowState=saved.State;}
                staff.MinWidth=saved.MinWidth;staff.MinHeight=saved.MinHeight;
            }
            catch{Trip();}
            finally{placing=false;}
        }
    }
    public bool Apply(string staffDevice, string customerDevice)
    {
        if (topology.Blocked) return false;
        var monitors = platform.Read();
        var candidate = new DisplayTopology();
        if (!candidate.Assign(monitors, staffDevice, customerDevice)) return false;
        // Validate before closing an existing display. All geometry is physical pixels;
        // SetWindowPos avoids treating device pixel coordinates as WPF DIPs.
        CloseCustomer();
        topology.Assign(monitors, staffDevice, customerDevice);
        placing = true;
        try
        {
            staff.WindowState = WindowState.Normal;
            platform.Place(source.Handle, topology.Staff!.WorkArea);
            if (!topology.AllowsStaff(platform.WindowBounds(source.Handle), platform.Read()))
                throw new InvalidOperationException("Staff placement outside assigned monitor.");
            customer = new CustomerDisplayWindow(platform);
            customer.Closed += CustomerClosed;
            customer.DisplayConfigurationChanged += (_, _) => { if (!placing && !closing) Trip(); };
            customer.Present(snapshot);
            var handle = new WindowInteropHelper(customer).EnsureHandle();
            platform.Place(handle, topology.Customer!.Bounds);
            customer.Show();
            platform.Place(handle, topology.Customer.Bounds);
        }
        catch { Trip(); return false; }
        finally { placing = false; }
        if (!topology.IsCurrent(platform.Read()) || !topology.AllowsStaff(platform.WindowBounds(source.Handle), platform.Read()))
        { Trip(); return false; }
        return true;
    }
    public void Present(CustomerDisplaySnapshot next)
    {
        snapshot = next;
        if (topology.Blocked) return;
        try { customer?.Present(next); }
        catch { Trip(); }
    }
    public void Disable() { CloseCustomer(); topology.Disable(); }
    public void Screensaver(System.Windows.Media.Imaging.BitmapSource? image,string title,string caption,bool locked)
    {
        if(topology.Blocked)return;
        try{customer?.Screensaver(image,title,caption,locked);}catch{Trip();}
    }
    private void CloseCustomer()
    {
        if (customer is null) return;
        closing = true;
        try {
            customer.Hide();
            customer.Present(CustomerDisplaySnapshot.Welcome);
            customer.Close();
        }
        finally { customer = null; closing = false; }
    }
    private void CustomerClosed(object? sender, EventArgs e) { if (!closing) { customer = null; Trip(); } }
    private void Changing(object? sender, EventArgs e)
    {
        if(disposed||staff.Dispatcher.HasShutdownStarted)return;
        try {_ = staff.Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=>{
            if(!disposed&&!staff.Dispatcher.HasShutdownStarted&&(Active||staffFullscreen is not null))Trip();
        }));}
        catch(InvalidOperationException){ /* Dispatcher shutdown raced the notification. */ }
    }
    private void Trip()
    {
        if (disposed||topology.Blocked) return;
        topology.Block();
        // Hide before any fallback placement, then expose only the neutral safety view.
        staff.Opacity = 0;
        try { CloseCustomer(); }
        finally
        {
            try { blocked(); }
            finally { staff.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => staff.Opacity = 1); }
        }
    }
    private nint WindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((!Active&&staffFullscreen is null) || topology.Blocked || placing) return 0;
        try
        {
            if (message is 0x007E or 0x02E0) { Trip(); return 0; } // topology / DPI
            if (message == 0x0046)
            {
                var position = Marshal.PtrToStructure<WindowsDisplays.WindowPosition>(lParam);
                var old = platform.WindowBounds(handle);
                int x = (position.Flags & 0x0002) != 0 ? old.Left : position.X;
                int y = (position.Flags & 0x0002) != 0 ? old.Top : position.Y;
                int width = (position.Flags & 0x0001) != 0 ? old.Width : position.Width;
                int height = (position.Flags & 0x0001) != 0 ? old.Height : position.Height;
                var proposed=new DisplayBounds(x,y,x+width,y+height);var monitors=platform.Read();
                if (!(Active?topology.AllowsStaff(proposed,monitors):staffFullscreen?.Placement.Allows(proposed,monitors)==true))
                {
                    position.Flags |= 0x0001 | 0x0002; // Reject unsafe move/resize before it occurs.
                    Marshal.StructureToPtr(position, lParam, false);
                    Trip();
                }
            }
        }
        catch { Trip(); }
        return 0;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try {Microsoft.Win32.SystemEvents.DisplaySettingsChanging -= Changing;}
        finally {try {source.RemoveHook(WindowMessage);}finally {CloseCustomer();}}
    }
}
