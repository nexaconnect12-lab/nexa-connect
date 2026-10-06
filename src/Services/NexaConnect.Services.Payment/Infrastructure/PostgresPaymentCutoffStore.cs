using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Payment.Application.Refunds;
using Npgsql;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class PostgresPaymentCutoffStore(NpgsqlDataSource source) : IPaymentCutoffStore
{
    private readonly PostgresSnapshotRetention<PaymentDaySummary> retention=new(source);
    public Task<SourceCutoff<PaymentDaySummary>> CaptureAsync(SourceCutoffCommand command,string actor,CancellationToken ct)=>
        retention.CaptureAsync(command,actor,(c,t,token)=>Snapshot(command.Window,c,t,token),ct);
    public async Task<SourceCutoffRead<PaymentDaySummary>?> ReadAsync(EndOfDayWindow window,Guid id,CancellationToken ct)
    {
        var saved=await retention.ReadAsync(window,id,ct);if(saved is null)return null;
        await using var c=await source.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var current=await Snapshot(window,c,t,ct);await t.CommitAsync(ct);
        return new(saved,saved.EvidenceVersion.Length==64&&saved.EvidenceVersion==current.EvidenceVersion);
    }
    private static async Task<SourceCutoff<PaymentDaySummary>> Snapshot(EndOfDayWindow w,NpgsqlConnection c,NpgsqlTransaction t,CancellationToken ct)
    {
        string[] retainedRows=[];var summary=await PostgresPaymentDayReader.QueryAsync(w,c,t,ct,e=>retainedRows=e);
        var events=new List<PaymentRefundedV1>();var refunds=new List<PaymentRefund>();int gaps=0;
        await using(var query=new NpgsqlCommand(PostgresPaymentRefunds.Select+" WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND status='completed' AND completed_at_utc>=$4 AND completed_at_utc<$5 ORDER BY id LIMIT 10001",c,t))
        {
            foreach(var value in new object[]{w.OrganizationId,w.RestaurantId,w.BranchId,w.FromUtc.ToUniversalTime(),w.ToUtc.ToUniversalTime()})query.Parameters.AddWithValue(value);
            await using var rows=await query.ExecuteReaderAsync(ct);
            while(await rows.ReadAsync(ct))refunds.Add(PostgresPaymentRefunds.Map(rows));
        }
        if(refunds.Count>10000)throw new InvalidOperationException("Payment cutoff inventory exceeds bound.");
        foreach(var refund in refunds)
        {
            await using var query=new NpgsqlCommand("SELECT payload::text FROM refund_financial_publications WHERE refund_id=$1",c,t);query.Parameters.AddWithValue(refund.Id);
            try
            {
                if(await query.ExecuteScalarAsync(ct) is not string json){gaps++;continue;}
                var value=JsonSerializer.Deserialize<PaymentRefundedV1>(json)??throw new JsonException();
                RefundFinancialEvidence.Validate(refund,value);events.Add(value);
            }
            catch(Exception e)when(e is JsonException or ArgumentException or InvalidOperationException or NullReferenceException){gaps++;}
        }
        summary=summary with{EvidenceGaps=Math.Max(summary.EvidenceGaps,gaps)};
        return new(Guid.Empty,Guid.Empty,0,w,DateTimeOffset.UtcNow,summary.EvidenceVersion??"",summary,[],events,retainedRows);
    }
}
