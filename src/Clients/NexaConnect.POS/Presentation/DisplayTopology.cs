namespace NexaConnect.POS;

public readonly record struct DisplayBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(DisplayBounds other) => other.Left >= Left && other.Top >= Top
        && other.Right <= Right && other.Bottom <= Bottom;
    public bool Overlaps(DisplayBounds other) => Left < other.Right && Right > other.Left
        && Top < other.Bottom && Bottom > other.Top;
}
public sealed record PosMonitor(string Device, DisplayBounds Bounds, DisplayBounds WorkArea, bool Primary)
{
    public string Label => $"{Device} · {Bounds.Width}×{Bounds.Height}{(Primary ? " · primary" : "")}";
}

/// <summary>Fail-closed display role policy; no financial or authentication authority.</summary>
public sealed class DisplayTopology
{
    public PosMonitor? Staff { get; private set; }
    public PosMonitor? Customer { get; private set; }
    public bool Blocked { get; private set; }
    private PosMonitor[] assignedTopology = [];
    public bool Active => Staff is not null && Customer is not null;

    public bool Assign(IReadOnlyList<PosMonitor> monitors, string staffDevice, string customerDevice)
    {
        if (Blocked || monitors.Count < 2 || monitors.Select(m => m.Device).Distinct().Count() != monitors.Count) return false;
        var staff = monitors.SingleOrDefault(m => m.Device == staffDevice);
        var customer = monitors.SingleOrDefault(m => m.Device == customerDevice);
        if (staff is null || customer is null || staff == customer || monitors.Any(m => m.Bounds.Width <= 0 || m.Bounds.Height <= 0
            || m.WorkArea.Width <= 0 || m.WorkArea.Height <= 0 || !m.Bounds.Contains(m.WorkArea))
            || monitors.Any(m => monitors.Any(other => m.Device != other.Device && m.Bounds.Overlaps(other.Bounds)))) return false;
        Staff = staff; Customer = customer; assignedTopology = monitors.ToArray();
        return true;
    }
    public bool IsCurrent(IReadOnlyList<PosMonitor> monitors) => !Blocked && Active
        && monitors.Count == assignedTopology.Length && assignedTopology.All(monitors.Contains);
    public bool AllowsStaff(DisplayBounds proposed, IReadOnlyList<PosMonitor> monitors) =>
        IsCurrent(monitors) && Staff!.Bounds.Contains(proposed);
    public void Block() => Blocked = true;
    public void Disable() { Staff = Customer = null; assignedTopology = []; }
}
