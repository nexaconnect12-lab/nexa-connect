namespace NexaConnect.Services.POS.Domain;
public sealed record CashTenderProof(Guid EventId,Guid TenderId,Guid OrderId,Guid TerminalId,Guid OrganizationId,Guid RestaurantId,Guid BranchId,decimal Amount,string Currency,string Method,DateTimeOffset OccurredAtUtc,string Fingerprint);
public sealed record LateCashCase(Guid WorkId,Guid SettlementId,long ReviewVersion,string ReviewStatus,CashTenderProof Tender,Guid DrawerId,DateTimeOffset DrawerClosedAtUtc,DateTimeOffset OriginalFromUtc,DateTimeOffset OriginalToUtc,bool AlreadyApplied);
public sealed record CorrectionPostingDay(DateOnly Date,string TimeZone,DateTimeOffset FromUtc,DateTimeOffset ToUtc);
public static class LateCashCorrection
{
 public const string PostPermission="pos.day-close.late-cash.post";
 public static decimal Validate(LateCashCase work,CashTenderProof authoritative,CorrectionPostingDay day,DateTimeOffset now)
 {
  if(work.WorkId==Guid.Empty||work.SettlementId==Guid.Empty||work.DrawerId==Guid.Empty||work.ReviewVersion<=0||work.ReviewStatus!="correction_required"||work.AlreadyApplied
   ||work.Tender!=authoritative||authoritative.EventId==Guid.Empty||authoritative.TenderId==Guid.Empty||authoritative.OrderId==Guid.Empty||authoritative.TerminalId==Guid.Empty
   ||authoritative.Amount<=0||authoritative.Amount>999999999999999.9999m||decimal.Round(authoritative.Amount,4)!=authoritative.Amount||authoritative.Currency!="THB"||authoritative.Method!="cash"
   ||work.DrawerClosedAtUtc<work.OriginalFromUtc||work.DrawerClosedAtUtc>=work.OriginalToUtc||authoritative.OccurredAtUtc>work.DrawerClosedAtUtc
   ||day.Date==default||day.FromUtc<work.OriginalToUtc||day.ToUtc<=day.FromUtc||day.ToUtc-day.FromUtc>TimeSpan.FromHours(27)||now<day.FromUtc||now>=day.ToUtc)
   throw new InvalidOperationException("Late cash correction evidence changed or unsupported.");
  return -authoritative.Amount;
 }
 public static CorrectionPostingDay CurrentDay(string timeZone,DateTimeOffset now)
 {
  var zone=TimeZoneInfo.FindSystemTimeZoneById(timeZone);if(!zone.HasIanaId&&TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone,out var iana))timeZone=iana;var date=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,zone).DateTime);
  var start=date.ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified);var end=date.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified);
  if(zone.IsInvalidTime(start)||zone.IsAmbiguousTime(start)||zone.IsInvalidTime(end)||zone.IsAmbiguousTime(end))throw new InvalidOperationException("Posting calendar unsupported.");
  return new(date,timeZone,new(TimeZoneInfo.ConvertTimeToUtc(start,zone)),new(TimeZoneInfo.ConvertTimeToUtc(end,zone)));
 }
}
