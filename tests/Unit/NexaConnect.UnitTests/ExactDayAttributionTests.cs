using Order=NexaConnect.Services.Order.Domain;
using Payment=NexaConnect.Services.Payment.Domain;
using Pos=NexaConnect.Services.POS.Domain;
namespace NexaConnect.UnitTests;

public sealed class ExactDayAttributionTests
{
    private static readonly DateTimeOffset From=new(2026,10,5,0,0,0,TimeSpan.Zero),To=From.AddDays(1);
    [Theory]
    [InlineData("completed",-2,-2,false,"unrelated")]
    [InlineData("completed",0,-2,true,"sales_date")]
    [InlineData("completed",-2,0,true,"tender_date")]
    [InlineData("completed",1,1,false,"unrelated")]
    [InlineData("draft",-2,null,true,"unresolved_order")]
    [InlineData("kitchen_accepted",-2,null,true,"unresolved_order")]
    [InlineData("cancellation_review",-2,null,true,"unresolved_order")]
    [InlineData("draft",1,null,false,"unrelated")]
    [InlineData("cancelled",-2,null,false,"unrelated")]
    public void Order_creation_tender_and_carry_forward_are_distinct(string status,int created,int? paid,bool affected,string reason)
    {
        var state=new Order.FinancialRecord(Guid.NewGuid(),status,1,From.AddDays(created),paid.HasValue?From.AddDays(paid.Value):null,true);
        Assert.Equal(new Order.FinancialImpact(affected,reason),Order.FinancialChange.Classify(From,To,state,state,false,"orders"));
    }
    [Theory]
    [InlineData("refunds","completed",-2,-2,false,"unrelated")]
    [InlineData("refunds","completed",-2,0,true,"refund_date")]
    [InlineData("refunds","refund_unknown",-2,null,true,"unresolved_refund")]
    [InlineData("refunds","failed",-2,null,false,"unrelated")]
    [InlineData("payment_intents","captured",-2,null,false,"unrelated")]
    [InlineData("payment_intents","void_failed",-2,null,true,"unresolved_payment")]
    [InlineData("payment_intents","authorized",1,null,false,"unrelated")]
    public void Payment_uses_refund_completion_and_uncertain_work(string kind,string status,int created,int? completed,bool affected,string reason)
    {
        var state=new Payment.FinancialRecord(Guid.NewGuid(),status,1,From.AddDays(created),completed.HasValue?From.AddDays(completed.Value):null,true);
        Assert.Equal(new Payment.FinancialImpact(affected,reason),Payment.FinancialChange.Classify(From,To,state,state,false,kind));
    }
    [Theory]
    [InlineData(-2,false,null,null,false,"unrelated")]
    [InlineData(0,false,null,null,true,"cash_close_date")]
    [InlineData(-2,true,"approved",2L,false,"unrelated")]
    [InlineData(-2,true,"approved",1L,true,"cash_review")]
    [InlineData(-2,true,"investigating",2L,true,"cash_review")]
    public void Drawer_closure_and_current_financial_review_control_carry_forward(int closed,bool variance,string? review,long? reviewed,bool affected,string reason)
    {
        var state=new Pos.FinancialRecord(Guid.NewGuid(),"closed",2,From.AddDays(-3),From.AddDays(closed),HasVariance:variance,ReviewStatus:review,ReviewedVersion:reviewed);
        Assert.Equal(new Pos.FinancialImpact(affected,reason),Pos.FinancialChange.Classify(From,To,state,state,false,"cash_movements"));
    }
    [Fact]
    public void Both_sides_of_a_date_move_remain_pending_even_when_current_totals_are_restored()
    {
        var before=new Order.FinancialRecord(Guid.NewGuid(),"completed",1,From,From,true);
        var after=before with{CreatedAtUtc=From.AddDays(-2),FinancialAtUtc=From.AddDays(-2),Version=2};
        Assert.True(Order.FinancialChange.Classify(From,To,before,after,false,"orders").AffectsWindow);
        Assert.True(Order.FinancialChange.Classify(From,To,after,before,false,"orders").AffectsWindow);
        var end=before with{CreatedAtUtc=To,FinancialAtUtc=To};
        Assert.False(Order.FinancialChange.Classify(From,To,end,end,false,"orders").AffectsWindow);
    }
    [Fact]
    public void Incomplete_history_unknown_states_and_ownership_drift_fail_closed()
    {
        var order=new Order.FinancialRecord(Guid.NewGuid(),"completed",1,From.AddDays(-3),From.AddDays(-3));
        Assert.Null(Order.FinancialChange.Classify(From,To,order,order,false,"orders").AffectsWindow);
        Assert.Null(Order.FinancialChange.Classify(From,To,order,order,true,"orders").AffectsWindow);
        var refund=new Payment.FinancialRecord(Guid.NewGuid(),"completed",1,From,null,true);
        Assert.Null(Payment.FinancialChange.Classify(From,To,refund,refund,false,"refunds").AffectsWindow);
        var drawer=new Pos.FinancialRecord(Guid.NewGuid(),"closed",1,From.AddDays(-3),From.AddDays(-2));
        Assert.Null(Pos.FinancialChange.Classify(From,To,drawer,drawer,false,"cash_sessions").AffectsWindow);
        Assert.Null(Pos.FinancialChange.Classify(From,To,drawer with{Status="unsupported"},drawer,false,"cash_sessions").AffectsWindow);
        var store=drawer with{Status="active"};
        Assert.False(Pos.FinancialChange.Classify(From,To,store,store,false,"stores").AffectsWindow);
        Assert.Null(Pos.FinancialChange.Classify(From,To,store,store,true,"stores").AffectsWindow);
    }
}
