using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Domain.DayClose;
using Npgsql;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;

public sealed class PostgresSettlementInventoryStore(NpgsqlDataSource source):ISettlementInventoryStore
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions PublicationJson=new(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public async Task<SettlementManifest> ReadAsync(SettlementReportRange range,Guid restaurant,CancellationToken ct)=>
        (await ReadRetainedAsync(range,restaurant,ct)).Manifest;
    public async Task<(SettlementManifest Manifest,IReadOnlyDictionary<Guid,string> Payloads)> ReadRetainedAsync(SettlementReportRange range,Guid restaurant,CancellationToken ct)
    {
        SettlementInventory.Validate(range);if(restaurant==Guid.Empty)throw new ArgumentException();
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        var selected=await QueryRetainedAsync(range,restaurant,c,tx,ct);await tx.CommitAsync(ct);return selected;
    }
    internal static async Task<(SettlementManifest Manifest,IReadOnlyDictionary<Guid,string> Payloads)> QueryRetainedAsync(
        SettlementReportRange range,Guid restaurant,NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {
        SettlementInventory.Validate(range);if(restaurant==Guid.Empty)throw new ArgumentException();
        await using var q=new NpgsqlCommand("""
            SELECT r.receipt::text,o.event_type,o.contract_version,o.payload::text,o.aggregate_id,o.correlation_id,
                   s.business_date,s.id,o.occurred_at_utc,d.decision_id,o.aggregate_type
            FROM branch_day_settlements s JOIN branch_day_settlement_decisions d
              ON d.organization_id=s.organization_id AND d.settlement_id=s.id AND d.decision='commit'
            LEFT JOIN branch_day_settlement_receipts r
              ON r.organization_id=s.organization_id AND r.settlement_id=s.id
            LEFT JOIN outbox_messages o ON o.id=(r.receipt->>'eventId')::uuid
            WHERE s.organization_id=$1 AND s.restaurant_id=$2 AND s.branch_id=$3 AND s.business_date >= $4 AND s.business_date < $5
            ORDER BY s.business_date,s.id LIMIT 32
            """,c,tx);
        foreach(var v in new object[]{range.OrganizationId,restaurant,range.BranchId,range.FromDate,range.ToDate})q.Parameters.AddWithValue(v);
        var events=new List<SettlementPublication>();var payloads=new Dictionary<Guid,string>();
        await using(var rows=await q.ExecuteReaderAsync(ct))
        while(await rows.ReadAsync(ct))
        {
            if(rows.IsDBNull(0))throw new InvalidOperationException("Committed settlement receipt absent.");
            var receipt=JsonSerializer.Deserialize<SettlementReceipt>(rows.GetString(0),Json)??throw new InvalidOperationException();
            if(rows.IsDBNull(1)||receipt.Identity!=new DayIdentity(range.OrganizationId,restaurant,range.BranchId,rows.GetFieldValue<DateOnly>(6))
                ||receipt.SettlementId!=rows.GetGuid(7)||receipt.DecisionId!=rows.GetGuid(9)||rows.GetString(10)!="branch-day-settlement"||rows.GetGuid(4)!=receipt.SettlementId||(rows.IsDBNull(5)||!Guid.TryParse(rows.GetString(5),out var correlation)||correlation!=receipt.CorrelationId)
                ||Math.Abs((rows.GetFieldValue<DateTimeOffset>(8)-receipt.SettledAtUtc).Ticks)>10)throw new InvalidOperationException("Original settlement publication absent or inconsistent.");
            var payload=rows.GetString(3);var version=rows.GetInt32(2);SettlementPublication original;
            if(version==1&&rows.GetString(1)=="pos.branch-day-settled.v1")
            {
                var value=JsonSerializer.Deserialize<BranchDaySettledV1>(payload,PublicationJson)??throw new InvalidOperationException();
                if(JsonSerializer.Serialize(value,Json)!=JsonSerializer.Serialize(SettlementPublicationFactory.Legacy(receipt),Json))throw new InvalidOperationException("Legacy settlement provenance changed.");
                original=new(value,null);
            }
            else if(version==2&&rows.GetString(1)=="pos.branch-day-settled.v2")
            {
                var value=JsonSerializer.Deserialize<BranchDaySettledV2>(payload,PublicationJson)??throw new InvalidOperationException();
                if(JsonSerializer.Serialize(value,Json)!=JsonSerializer.Serialize(SettlementPublicationFactory.Current(receipt),Json))throw new InvalidOperationException("Settlement proof provenance changed.");
                original=new(null,value);
            }
            else throw new InvalidOperationException("Settlement publication version unsupported.");
            events.Add(original);payloads.Add(receipt.EventId,payload);
        }
        if(events.Count>31||events.Sum(e=>e.V2?.Corrections.Expected??0)>1000)throw new ArgumentException("Narrow the settlement date range.");
        var manifest=new SettlementManifest(range.OrganizationId,restaurant,range.BranchId,range.FromDate,range.ToDate,DateTimeOffset.UtcNow,events);
        if(JsonSerializer.SerializeToUtf8Bytes(manifest,Json).Length>2*1024*1024)throw new ArgumentException("Narrow the settlement evidence range.");
        return (manifest,payloads);
    }
}
