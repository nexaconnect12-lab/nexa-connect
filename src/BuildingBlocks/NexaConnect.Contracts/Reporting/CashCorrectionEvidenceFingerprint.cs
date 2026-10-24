using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.Contracts.Reporting;

/// <summary>Version-one integration evidence encoding only; admission and financial policy belong to each owner.</summary>
public static class CashCorrectionEvidenceFingerprint
{
    public static string Calculate(IEnumerable<PosLateCashCorrectionPostedV1> events) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(events.OrderBy(e=>e.EventId).Select(e=>new Canonical(e.EventId,e.OrganizationId,e.RestaurantId,e.BranchId,
            e.CorrectionId,e.WorkId,e.OriginalSettlementId,e.OrderId,e.TenderId,e.DrawerId,e.ReviewedVersion,e.PostingDate,
            e.PostingFromUtc.ToUniversalTime(),e.PostingToUtc.ToUniversalTime(),e.OccurredAtUtc.ToUniversalTime(),e.Currency,
            decimal.Parse(e.CashVarianceAdjustment.ToString("G29",CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture))).ToArray())));

    private sealed record Canonical(Guid EventId,Guid OrganizationId,Guid RestaurantId,Guid BranchId,Guid CorrectionId,Guid WorkId,
        Guid OriginalSettlementId,Guid OrderId,Guid TenderId,Guid DrawerId,long ReviewedVersion,DateOnly PostingDate,
        DateTimeOffset PostingFromUtc,DateTimeOffset PostingToUtc,DateTimeOffset PostedAtUtc,string Currency,decimal Adjustment);
}
