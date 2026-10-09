using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Application.CashReviews;
using NexaConnect.Services.POS.Domain;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Application.DayClose;
public sealed record CorrectionCalendar(Guid OrganizationId,Guid RestaurantId,Guid BranchId,string TimeZone,string Currency);
public interface ILateCashCorrectionEvidence
{Task<CashTenderProof?> TenderAsync(LateCashCase work,PosUserContext user,CancellationToken ct);Task<CorrectionCalendar> CalendarAsync(Guid branch,CancellationToken ct);}
public interface ILateCashCorrectionStore
{
 Task<LateCashCase?> CaseAsync(LateWorkScope scope,Guid work,CancellationToken ct);
 Task<LateCashCorrectionReceipt?> ReadAsync(LateWorkScope scope,Guid work,CancellationToken ct);
 Task<LateCashCorrectionReceipt?> ReplayAsync(LateWorkScope scope,LateCashCorrectionCommand command,string actor,CancellationToken ct);
 Task<LateCashCorrectionReceipt> PostAsync(LateWorkScope scope,LateCashCorrectionCommand command,LateCashCase work,CorrectionPostingDay day,CashTenderProof proof,string actor,Guid authorization,Guid correlation,DateTimeOffset now,CancellationToken ct);
}
public sealed class LateCashCorrections(IDaySettlementStore settlements,ILateCashCorrectionStore store,ILateCashCorrectionEvidence evidence,IRestaurantScopeReader scopes,IAuthorizationDecisionClient permissions,
 CashReviews.POSLateWork access,TimeProvider clock)
{
 private async Task<(LateWorkScope Scope,bool CanPost)> Authorize(Guid organization,Guid branch,DateOnly date,Guid work,PosUserContext user,CancellationToken ct)
 {
  if(organization==Guid.Empty||branch==Guid.Empty||work==Guid.Empty||date==default||date==DateOnly.MaxValue)throw new ArgumentException();
  var scope=await scopes.GetAsync(branch,ct);if(scope.OrganizationId!=organization||scope.BranchId!=branch||scope.RestaurantId==Guid.Empty||string.IsNullOrWhiteSpace(user.Subject)||string.IsNullOrWhiteSpace(user.AccessToken))throw new UnauthorizedAccessException();
  foreach(var required in new[]{DayClosePreparation.ReadPermission,CashReviewPermissions.Read}){var read=await permissions.DecideAsync(user,scope,required,ct);if(!read.Granted||read.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();}
  var state=await settlements.ReadAsync(new(organization,scope.RestaurantId,branch,date),ct);
  if(state?.Receipt is null)throw new DayCloseConflictException("settlement_not_committed");
  var snapshot=state.Preparation.Approval.Snapshot;var basis=new LateWorkScope(new(organization,scope.RestaurantId,branch,snapshot.FromUtc,snapshot.ToUtc),state.Id);
  if(await access.ReadAsync(basis,work,user,ct) is null)throw new ArgumentException("Case unavailable.");
  var permission=await permissions.DecideAsync(user,scope,LateCashCorrection.PostPermission,ct);return(basis,permission.Granted&&permission.DecisionId!=Guid.Empty);
 }
 public async Task<LateCashCorrectionView> PreviewAsync(Guid organization,Guid branch,DateOnly date,Guid workId,PosUserContext user,CancellationToken ct)
 {
  var (scope,canPost)=await Authorize(organization,branch,date,workId,user,ct);var receipt=await store.ReadAsync(scope,workId,ct);if(receipt is not null)return new(scope,workId,null,receipt,canPost);
  var candidate=await store.CaseAsync(scope,workId,ct);if(candidate is null||candidate.Tender.Method!="cash")return new(scope,workId,null,null,false,"unsupported_cash_evidence");
  if(candidate.ReviewStatus!="correction_required")return new(scope,workId,null,null,false,"correction_review_required");
  var (work,proof,day,adjustment)=await Basis(scope,workId,user,ct);
  if(canPost){var branchScope=await scopes.GetAsync(branch,ct);if(branchScope.OrganizationId!=organization||branchScope.RestaurantId!=scope.Window.RestaurantId||branchScope.BranchId!=branch)throw new UnauthorizedAccessException();var amountDecision=await permissions.DecideForAmountAsync(user,branchScope,LateCashCorrection.PostPermission,proof.Amount,"THB",ct);canPost=amountDecision.Granted&&amountDecision.DecisionId!=Guid.Empty;}
  return new(scope,workId,Preview(scope,work,day,adjustment),null,canPost);
 }
 private async Task<(LateCashCase,CashTenderProof,CorrectionPostingDay,decimal)> Basis(LateWorkScope scope,Guid workId,PosUserContext user,CancellationToken ct)
 {
  var work=await store.CaseAsync(scope,workId,ct)??throw new DayCloseConflictException("late_cash_evidence_unavailable");
  var proof=await evidence.TenderAsync(work,user,ct)??throw new DayCloseConflictException("original_tender_evidence_missing");var calendar=await evidence.CalendarAsync(scope.Window.BranchId,ct);
  if(calendar.OrganizationId!=scope.Window.OrganizationId||calendar.RestaurantId!=scope.Window.RestaurantId||calendar.BranchId!=scope.Window.BranchId||calendar.Currency!="THB")throw new UnauthorizedAccessException();
  var now=clock.GetUtcNow();var day=LateCashCorrection.CurrentDay(calendar.TimeZone,now);
  try{return(work,proof,day,LateCashCorrection.Validate(work,proof,day,now));}catch(InvalidOperationException){throw new DayCloseConflictException("late_cash_evidence_changed");}
 }
 public async Task<LateCashCorrectionView> PostAsync(Guid organization,Guid branch,DateOnly date,LateCashCorrectionCommand command,PosUserContext user,Guid correlation,CancellationToken ct)
 {
  if(command.OperationId==Guid.Empty||command.ExpectedReviewVersion<=0||command.PreviewFingerprint is null||command.PreviewFingerprint.Length!=64||command.PreviewFingerprint.Any(c=>!char.IsAsciiHexDigit(c)))throw new ArgumentException();
  var (scope,canPost)=await Authorize(organization,branch,date,command.WorkId,user,ct);if(!canPost)throw new UnauthorizedAccessException();
  if(await store.ReplayAsync(scope,command,user.Subject,ct) is {} replay){var replayScope=await scopes.GetAsync(branch,ct);if(replayScope.OrganizationId!=organization||replayScope.RestaurantId!=scope.Window.RestaurantId||replayScope.BranchId!=branch)throw new UnauthorizedAccessException();var replayDecision=await permissions.DecideForAmountAsync(user,replayScope,LateCashCorrection.PostPermission,-replay.Adjustment,"THB",ct);if(!replayDecision.Granted||replayDecision.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();return new(scope,command.WorkId,null,replay,true);}
  var (work,proof,day,adjustment)=await Basis(scope,command.WorkId,user,ct);var preview=Preview(scope,work,day,adjustment);
  if(preview.ReviewVersion!=command.ExpectedReviewVersion||preview.Fingerprint!=command.PreviewFingerprint)throw new DayCloseConflictException("correction_preview_changed");
  var confirmed=await Authorize(organization,branch,date,command.WorkId,user,ct);if(!confirmed.CanPost||confirmed.Scope!=scope)throw new UnauthorizedAccessException();
  var branchScope=await scopes.GetAsync(branch,ct);if(branchScope.OrganizationId!=organization||branchScope.RestaurantId!=scope.Window.RestaurantId||branchScope.BranchId!=branch)throw new UnauthorizedAccessException();
  var confirmedProof=await evidence.TenderAsync(work,user,ct);if(confirmedProof!=proof)throw new DayCloseConflictException("original_tender_evidence_changed");
  var confirmedCalendar=await evidence.CalendarAsync(branch,ct);if(confirmedCalendar.OrganizationId!=organization||confirmedCalendar.RestaurantId!=scope.Window.RestaurantId||confirmedCalendar.BranchId!=branch||confirmedCalendar.Currency!="THB")throw new UnauthorizedAccessException();
  if(LateCashCorrection.CurrentDay(confirmedCalendar.TimeZone,clock.GetUtcNow())!=day)throw new DayCloseConflictException("posting_calendar_changed");
  var decision=await permissions.DecideForAmountAsync(user,branchScope,LateCashCorrection.PostPermission,proof.Amount,"THB",ct);if(!decision.Granted||decision.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
  var receipt=await store.PostAsync(scope,command,work,day,proof,user.Subject,decision.DecisionId,correlation,clock.GetUtcNow(),ct);return new(scope,command.WorkId,null,receipt,true);
 }
 public static LateCashCorrectionPreview Preview(LateWorkScope scope,LateCashCase work,CorrectionPostingDay day,decimal adjustment)
 {
  var p=new LateCashCorrectionPreview(scope,work.WorkId,work.ReviewVersion,work.Tender.OrderId,work.Tender.TenderId,work.DrawerId,"THB",adjustment,day.Date,day.TimeZone,day.FromUtc,day.ToUtc,"");
  var fingerprint=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{preview=p,work.Tender.Fingerprint},new JsonSerializerOptions(JsonSerializerDefaults.Web))));return p with{Fingerprint=fingerprint};
 }
}
