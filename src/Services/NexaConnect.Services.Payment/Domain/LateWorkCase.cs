namespace NexaConnect.Services.Payment.Domain;
public sealed record LateWorkCase(long Version,string Status)
{
 public const string ReviewPermission="pos.day-close.late-work.review";
 public LateWorkCase Review(long expected,string decision,string reason)
 {
  if(expected<0||decision is not("investigate" or "require_correction" or "acknowledge")
   ||reason!=(decision=="investigate"?"investigate_delivery":decision=="require_correction"?"correction_needed":"evidence_checked"))throw new ArgumentException();
  if(Version!=expected||Status is not("pending_review" or "investigating" or "correction_required" or "reviewed"))throw new InvalidOperationException("Review version changed.");
  return new(checked(Version+1),decision=="investigate"?"investigating":decision=="require_correction"?"correction_required":"reviewed");
 }
}
