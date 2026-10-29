using System.IO;
using System.Security.Cryptography;
using NexaConnect.Contracts.Media;
namespace NexaConnect.POS;

public sealed record ManualTenderReadiness(bool Allowed,string Guidance)
{
    public static ManualTenderReadiness Evaluate(string method,bool cashOpen,bool qrAvailable,bool receiptVerified,string? reference,bool originalAttempt)
    {
        if(method=="cash")return cashOpen?new(true,originalAttempt?"Verify the original cash payment. Do not collect again.":"Confirm only after receiving the exact cash amount."):new(false,"Open or resume the original THB cash session.");
        if(method!="promptpay_manual")return new(false,"Select a supported manual payment method.");
        if(!originalAttempt&&!qrAvailable)return new(false,"PromptPay QR is missing, expired or unavailable. Configure and approve it in Customer Portal, then refresh the branch QR.");
        if(!receiptVerified)return new(false,originalAttempt?"Original saved proof is incomplete. Preserve recovery and request reconciliation; do not collect again.":"Verify receipt of the exact amount, then check I verified the received payment and receipt.");
        if(string.IsNullOrWhiteSpace(reference)||reference.Trim().Length>64)return new(false,originalAttempt?"Original saved reference is incomplete. Preserve recovery and request reconciliation.":"Enter the receipt or bank reference (1–64 characters).");
        return new(true,originalAttempt?"Verify using the original saved receipt and reference. Do not request another transfer.":"Receipt verification recorded locally. Confirm payment received to submit once.");
    }
}
public static class PromptPayQrPolicy
{
    public static void Validate(PromptPayQrBundleV1 bundle,Guid org,Guid restaurant,Guid branch,DateTimeOffset now)
    {
        var s=bundle.Selection??throw new InvalidDataException("Missing branch QR selection.");
        if(s.OrganizationId!=org||s.RestaurantId!=restaurant||s.BranchId!=branch||s.Version<1||s.AssetId==Guid.Empty
            ||bundle.IssuedAtUtc>now.AddSeconds(5)||bundle.ExpiresAtUtc<=now||bundle.ExpiresAtUtc<=bundle.IssuedAtUtc
            ||bundle.ExpiresAtUtc-bundle.IssuedAtUtc>TimeSpan.FromSeconds(60))throw new InvalidDataException("Invalid branch QR scope or lease.");
        if(bundle.Image is {} image&&(s.AssetId!=image.AssetId||image.Png is null||image.Png.Length is <24 or >2_097_152
            ||!image.Png.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})
            ||!image.Png.AsSpan(12,4).SequenceEqual("IHDR"u8)
            ||System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(image.Png.AsSpan(16,4)) is <64 or >2048
            ||System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(image.Png.AsSpan(20,4)) is <64 or >2048
            ||!Convert.ToHexString(SHA256.HashData(image.Png)).Equals(image.Sha256,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Invalid branch QR image.");
    }
}
public static class ManualTenderIntent
{
    public static LocalPendingSettlementState Prepare(LocalPendingSettlementState? saved,PosOrderResult order,Guid key,string method,bool verified,string? reference)
    {
        if(key==Guid.Empty||order.OrderId==Guid.Empty||order.Currency!="THB"||order.TotalAmount<=0
            ||saved is not null&&(saved.OrderId!=order.OrderId||saved.IdempotencyKey!=key))throw new InvalidDataException("Original payment identity differs.");
        if(saved?.Method is not null){
            if(saved.Method is not ("cash" or "promptpay_manual")||saved.Currency!="THB"||saved.Amount<=0)throw new InvalidDataException("Original payment is invalid.");
            return saved with{OutcomeUncertain=true};
        }
        if(method is not ("cash" or "promptpay_manual"))throw new InvalidDataException("Unsupported manual tender.");
        return new(order.OrderId,order.TotalAmount,order.Currency,key,method,method=="promptpay_manual"&&verified,method=="promptpay_manual"?reference:null,true,order.Pricing);
    }
}
