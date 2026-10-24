namespace NexaConnect.Services.POS.Domain.DayClose;

public sealed record DayCorrectionItem(Guid EventId,Guid CorrectionId,Guid OriginalSettlementId,Guid WorkId,
    Guid OrderId,Guid TenderId,Guid DrawerId,DateOnly PostingDate,DateTimeOffset PostedAtUtc,decimal Adjustment)
{
    public string AdjustmentText=>Adjustment.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture);
}
public sealed record DayCorrectionEvidence(Guid ManifestId,Guid RevisionEpoch,long SourceRevision,string ManifestHash,
    decimal Adjustment,int Expected,int Matched,int Missing,int Conflicting,int Unexpected,DayCorrectionItem[] Items)
{
    public string AdjustmentText=>Adjustment.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture);
    public bool IsValid(DateTimeOffset from,DateTimeOffset to) => ManifestId!=Guid.Empty&&RevisionEpoch!=Guid.Empty&&SourceRevision>=0
        && ManifestHash is {Length:64}&&ManifestHash.All(char.IsAsciiHexDigit)&&Adjustment<=0
        && new[]{Expected,Matched,Missing,Conflicting,Unexpected}.All(x=>x>=0&&x<=2000)&&Expected<=1000
        && Expected==Matched+Missing+Conflicting&&Items is not null&&Items.Length==Expected
        && Items.Select(i=>i.EventId).Distinct().Count()==Items.Length
        && Items.Select(i=>i.CorrectionId).Distinct().Count()==Items.Length
        && Items.Select(i=>i.WorkId).Distinct().Count()==Items.Length&&Items.Select(i=>i.TenderId).Distinct().Count()==Items.Length
        && Items.Select(i=>i.OrderId).Distinct().Count()==Items.Length
        && Items.All(i=>new[]{i.EventId,i.CorrectionId,i.OriginalSettlementId,i.WorkId,i.OrderId,i.TenderId,i.DrawerId}.All(id=>id!=Guid.Empty)
            && i.PostingDate!=default&&i.PostedAtUtc>=from&&i.PostedAtUtc<to&&i.Adjustment<0&&i.Adjustment>=-999999999999999.9999m&&decimal.Round(i.Adjustment,4)==i.Adjustment)
        && Items.Sum(i=>i.Adjustment)==Adjustment;
    public bool DeliveryComplete=>Missing==0&&Conflicting==0&&Unexpected==0&&Matched==Expected;
    public bool Same(DayCorrectionEvidence other)=>ManifestId==other.ManifestId&&RevisionEpoch==other.RevisionEpoch
        && SourceRevision==other.SourceRevision&&ManifestHash==other.ManifestHash&&Adjustment==other.Adjustment
        && Expected==other.Expected&&Matched==other.Matched&&Missing==other.Missing&&Conflicting==other.Conflicting&&Unexpected==other.Unexpected
        && Items.SequenceEqual(other.Items);
    public DayCorrectionEvidence Copy()=>this with{Items=[..Items]};
}
