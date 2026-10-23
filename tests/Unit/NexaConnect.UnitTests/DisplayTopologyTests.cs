using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class DisplayTopologyTests
{
    private static PosMonitor Staff => new("staff", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), true);
    private static PosMonitor Customer => new("customer", new(-1280, 0, 0, 1024), new(-1280, 0, 0, 984), false);

    [Fact]
    public void Roles_require_explicit_distinct_extended_monitors()
    {
        var policy = new DisplayTopology();
        Assert.False(policy.Active); Assert.False(policy.Assign([Staff], "staff", "staff"));
        Assert.False(policy.Assign([Staff, Customer], "staff", "staff"));
        Assert.False(policy.Assign([Staff, Customer], "missing", "customer"));
        Assert.False(policy.Assign([Staff, Customer with { Bounds = Staff.Bounds, WorkArea = Staff.WorkArea }], "staff", "customer"));
        Assert.True(policy.Assign([Staff, Customer], "staff", "customer"));
        Assert.Equal(Customer.Bounds, policy.Customer!.Bounds); // Negative pixel coordinates are valid.
    }

    [Fact]
    public void Staff_movement_cannot_cross_or_straddle_the_customer_screen()
    {
        var policy = new DisplayTopology(); policy.Assign([Staff, Customer], "staff", "customer");
        Assert.True(policy.AllowsStaff(new(0, 0, 1200, 900), [Staff, Customer]));
        Assert.False(policy.AllowsStaff(Customer.Bounds, [Staff, Customer]));
        Assert.False(policy.AllowsStaff(new(-10, 0, 1190, 900), [Staff, Customer]));
    }

    [Theory]
    [InlineData("staff-lost")]
    [InlineData("customer-lost")]
    [InlineData("resolution")]
    [InlineData("work-area")]
    [InlineData("primary")]
    [InlineData("new-monitor")]
    public void Topology_changes_fail_closed_and_reconnect_does_not_auto_restore(string change)
    {
        var policy = new DisplayTopology(); policy.Assign([Staff, Customer], "staff", "customer");
        PosMonitor[] changed = change switch {
            "staff-lost" => [Customer], "customer-lost" => [Staff],
            "resolution" => [Staff, Customer with { Bounds = new(-1024, 0, 0, 768) }],
            "work-area" => [Staff with { WorkArea = new(0, 0, 1920, 1000) }, Customer],
            "primary" => [Staff with { Primary = false }, Customer with { Primary = true }],
            _ => [Staff, Customer, new("third", new(1920, 0, 3840, 1080), new(1920, 0, 3840, 1040), false)]
        };
        Assert.False(policy.IsCurrent(changed)); Assert.False(policy.AllowsStaff(Staff.WorkArea, changed));
        policy.Block();
        Assert.False(policy.IsCurrent([Staff, Customer]));
        Assert.False(policy.Assign([Staff, Customer], "staff", "customer"));
    }

    [Fact]
    public void Explicit_disable_returns_to_single_screen_and_restart_starts_unassigned()
    {
        var policy = new DisplayTopology(); policy.Assign([Staff, Customer], "staff", "customer");
        policy.Disable(); Assert.False(policy.Active); Assert.Null(policy.Customer);
        Assert.True(policy.Assign([Staff, Customer], "staff", "customer"));
        Assert.False(new DisplayTopology().Active);
    }
}
