namespace NexaConnect.Services.Reporting.Domain;

public sealed record SettlementTender(string Method,string Currency,decimal Amount);
public sealed record SettlementSource(string Source,Guid SealId,Guid Epoch,long Revision);
public sealed record SettlementCorrectionItem(Guid EventId,Guid CorrectionId,Guid OriginalSettlementId,Guid WorkId,
    Guid OrderId,Guid TenderId,Guid DrawerId,DateOnly PostingDate,DateTimeOffset PostedAtUtc,decimal Adjustment);
public sealed record SettlementCorrection(Guid ManifestId,Guid RevisionEpoch,long SourceRevision,string ManifestHash,
    int Expected,int Matched,int Missing,int Conflicting,int Unexpected,decimal Adjustment,SettlementCorrectionItem[] Items);
public sealed record SettlementFact(Guid EventId,Guid CorrelationId,Guid SettlementId,Guid OrganizationId,Guid RestaurantId,Guid BranchId,
    DateOnly BusinessDate,DateTimeOffset SettledAtUtc,Guid ApprovalId,long SealVersion,string Currency,
    decimal GrossSales,decimal CompletedRefunds,decimal NetSales,decimal CashVariance,SettlementTender[] Tenders,
    SettlementSource[] Sources,int ContractVersion,Guid? DecisionId,DateTimeOffset? FromUtc,DateTimeOffset? ToUtc,SettlementCorrection? Corrections)
{
    public void Validate()
    {
        static bool Money(decimal n)=>decimal.Round(n,4)==n&&Math.Abs(n)<=999999999999999999.9999m;
        if(new[]{EventId,CorrelationId,SettlementId,OrganizationId,RestaurantId,BranchId,ApprovalId}.Any(id=>id==Guid.Empty)
            ||BusinessDate==default||SettledAtUtc==default||SealVersion<=0||Currency!="THB"
            ||GrossSales<0||CompletedRefunds<0||NetSales!=GrossSales-CompletedRefunds
            ||new[]{GrossSales,CompletedRefunds,NetSales,CashVariance}.Any(n=>!Money(n))
            ||Tenders is null||Tenders.Length>32||Tenders.Any(t=>t is null||string.IsNullOrWhiteSpace(t.Method)||t.Method.Length>40||t.Currency!="THB"||t.Amount<0||!Money(t.Amount))
            ||Tenders.Select(t=>(t.Method,t.Currency)).Distinct().Count()!=Tenders.Length
            ||Sources is null||Sources.Length!=3||Sources.Select(s=>s.Source).Distinct().Count()!=3
            ||Sources.Any(s=>s is null||s.Source is not("Order" or "Payment" or "POS")||s.SealId==Guid.Empty||s.Epoch==Guid.Empty||s.Revision<0))
            throw new ArgumentException("Invalid settlement financial evidence.");
        if(ContractVersion==1)
        {
            if(DecisionId is not null||FromUtc is not null||ToUtc is not null||Corrections is not null)throw new ArgumentException("Legacy evidence cannot be enriched.");
            return;
        }
        var c=Corrections;var pos=Sources.Single(s=>s.Source=="POS");
        if(ContractVersion!=2||DecisionId is null||DecisionId==Guid.Empty||FromUtc is null||ToUtc is null
            ||FromUtc>=ToUtc||ToUtc-FromUtc>TimeSpan.FromHours(27)||SettledAtUtc<ToUtc||c is null
            ||c.ManifestId==Guid.Empty||c.RevisionEpoch!=pos.Epoch||c.SourceRevision!=pos.Revision
            ||c.ManifestHash is not{Length:64}||c.ManifestHash.Any(ch=>!char.IsAsciiHexDigit(ch))
            ||c.Expected<0||c.Expected>1000||c.Expected!=c.Matched||c.Missing!=0||c.Conflicting!=0||c.Unexpected!=0
            ||c.Items is null||c.Items.Length!=c.Expected||c.Adjustment>0||!Money(c.Adjustment)
            ||c.Items.Any(i=>i is null||new[]{i.EventId,i.CorrectionId,i.OriginalSettlementId,i.WorkId,i.OrderId,i.TenderId,i.DrawerId}.Any(id=>id==Guid.Empty)
                ||i.PostingDate==default||i.PostedAtUtc<FromUtc||i.PostedAtUtc>=ToUtc||i.Adjustment>=0||i.Adjustment< -999999999999999.9999m||!Money(i.Adjustment))
            ||c.Items.Select(i=>i.EventId).Distinct().Count()!=c.Expected||c.Items.Select(i=>i.CorrectionId).Distinct().Count()!=c.Expected
            ||c.Items.Select(i=>i.WorkId).Distinct().Count()!=c.Expected||c.Items.Select(i=>i.OrderId).Distinct().Count()!=c.Expected
            ||c.Items.Select(i=>i.TenderId).Distinct().Count()!=c.Expected||c.Items.Sum(i=>i.Adjustment)!=c.Adjustment)
            throw new ArgumentException("Invalid settled correction evidence.");
    }
}
