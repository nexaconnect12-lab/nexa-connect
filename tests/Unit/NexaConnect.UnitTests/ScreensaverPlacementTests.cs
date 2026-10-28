using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class ScreensaverPlacementTests
{
    private static PosMonitor Staff=>new("staff",new(-1920,0,0,1080),new(-1920,0,0,1040),false);
    private static PosMonitor Customer=>new("customer",new(0,0,2560,1440),new(0,0,2560,1400),true);
    [Fact]
    public void Empty_or_inverted_window_geometry_is_never_accepted()
    {
        Assert.Null(ScreensaverPlacement.Create([Staff,Customer],new(0,0,0,0),null));
        Assert.Null(ScreensaverPlacement.Create([Staff],new(0,0,-1,100),null));
        var placement=ScreensaverPlacement.Create([Staff],Staff.WorkArea,null)!;
        Assert.False(placement.Allows(new(-100,0,-200,100),[Staff]));
    }
    [Fact]
    public void Single_monitor_covers_physical_bounds_including_taskbar_without_dip_conversion()
    {
        var placement=ScreensaverPlacement.Create([Staff],new(-1928,-8,8,1088),null);
        Assert.NotNull(placement);Assert.Equal(Staff.Bounds,placement.Monitor.Bounds);
        Assert.True(placement.Allows(Staff.Bounds,[Staff]));Assert.False(placement.Allows(new(-1920,0,1,1080),[Staff]));
    }
    [Fact]
    public void Assigned_staff_monitor_wins_and_never_selects_customer_or_missing_device()
    {
        var placement=ScreensaverPlacement.Create([Staff,Customer],Customer.WorkArea,"staff");
        Assert.Equal(Staff,placement!.Monitor);
        Assert.False(placement.Allows(Customer.Bounds,[Staff,Customer]));
        Assert.Null(ScreensaverPlacement.Create([Staff,Customer],Staff.WorkArea,"missing"));
    }
    [Fact]
    public void Unassigned_extended_desktop_requires_one_monitor_to_contain_the_staff_window()
    {
        Assert.Equal(Staff,ScreensaverPlacement.Create([Staff,Customer],new(-1800,80,-500,900),null)!.Monitor);
        Assert.Null(ScreensaverPlacement.Create([Staff,Customer],new(-500,0,500,900),null));
    }
    [Theory]
    [InlineData("empty")][InlineData("duplicate")][InlineData("overlap")][InlineData("invalid-bounds")][InlineData("invalid-work-area")]
    public void Invalid_or_mirrored_topology_does_not_enlarge_the_staff_window(string change)
    {
        PosMonitor[] monitors=change switch{
            "empty"=>[],"duplicate"=>[Staff,Staff],"overlap"=>[Staff,Customer with{Bounds=Staff.Bounds,WorkArea=Staff.WorkArea}],
            "invalid-bounds"=>[Staff with{Bounds=new(0,0,0,0)}],_=>[Staff with{WorkArea=new(-1920,0,1,1080)}]};
        Assert.Null(ScreensaverPlacement.Create(monitors,Staff.WorkArea,null));
    }
    [Theory]
    [InlineData("removed")][InlineData("added")][InlineData("bounds")][InlineData("work-area")]
    public void A_monitor_lease_does_not_survive_topology_or_geometry_changes(string change)
    {
        var placement=ScreensaverPlacement.Create([Staff,Customer],Staff.WorkArea,"staff")!;
        PosMonitor[] changed=change switch{
            "removed"=>[Staff],"added"=>[Staff,Customer,new("extra",new(2560,0,3840,720),new(2560,0,3840,700),false)],
            "bounds"=>[Staff with{Bounds=new(-1280,0,0,1080)},Customer],_=>[Staff with{WorkArea=new(-1920,0,0,1000)},Customer]};
        Assert.False(placement.IsCurrent(changed));Assert.False(placement.Allows(Staff.Bounds,changed));
    }
}
