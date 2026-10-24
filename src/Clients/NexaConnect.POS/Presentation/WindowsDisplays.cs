using System.Runtime.InteropServices;

namespace NexaConnect.POS;

internal sealed class WindowsDisplays : IPosDisplayPlatform
{
    public IReadOnlyList<PosMonitor> Read()
    {
        var result = new List<PosMonitor>();
        MonitorCallback callback = (nint monitor, nint dc, ref Rect rect, nint data) => {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return false;
            result.Add(new(info.Device, Bounds(info.Monitor), Bounds(info.Work), (info.Flags & 1) != 0));
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0)) throw new InvalidOperationException("Display enumeration failed.");
        return result;
    }
    public DisplayBounds WindowBounds(nint handle)
    {
        if (!GetWindowRect(handle, out var rect)) throw new InvalidOperationException("Display placement unavailable.");
        return Bounds(rect);
    }
    public void Place(nint handle, DisplayBounds bounds)
    {
        if (!SetWindowPos(handle, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0010 | 0x0004))
            throw new InvalidOperationException("Display placement failed.");
    }
    public void MakeCustomerReadOnly(nint handle, Action displayChanged)
    {
        // WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW; also reject mouse activation.
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new nint(style | 0x08000000 | 0x00000020 | 0x00000080));
        System.Windows.Interop.HwndSource.FromHwnd(handle)?.AddHook((nint h, int m, nint w, nint l, ref bool handled) => {
            if (m == 0x0021) { handled = true; return new nint(3); } // MA_NOACTIVATE
            if (m is 0x007E or 0x02E0) displayChanged();
            return 0;
        });
    }
    private static DisplayBounds Bounds(Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct WindowPosition
    {
        public nint Window, InsertAfter; public int X, Y, Width, Height; public uint Flags;
    }
    private delegate bool MonitorCallback(nint monitor, nint dc, ref Rect rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
