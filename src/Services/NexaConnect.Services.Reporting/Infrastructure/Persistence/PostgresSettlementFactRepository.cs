using System.Data;
using System.Globalization;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;
using Npgsql;
namespace NexaConnect.Services.Reporting.Infrastructure.Persistence;

public sealed class PostgresSettlementFactRepository(NpgsqlDataSource source):ISettlementFactRepository
{
    public async Task<bool> ProjectAsync(SettlementFact fact,CancellationToken ct)
    {
        fact.Validate();var hash=FinancialCompleteness.Hash(fact);
        try
        {
            await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
            await using var receipt=new NpgsqlCommand("INSERT INTO settlement_event_receipts(event_id,payload_hash) VALUES($1,$2) ON CONFLICT DO NOTHING",c,tx);
            Add(receipt,fact.EventId,hash);
            if(await receipt.ExecuteNonQueryAsync(ct)==0)
            {
                await using var prior=new NpgsqlCommand("SELECT payload_hash FROM settlement_event_receipts WHERE event_id=$1",c,tx);Add(prior,fact.EventId);
                if((string?)await prior.ExecuteScalarAsync(ct)!=hash)throw new ArgumentException("Settlement event identity conflict.");
                await tx.CommitAsync(ct);return false;
            }
            await using var insert=new NpgsqlCommand("""
                INSERT INTO settlement_facts(event_id,organization_id,restaurant_id,branch_id,settlement_id,business_date,contract_version,payload)
                VALUES($1,$2,$3,$4,$5,$6,$7,$8::jsonb)
                """,c,tx);
            Add(insert,fact.EventId,fact.OrganizationId,fact.RestaurantId,fact.BranchId,fact.SettlementId,fact.BusinessDate,fact.ContractVersion,JsonSerializer.Serialize(fact));
            await insert.ExecuteNonQueryAsync(ct);
            await using var checkpoint=new NpgsqlCommand("""
                INSERT INTO projection_checkpoints(projector_name,source_stream,position,last_event_id,last_event_occurred_at_utc,updated_at_utc)
                VALUES('pos-day-settlement',$1,1,$2,$3,clock_timestamp())
                ON CONFLICT(projector_name,source_stream) DO UPDATE SET position=projection_checkpoints.position+1,
                last_event_id=EXCLUDED.last_event_id,last_event_occurred_at_utc=EXCLUDED.last_event_occurred_at_utc,updated_at_utc=clock_timestamp()
                """,c,tx);
            Add(checkpoint,"pos.branch-day-settled.v"+fact.ContractVersion,fact.EventId,fact.SettledAtUtc);await checkpoint.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);return true;
        }
        catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.UniqueViolation){throw new ArgumentException("Settlement financial identity conflict.",e);}
    }
    public async Task<SettlementReport> CompareAsync(SettlementManifest manifest,CancellationToken ct)
    {
        var range=new SettlementReportRange(manifest.OrganizationId,manifest.BranchId,manifest.FromDate,manifest.ToDate);
        SettlementReporting.ValidateManifest(manifest,range);
        var expected=manifest.Events.Select(SettlementReporting.Translate).OrderBy(f=>f.EventId).ToArray();
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        var facts=new Dictionary<Guid,(SettlementFact Fact,string? Hash)>();
        await using(var q=new NpgsqlCommand("""
            SELECT f.payload::text,r.payload_hash FROM settlement_facts f LEFT JOIN settlement_event_receipts r ON r.event_id=f.event_id
            WHERE (f.organization_id=$1 AND f.branch_id=$2 AND f.business_date >= $3 AND f.business_date < $4)
               OR f.event_id=ANY($5) ORDER BY f.event_id LIMIT 63
            """,c,tx))
        {
            Add(q,range.OrganizationId,range.BranchId,range.FromDate,range.ToDate,expected.Select(f=>f.EventId).ToArray());
            await using var rows=await q.ExecuteReaderAsync(ct);
            while(await rows.ReadAsync(ct))
            {var fact=JsonSerializer.Deserialize<SettlementFact>(rows.GetString(0))??throw new InvalidOperationException();fact.Validate();facts.Add(fact.EventId,(fact,rows.IsDBNull(1)?null:rows.GetString(1)));}
        }
        if(facts.Count>62)throw new ArgumentException("Narrow the settlement projection range.");
        var items=new List<SettlementReportItem>();int matched=0,missing=0,conflicting=0;
        foreach(var e in expected)
        {
            string status;
            if(!facts.TryGetValue(e.EventId,out var actual)){status="missing";missing++;}
            else if(actual.Hash!=FinancialCompleteness.Hash(e)||FinancialCompleteness.Hash(actual.Fact)!=actual.Hash){status="conflicting";conflicting++;}
            else{status="matched";matched++;}
            // Never return foreign/conflicting values. Expected rows come only from the authorized owner.
            items.Add(Item(e,status));
        }
        var ids=expected.Select(f=>f.EventId).ToHashSet();
        if(facts.Values.Any(f=>!ids.Contains(f.Fact.EventId)&&(f.Fact.OrganizationId!=range.OrganizationId||f.Fact.RestaurantId!=manifest.RestaurantId||f.Fact.BranchId!=range.BranchId)))
            throw new InvalidOperationException("Settlement projection scope inconsistent.");
        var unexpected=facts.Values.Where(f=>!ids.Contains(f.Fact.EventId)&&f.Fact.OrganizationId==range.OrganizationId&&f.Fact.RestaurantId==manifest.RestaurantId
            &&f.Fact.BranchId==range.BranchId&&f.Fact.BusinessDate>=range.FromDate&&f.Fact.BusinessDate<range.ToDate).ToArray();
        items.AddRange(unexpected.Select(f=>Item(f.Fact,"unexpected")));
        if(items.Sum(i=>i.Corrections?.Expected??0)>2000)throw new ArgumentException("Narrow the settlement evidence range.");
        await tx.CommitAsync(ct);
        return new(range.OrganizationId,range.BranchId,range.FromDate,range.ToDate,manifest.ObservedAtUtc,DateTimeOffset.UtcNow,
            FinancialCompleteness.Hash(expected),missing+conflicting+unexpected.Length==0?"matched":"gaps",expected.Length,matched,missing,conflicting,unexpected.Length,items);
    }
    private static SettlementReportItem Item(SettlementFact f,string status)
    {
        static string Money(decimal n)=>n.ToString("0.####",CultureInfo.InvariantCulture);
        var c=f.Corrections;
        return new(f.EventId,f.SettlementId,f.BusinessDate,f.SettledAtUtc,f.ApprovalId,f.SealVersion,f.Currency,Money(f.GrossSales),Money(f.CompletedRefunds),Money(f.NetSales),Money(f.CashVariance),
            f.ContractVersion,c is null?"historical_unavailable":"verified",c is null?null:new(c.ManifestId,c.RevisionEpoch,c.SourceRevision,c.ManifestHash,c.Expected,c.Matched,c.Missing,c.Conflicting,c.Unexpected,c.Adjustment,
                c.Items.Select(i=>new SettlementCorrectionItemV2(i.EventId,i.CorrectionId,i.OriginalSettlementId,i.WorkId,i.OrderId,i.TenderId,i.DrawerId,i.PostingDate,i.PostedAtUtc,i.Adjustment)).ToArray()),status,
            f.Sources.Select(s=>new SettlementSourceV1(s.Source,s.SealId,s.Epoch,s.Revision)).ToArray(),f.DecisionId,f.FromUtc,f.ToUtc);
    }
    private static void Add(NpgsqlCommand q,params object[] values){foreach(var v in values)q.Parameters.AddWithValue(v);}
}
