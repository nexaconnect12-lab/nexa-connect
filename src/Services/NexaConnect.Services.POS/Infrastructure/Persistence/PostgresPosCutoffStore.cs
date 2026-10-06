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
            return new(Guid.Empty,Guid.Empty,0,command.Window,DateTimeOffset.UtcNow,summary.EvidenceVersion??"",summary,[],[],rows);
        },ct);
    public async Task<SourceCutoffRead<PosDaySummary>?> ReadAsync(EndOfDayWindow window,Guid id,CancellationToken ct)
    {
        var saved=await retention.ReadAsync(window,id,ct);if(saved is null)return null;
        var current=await new PostgresPosDayReader(source).ReadAsync(window,ct);
        return new(saved,saved.EvidenceVersion.Length==64&&saved.EvidenceVersion==current.EvidenceVersion);
    }
}
