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
            var w=command.Window;
            var corrections=(await NexaConnect.Services.POS.Infrastructure.DayClose.PostgresCashCorrectionInventoryStore.QueryRetainedAsync(w.OrganizationId,w.RestaurantId,w.BranchId,w.FromUtc,w.ToUtc,c,t,token)).Manifest;
            if(corrections.Events.Count!=summary.LateCashCorrections || corrections.Events.Sum(e=>e.CashVarianceAdjustment)!=summary.LateCashCorrectionAdjustment)
                throw new InvalidOperationException("Correction inventory disagrees with its owning summary.");
            return new(Guid.Empty,Guid.Empty,0,command.Window,DateTimeOffset.UtcNow,summary.EvidenceVersion??"",summary,[],[],rows,revision,3,corrections);
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
        {EvidenceVersion=summary.EvidenceVersion??"",SourceRevision=revision,EvidenceProtocolVersion=3}));
    }
    public Task<SourceSealRead<PosDaySummary>?> ReadSealAsync(EndOfDayWindow window,Guid id,CancellationToken ct)=>
        new PostgresEvidenceSeals<PosDaySummary>(source).ReadAsync(window,id,ct,json=>FinancialChangeAttribution.Classify(json,window.FromUtc,window.ToUtc),
            (c,t,token)=>PostgresPosDayReader.QueryAsync(window,c,t,token));
    public Task<SourceDaySeal> SealAsync(SourceSealCommand command,string actor,CancellationToken ct)=>
        new PostgresEvidenceSeals<PosDaySummary>(source).RetainAsync(command,actor,(m,r)=>
        {
            try
            {
                if(m.EvidenceProtocolVersion!=3 || m.CashCorrections is null)throw new InvalidOperationException("Fresh correction-aware capture required.");
                NexaConnect.Services.POS.Domain.FinancialDaySeal.Retain(command.ManifestId,command.ExpectedRevision.Epoch,
                    command.ExpectedRevision.Revision,m.ManifestId,r.Epoch,r.Revision,PostgresFinancialRevision.IsCurrent(m,m with{SourceRevision=r}),
                    m.Summary.Window==command.Window && m.Summary.Currencies.All(c=>c=="THB"),m.Summary.OpenShifts+m.Summary.OpenCashSessions+m.Summary.PendingCashReviews);
            }
            catch(InvalidOperationException){throw new SnapshotOperationConflictException();}
        },ct);
}
