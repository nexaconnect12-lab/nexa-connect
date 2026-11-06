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
    private bool customerRefitScheduled;
    private volatile bool disposed;
    private StaffDisplayPlacement? staffPlacement;
    private CustomerDisplaySnapshot snapshot = CustomerDisplaySnapshot.Welcome;

    public PosDisplayCoordinator(Window staff, Action blocked, IPosDisplayPlatform platform)
    {
        this.staff = staff; this.blocked = blocked; this.platform = platform;
        source = HwndSource.FromHwnd(new WindowInteropHelper(staff).Handle);
        source.AddHook(WindowMessage);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanging += Changing;
    }
    public bool Active => topology.Active;
    public (string Staff, string Customer)? ActiveRoles => Active
        ? (topology.Staff!.Device, topology.Customer!.Device) : null;
    public void InitializeStaffFullscreen()
    {
        if (staffPlacement is not null || topology.Blocked) return;
        placing = true;
        try
        {
            var monitors = platform.Read();
            PlaceStaff(monitors, monitors.SingleOrDefault(monitor => monitor.Primary)?.Device);
        }
        catch { Trip(); }
        finally { placing = false; }
    }
    private void PlaceStaff(IReadOnlyList<PosMonitor> monitors, string? device)
    {
        staffPlacement = StaffDisplayPlacement.Create(monitors, platform.WindowBounds(source.Handle), device)
            ?? throw new InvalidOperationException("Staff monitor placement is ambiguous.");
        var target = staffPlacement.Monitor.Bounds;
        staff.WindowState = WindowState.Normal;
        platform.Place(source.Handle, target);
        if (!StaffPlacementIsCurrent(platform.Read()))
            throw new InvalidOperationException("Staff window could not fill its monitor.");
    }
    public bool Restore(PosDisplayRoles saved)
    {
        var monitors = platform.Read();
        return saved.Matches(monitors) && Apply(monitors, saved.StaffDevice, saved.CustomerDevice);
    }
    public bool Apply(PosMonitor staffMonitor, PosMonitor customerMonitor, out PosDisplayRoles? saved)
    {
        saved = null;
        var monitors = platform.Read();
        if (!monitors.Contains(staffMonitor) || !monitors.Contains(customerMonitor)
            || !Apply(monitors, staffMonitor.Device, customerMonitor.Device)) return false;
        if (DisplayTopology.LayoutHash(monitors) is { } hash)
            saved = new(staffMonitor.Device, customerMonitor.Device, hash);
        return true;
    }
    private bool Apply(IReadOnlyList<PosMonitor> monitors, string staffDevice, string customerDevice)
    {
        if (topology.Blocked) return false;
        var candidate = new DisplayTopology();
        if (!candidate.Assign(monitors, staffDevice, customerDevice)) return false;
        // Validate before closing an existing display. All geometry is physical pixels;
        // SetWindowPos avoids treating device pixel coordinates as WPF DIPs.
        CloseCustomer();
        topology.Assign(monitors, staffDevice, customerDevice);
        placing = true;
        try
        {
            PlaceStaff(monitors, staffDevice);
            customer = new CustomerDisplayWindow(platform);
            customer.Closed += CustomerClosed;
            customer.DisplayConfigurationChanged += CustomerGeometryChanged;
            customer.StateChanged += CustomerGeometryChanged;
            customer.LocationChanged += CustomerGeometryChanged;
            customer.SizeChanged += CustomerGeometryChanged;
            customer.Present(snapshot);
            var handle = new WindowInteropHelper(customer).EnsureHandle();
            platform.Place(handle, topology.Customer!.Bounds);
            if (platform.WindowBounds(handle) != topology.Customer.Bounds)
                throw new InvalidOperationException("Customer window is outside its assigned monitor.");
            customer.Show();
            platform.Place(handle, topology.Customer.Bounds);
            if (platform.WindowBounds(handle) != topology.Customer.Bounds)
                throw new InvalidOperationException("Customer window could not fill its monitor.");
        }
        catch { Trip(); return false; }
        finally { placing = false; }
        if (!StaffPlacementIsCurrent(platform.Read()))
        { Trip(); return false; }
        return true;
    }
    public void Present(CustomerDisplaySnapshot next)
    {
        snapshot = next;
        if (topology.Blocked) return;
        try
        {
            if (next.IsWorking) customer?.TableQr(null, null);
            customer?.Present(next);
        }
        catch { Trip(); }
    }
    public bool ShowTableQr(System.Windows.Media.Imaging.BitmapSource image, string tableCode)
    {
        if (!Active || topology.Blocked || customer is null || snapshot.IsWorking) return false;
        try
        {
            if (!StaffPlacementIsCurrent(platform.Read())
                || platform.WindowBounds(new WindowInteropHelper(customer).Handle) != topology.Customer!.Bounds)
                return false;
            customer.TableQr(image, tableCode);
            return true;
        }
        catch { Trip(); return false; }
    }
    public void HideTableQr()
    {
        try { customer?.TableQr(null, null); }
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
    private void CustomerGeometryChanged(object? sender,EventArgs e)
    {
        if(disposed||staff.Dispatcher.HasShutdownStarted||placing||closing||topology.Blocked||customer is null||customerRefitScheduled)return;
        var expected=customer;customerRefitScheduled=true;
        try{_=staff.Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=>{
            customerRefitScheduled=false;
            if(disposed||closing||topology.Blocked||!ReferenceEquals(customer,expected))return;
            try
            {
                var monitors=platform.Read();
                if(!StaffPlacementIsCurrent(monitors)){Trip();return;}
                var handle=new WindowInteropHelper(expected).Handle;
                if(expected.WindowState==WindowState.Normal&&platform.WindowBounds(handle)==topology.Customer!.Bounds)return;
                placing=true;
                expected.WindowState=WindowState.Normal;platform.Place(handle,topology.Customer!.Bounds);
                if(!topology.IsCurrent(platform.Read())||platform.WindowBounds(handle)!=topology.Customer.Bounds
                    ||!StaffPlacementIsCurrent(platform.Read()))Trip();
            }
            catch{Trip();}
            finally{placing=false;}
        }));}catch(InvalidOperationException){customerRefitScheduled=false;}
    }
    private void Changing(object? sender, EventArgs e)
    {
        if(disposed||staff.Dispatcher.HasShutdownStarted)return;
        try {_ = staff.Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=>{
            if(!disposed&&!staff.Dispatcher.HasShutdownStarted&&staffPlacement is not null)Trip();
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
    private bool StaffPlacementIsCurrent(IReadOnlyList<PosMonitor> monitors)
    {
        var bounds = platform.WindowBounds(source.Handle);
        return staffPlacement?.IsFullscreen(bounds, monitors) == true
            && (!Active || topology.AllowsStaff(bounds, monitors));
    }

    private nint WindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (disposed || placing || staffPlacement is not { } placement) return 0;
        try
        {
            // WM_SYSCOMMAND: keep the staff window borderless and fullscreen. Close and
            // system focus switching still use normal Windows behavior.
            if (message == 0x0112 && ((long)wParam & 0xFFF0) is 0xF000 or 0xF010 or 0xF020 or 0xF030 or 0xF120)
            {
                handled = true;
                return 0;
            }
            if (topology.Blocked) return 0;
            if (message is 0x007E or 0x02E0) { Trip(); return 0; } // topology / DPI
            if (message == 0x0046)
            {
                var position = Marshal.PtrToStructure<WindowsDisplays.WindowPosition>(lParam);
                const uint noGeometry = 0x0001 | 0x0002; // SWP_NOSIZE | SWP_NOMOVE
                if ((position.Flags & noGeometry) == noGeometry) return 0;
                if (!placement.IsCurrent(platform.Read()))
                {
                    position.Flags |= noGeometry;
                    Marshal.StructureToPtr(position, lParam, false); Trip(); return 0;
                }
                var target = placement.Monitor.Bounds;
                position.X = target.Left; position.Y = target.Top;
                position.Width = target.Width; position.Height = target.Height;
                position.Flags &= ~noGeometry;
                Marshal.StructureToPtr(position, lParam, false);
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
