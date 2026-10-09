using System.Text.Json.Serialization;
namespace NexaConnect.Contracts.Reporting;
public sealed record LateCashCorrectionCommand(Guid WorkId,Guid OperationId,long ExpectedReviewVersion,string PreviewFingerprint);
public sealed record LateCashCorrectionPreview(LateWorkScope Scope,Guid WorkId,long ReviewVersion,Guid OrderId,Guid TenderId,Guid DrawerId,
 string Currency,[property:JsonNumberHandling(JsonNumberHandling.AllowReadingFromString|JsonNumberHandling.WriteAsString)]decimal Adjustment,DateOnly PostingDate,string TimeZone,DateTimeOffset PostingFromUtc,DateTimeOffset PostingToUtc,string Fingerprint);
public sealed record LateCashCorrectionReceipt(Guid CorrectionId,Guid OperationId,LateWorkScope Scope,Guid WorkId,long ReviewVersion,Guid OrderId,Guid TenderId,Guid DrawerId,
 string Currency,[property:JsonNumberHandling(JsonNumberHandling.AllowReadingFromString|JsonNumberHandling.WriteAsString)]decimal Adjustment,DateOnly PostingDate,DateTimeOffset PostedAtUtc,Guid EventId);
public sealed record LateCashCorrectionView(LateWorkScope Scope,Guid WorkId,LateCashCorrectionPreview? Preview,LateCashCorrectionReceipt? Receipt,bool CanPost,string? Blocker=null);
