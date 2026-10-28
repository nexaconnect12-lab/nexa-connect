namespace NexaConnect.POS;

/// <summary>Physical-pixel monitor lease for a staff screensaver; never spans displays.</summary>
public sealed class ScreensaverPlacement
{
    private readonly PosMonitor[] monitors;
    public PosMonitor Monitor { get; }
    private ScreensaverPlacement(PosMonitor monitor, IReadOnlyList<PosMonitor> current)
    { Monitor=monitor;monitors=current.ToArray(); }
    public static ScreensaverPlacement? Create(IReadOnlyList<PosMonitor> monitors, DisplayBounds window, string? assignedStaff)
    {
        if(window.Width<=0||window.Height<=0||monitors.Count==0||monitors.Select(m=>m.Device).Distinct().Count()!=monitors.Count
            ||monitors.Any(m=>m.Bounds.Width<=0||m.Bounds.Height<=0||m.WorkArea.Width<=0||m.WorkArea.Height<=0||!m.Bounds.Contains(m.WorkArea))
            ||monitors.Any(m=>monitors.Any(other=>m.Device!=other.Device&&m.Bounds.Overlaps(other.Bounds))))return null;
        PosMonitor? selected=assignedStaff is not null?monitors.SingleOrDefault(m=>m.Device==assignedStaff)
            :monitors.Count==1?monitors[0]:monitors.SingleOrDefault(m=>m.Bounds.Contains(window));
        return selected is null?null:new(selected,monitors);
    }
    public bool IsCurrent(IReadOnlyList<PosMonitor> current)=>current.Count==monitors.Length&&monitors.All(current.Contains);
    public bool Allows(DisplayBounds proposed,IReadOnlyList<PosMonitor> current)=>proposed.Width>0&&proposed.Height>0&&IsCurrent(current)&&Monitor.Bounds.Contains(proposed);
}
