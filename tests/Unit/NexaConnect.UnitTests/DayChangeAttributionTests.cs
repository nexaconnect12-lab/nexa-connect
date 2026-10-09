namespace NexaConnect.UnitTests;

public sealed class DayChangeAttributionTests
{
    [Theory]
    [InlineData(null,null,null)]
    [InlineData(-1,null,true)]
    [InlineData(null,-1,true)]
    [InlineData(0,null,false)]
    [InlineData(null,0,false)]
    [InlineData(1,2,false)]
    [InlineData(-1,1,true)]
    [InlineData(1,-1,true)]
    public void Owning_domains_use_both_effective_states_and_the_exclusive_utc_boundary(int? before,int? after,bool? expected)
    {
        var end=new DateTimeOffset(2026,9,3,0,0,0,TimeSpan.Zero);
        DateTimeOffset[]? a=before.HasValue?[end.AddTicks(before.Value)]:null,b=after.HasValue?[end.AddTicks(after.Value)]:null;
        Assert.Equal(expected,NexaConnect.Services.Order.Domain.FinancialChange.AffectsWindow(end,a,b));
        Assert.Equal(expected,NexaConnect.Services.Payment.Domain.FinancialChange.AffectsWindow(end,a,b));
        Assert.Equal(expected,NexaConnect.Services.POS.Domain.FinancialChange.AffectsWindow(end,a,b));
    }
    [Fact]
    public void Incomplete_attribution_never_proves_a_change_unrelated()
    {
        var end=DateTimeOffset.UtcNow;
        Assert.Null(NexaConnect.Services.Order.Domain.FinancialChange.AffectsWindow(end,[],[end]));
        Assert.Null(NexaConnect.Services.Payment.Domain.FinancialChange.AffectsWindow(end,[default],[end]));
        Assert.Null(NexaConnect.Services.POS.Domain.FinancialChange.AffectsWindow(default,null,[end]));
    }
}
