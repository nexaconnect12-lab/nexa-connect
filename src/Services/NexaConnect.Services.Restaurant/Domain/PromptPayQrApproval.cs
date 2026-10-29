namespace NexaConnect.Services.Restaurant.Domain;

public sealed record PromptPayQrApproval
{
    public long ExpectedVersion { get; }
    public Guid? AssetId { get; }
    public PromptPayQrApproval(long expectedVersion,Guid? assetId,bool approved)
    {
        if(expectedVersion<1||assetId==Guid.Empty||!approved)throw new ArgumentException("Explicit branch approval and the current version are required.");
        ExpectedVersion=expectedVersion;AssetId=assetId;
    }
}
