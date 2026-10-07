using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.POS.Application.CashReviews;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresPosCutoffStore(NpgsqlDataSource source) : IPosCutoffStore
{
    private readonly PostgresSnapshotRetention<PosDaySummary> retention=new(source);
    public Task<SourceCutoff<PosDaySummary>> CaptureAsync(SourceCutoffCommand command,string actor,CancellationToken ct)=>
        retention.CaptureAsync(command,actor,async(c,t,token)=>
        {
            string[] rows=[];var summary=await PostgresPosDayReader.QueryAsync(command.Window,c,t,token,e=>rows=e);
            var revision=await PostgresFinancialRevision.ReadAsync(command.Window,c,t,token);
            return new(Guid.Empty,Guid.Empty,0,command.Window,DateTimeOffset.UtcNow,summary.EvidenceVersion??"",summary,[],[],rows,revision,2);
        },ct);
    public async Task<SourceCutoffRead<PosDaySummary>?> ReadAsync(EndOfDayWindow window,Guid id,CancellationToken ct)
    {
        var saved=await retention.ReadAsync(window,id,ct);if(saved is null)return null;
        await using var c=await source.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var summary=await PostgresPosDayReader.QueryAsync(window,c,t,ct);
        var revision=await PostgresFinancialRevision.ReadAsync(window,c,t,ct);
        await t.CommitAsync(ct);
        return new(saved,PostgresFinancialRevision.IsCurrent(saved,saved with
        {EvidenceVersion=summary.EvidenceVersion??"",SourceRevision=revision,EvidenceProtocolVersion=2}));
    }
}
