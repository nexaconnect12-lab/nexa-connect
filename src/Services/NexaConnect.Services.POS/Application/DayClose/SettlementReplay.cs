using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;

namespace NexaConnect.Services.POS.Application.DayClose;

public sealed record SettlementReplayRequest(Guid OrganizationId,Guid RestaurantId,Guid BranchId,DateOnly FromDate,DateOnly ToDate,int Limit)
{
    public void Validate()
    {
        SettlementInventory.Validate(new(OrganizationId,BranchId,FromDate,ToDate));
        if(RestaurantId==Guid.Empty||Limit is <1 or >31)throw new ArgumentException("Invalid settlement replay selection.");
    }
}
public sealed record SettlementReplayEvent(Guid Id,int ContractVersion,string Payload)
{public override string ToString()=>"SettlementReplayEvent [redacted]";}
public sealed record SettlementReplayPlan(string Manifest,int Count);
public sealed class SettlementReplaySelectionChangedException():Exception("Settlement replay selection changed; repeat preview.");
public sealed class SettlementReplayInterruptedException(Guid runId):Exception("Settlement replay interrupted; inspect its durable audit if recorded.")
{public Guid RunId{get;}=runId;}
public interface ISettlementReplayStore
{
    Task<IReadOnlyList<SettlementReplayEvent>> SelectAsync(SettlementReplayRequest request,CancellationToken ct);
    // Selection validation and durable intent share one owning transaction.
    Task<IReadOnlyList<SettlementReplayEvent>> BeginAsync(Guid runId,SettlementReplayRequest request,Guid operatorId,string reason,string expectedManifest,CancellationToken ct);
    Task AppendAsync(Guid runId,Guid eventId,string outcome,CancellationToken ct);
}
public interface ISettlementReplayTransport
{Task PublishAsync(SettlementReplayEvent value,CancellationToken ct);}

public sealed class SettlementReplay(ISettlementReplayStore store,ISettlementReplayTransport transport)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public static BranchDaySettledV1 ReadEnvelope(SettlementReplayEvent value)
    {
        if(value is null||value.Id==Guid.Empty||value.Payload is null||Encoding.UTF8.GetByteCount(value.Payload)>2*1024*1024)
            throw new ArgumentException("Invalid original settlement envelope.");
        BranchDaySettledV1 result;
        if(value.ContractVersion==1)result=JsonSerializer.Deserialize<BranchDaySettledV1>(value.Payload,Json)??throw new ArgumentException();
        else if(value.ContractVersion==2)
        {
            var v2=JsonSerializer.Deserialize<BranchDaySettledV2>(value.Payload,Json)??throw new ArgumentException();
            result=v2.Settlement??throw new ArgumentException();
            if(v2.EventId!=result.EventId||v2.CorrelationId!=result.CorrelationId||v2.OccurredAtUtc!=result.OccurredAtUtc||v2.Corrections is null)
                throw new ArgumentException("Original settlement version identity mismatch.");
        }
        else throw new ArgumentException("Unsupported settlement publication version.");
        if(result.EventId!=value.Id||result.CorrelationId==Guid.Empty||result.SettlementId==Guid.Empty||result.OccurredAtUtc==default)
            throw new ArgumentException("Original settlement identity absent.");
        return result;
    }
    public static SettlementReplayPlan Plan(SettlementReplayRequest request,IReadOnlyList<SettlementReplayEvent> events)
    {
        request.Validate();
        if(events is null||events.Count>request.Limit||events.Select(e=>e?.Id).Distinct().Count()!=events.Count)
            throw new ArgumentException("Settlement replay inventory exceeds its limit or repeats identity.");
        var financial=new List<BranchDaySettledV1>();long bytes=0;
        foreach(var e in events)
        {
            var s=ReadEnvelope(e);bytes+=Encoding.UTF8.GetByteCount(e.Payload);
            if(s.OrganizationId!=request.OrganizationId||s.RestaurantId!=request.RestaurantId||s.BranchId!=request.BranchId
                ||s.BusinessDate<request.FromDate||s.BusinessDate>=request.ToDate||s.Currency!="THB")throw new ArgumentException("Original settlement selection scope mismatch.");
            financial.Add(s);
        }
        if(bytes>2*1024*1024||financial.Select(s=>s.SettlementId).Distinct().Count()!=events.Count||financial.Select(s=>s.BusinessDate).Distinct().Count()!=events.Count)
            throw new ArgumentException("Narrow the original settlement selection.");
        var digest=JsonSerializer.SerializeToUtf8Bytes(new{request.OrganizationId,request.RestaurantId,request.BranchId,request.FromDate,request.ToDate,request.Limit,
            Events=events.OrderBy(e=>e.Id).Select(e=>new{e.Id,e.ContractVersion,Hash=PayloadHash(e)}).ToArray()});
        return new(Convert.ToHexString(SHA256.HashData(digest)),events.Count);
    }
    public static string PayloadHash(SettlementReplayEvent e)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(e.Payload)));
    public Task<SettlementReplayPlan> PreviewAsync(SettlementReplayRequest request,CancellationToken ct)=>PreviewCore(request,ct);
    private async Task<SettlementReplayPlan> PreviewCore(SettlementReplayRequest request,CancellationToken ct)
    {request.Validate();return Plan(request,await store.SelectAsync(request,ct));}
    public static void ValidateExecution(SettlementReplayRequest request,Guid operatorId,string reason,string manifest)
    {
        request.Validate();
        if(operatorId==Guid.Empty||reason is not("retry" or "rebuild")||manifest is not{Length:64}||manifest.Any(c=>!char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Invalid settlement replay attribution or manifest.");
    }
    public async Task<Guid> ExecuteAsync(SettlementReplayRequest request,Guid operatorId,string reason,string expectedManifest,CancellationToken ct)
    {
        ValidateExecution(request,operatorId,reason,expectedManifest);var run=Guid.NewGuid();
        try
        {
            var events=await store.BeginAsync(run,request,operatorId,reason,expectedManifest,ct);
            if(Plan(request,events).Manifest!=expectedManifest)throw new SettlementReplaySelectionChangedException();
            foreach(var e in events.OrderBy(e=>e.Id))
            {
                await store.AppendAsync(run,e.Id,"started",ct);
                await transport.PublishAsync(e,ct);
                await store.AppendAsync(run,e.Id,"confirmed",ct);
            }
        }
        catch(SettlementReplaySelectionChangedException){throw;}
        catch(Exception){throw new SettlementReplayInterruptedException(run);}
        return run;
    }
}
