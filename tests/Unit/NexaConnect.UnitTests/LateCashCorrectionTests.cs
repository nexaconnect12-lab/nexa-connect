using NexaConnect.Services.POS.Domain;
namespace NexaConnect.UnitTests;
public sealed class LateCashCorrectionTests
{
 private static readonly DateTimeOffset Now=new(2026,10,9,4,0,0,TimeSpan.Zero);
 private static CashTenderProof Proof()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),10,"THB","cash",Now.AddDays(-1),new('a',64));
 private static LateCashCase Case(CashTenderProof t)=>new(Guid.NewGuid(),Guid.NewGuid(),2,"correction_required",t,Guid.NewGuid(),Now.AddDays(-1).AddHours(1),Now.AddDays(-1).Date,Now.Date,false);
 [Theory][InlineData("tenant-admin",true)][InlineData("store-manager",true)][InlineData("accountant",false)][InlineData("cashier",false)]public void Posting_is_manager_only_by_default(string role,bool granted)=>Assert.Equal(granted,NexaConnect.Services.Authorization.Domain.ProductRoleDefaults.PermissionsFor(role).Contains(LateCashCorrection.PostPermission));
 [Fact]public void Verified_missing_cash_produces_only_negative_variance_adjustment(){var t=Proof();Assert.Equal(-10,LateCashCorrection.Validate(Case(t),t,LateCashCorrection.CurrentDay("UTC",Now),Now));}
 [Theory][InlineData("investigating")][InlineData("reviewed")][InlineData("pending_review")]public void Posting_requires_current_correction_decision(string status){var t=Proof();Assert.Throws<InvalidOperationException>(()=>LateCashCorrection.Validate(Case(t) with{ReviewStatus=status},t,LateCashCorrection.CurrentDay("UTC",Now),Now));}
 [Fact]public void Original_evidence_and_existing_projection_cannot_be_overridden(){var t=Proof();var day=LateCashCorrection.CurrentDay("UTC",Now);foreach(var invalid in new[]{t with{Amount=11},t with{TerminalId=Guid.NewGuid()},t with{Method="promptpay_manual"},t with{Currency="USD"}})Assert.Throws<InvalidOperationException>(()=>LateCashCorrection.Validate(Case(t),invalid,day,Now));Assert.Throws<InvalidOperationException>(()=>LateCashCorrection.Validate(Case(t) with{AlreadyApplied=true},t,day,Now));}
 [Fact]public void Branch_local_posting_date_and_expired_day_are_explicit(){var instant=new DateTimeOffset(Now.UtcDateTime.Date.AddHours(23));var day=LateCashCorrection.CurrentDay("Asia/Bangkok",instant);Assert.Equal(new DateOnly(2026,10,10),day.Date);var t=Proof();Assert.Throws<InvalidOperationException>(()=>LateCashCorrection.Validate(Case(t),t,day,day.ToUtc));}
 [Fact]public void Historical_barrier_attribution_uses_posting_time(){var day=LateCashCorrection.CurrentDay("UTC",Now);var state=new FinancialRecord(Guid.NewGuid(),"posted",1,Now,Now);Assert.False(FinancialChange.Classify(day.FromUtc.AddDays(-1),day.FromUtc,null,state,false,"late_cash_corrections").AffectsWindow);Assert.True(FinancialChange.Classify(day.FromUtc,day.ToUtc,null,state,false,"late_cash_corrections").AffectsWindow);}
}
