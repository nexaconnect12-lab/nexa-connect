using NexaConnect.Services.POS.Domain;
namespace NexaConnect.UnitTests;
public sealed class LateWorkCaseTests
{
 [Theory][InlineData("tenant-admin",true)][InlineData("store-manager",true)][InlineData("accountant",false)][InlineData("cashier",false)]
 public void Late_review_is_manager_only_by_default(string role,bool granted)=>Assert.Equal(granted,NexaConnect.Services.Authorization.Domain.ProductRoleDefaults.PermissionsFor(role).Contains(LateWorkCase.ReviewPermission));
 [Theory][InlineData("investigate","investigate_delivery","investigating")][InlineData("require_correction","correction_needed","correction_required")][InlineData("acknowledge","evidence_checked","reviewed")]
 public void Append_decision_advances_review_version(string decision,string reason,string status){var result=new LateWorkCase(2,"reviewed").Review(2,decision,reason);Assert.Equal(3,result.Version);Assert.Equal(status,result.Status);}
 [Fact]public void Stale_version_cannot_overwrite_other_manager_decision()=>Assert.Throws<InvalidOperationException>(()=>new LateWorkCase(2,"investigating").Review(1,"acknowledge","evidence_checked"));
 [Theory][InlineData("release","evidence_checked")][InlineData("acknowledge","correction_needed")][InlineData("require_correction","investigate_delivery")]
 public void Unsupported_or_mismatched_reason_is_rejected(string decision,string reason)=>Assert.Throws<ArgumentException>(()=>new LateWorkCase(0,"pending_review").Review(0,decision,reason));
}
