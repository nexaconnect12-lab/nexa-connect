namespace NexaConnect.Services.Payment.Domain;

/// <summary>Immutable source evidence eligibility. This grants no financial approval or write lock.</summary>
public sealed record FinancialDaySeal(Guid ManifestId,Guid Epoch,long Revision)
{
    public static FinancialDaySeal Retain(Guid expectedManifest,Guid expectedEpoch,long expectedRevision,
        Guid manifest,Guid epoch,long revision,bool revisionMatches,bool supportedCurrency,int blockers)
    {
        if(expectedManifest==Guid.Empty || expectedEpoch==Guid.Empty || expectedRevision<0)throw new ArgumentException();
        if(manifest!=expectedManifest || epoch!=expectedEpoch || revision!=expectedRevision || !revisionMatches
            || !supportedCurrency || blockers!=0)throw new InvalidOperationException("Source evidence cannot be sealed.");
        return new(manifest,epoch,revision);
    }
}