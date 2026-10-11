extern alias POS;
extern alias REPORTING;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using Npgsql;
using P=POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using R=REPORTING::NexaConnect.Services.Reporting.Application;
using RD=REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;
namespace NexaConnect.IntegrationTests;

public sealed partial class DaySealPostgresTests
{
    [ReportingDatabaseFact]
    public async Task Correction_manifest_hash_is_independently_verified_for_cutoff_and_sealed_readiness()
    {
        forgeCorrectionHash=true;
        var blocked=await Reviewed();Assert.Equal("blocked",blocked.Status);
        forgeCorrectionHash=false;
        var ready=await Workflow.PrepareAsync(organization,Command(blocked.Version),new("manager","token"),default);
        Assert.Equal("ready_for_review",ready.Status);
        var sealedDay=await Sealing.SealAsync(organization,SealCommand(ready.Version),new("manager","token"),default);
        Assert.Equal("ready_for_review",sealedDay.Status);
        forgeCorrectionHash=true;
        var current=await Sealing.ReadAsync(organization,branch,Date,new("manager","token"),default);
        Assert.Equal("blocked",current!.Status);
    }

    [ReportingDatabaseFact]
    public async Task Correction_posting_window_requires_exact_delivery_and_carries_proof_through_settlement_with_a_controlled_calendar()
    {
        var oldDay=Day;var publication=await PostedCorrectionManifest();var original=JsonSerializer.Serialize((await Settlements.ReadAsync(oldDay,default))!.Receipt);
        correctionTestDate=DateOnly.FromDateTime(DateTime.UtcNow);
        // Owning endpoints accept closed UTC cuts. This test calendar uses a completed observation interval;
        // it does not claim production can finalize the current whole branch-local business day.
        var cut=DateTimeOffset.UtcNow;cut=new(cut.UtcTicks-cut.UtcTicks%10,TimeSpan.Zero);
        correctionEvidenceTestWindow=new(organization,restaurant,branch,new(DateTime.UtcNow.Date),cut);
        var blocked=await Reviewed();Assert.Equal("blocked",blocked.Status);Assert.Contains("correction_delivery_missing",blocked.Blockers);
        var retained=blocked.Snapshot!.Corrections!;Assert.Equal(1,retained.Expected);Assert.Equal(-10,retained.Adjustment);Assert.Equal(1,retained.Missing);
        var source=(await new P.PostgresPosCutoffStore(pos).ReadAsync(Window,retained.ManifestId,default))!.Manifest;
        Assert.Equal(3,source.EvidenceProtocolVersion);Assert.Equal(publication.Events.Single(),Assert.Single(source.CashCorrections!.Events));
        Assert.Equal(retained.SourceRevision,source.SourceRevision!.Revision);Assert.Equal(retained.RevisionEpoch,source.SourceRevision.Epoch);
        var projector=new RD.PostgresCashCorrectionFactRepository(reporting);
        Assert.True(await projector.ProjectAsync(R.CashCorrectionReporting.Translate(publication.Events.Single()),default));
        Assert.False(await projector.ProjectAsync(R.CashCorrectionReporting.Translate(publication.Events.Single()),default));
        var unchanged=(await new P.PostgresPosCutoffStore(pos).CaptureAsync(new(source.OperationId,Window),"manager",default));
        Assert.Equal(source.ManifestId,unchanged.ManifestId);
        var ready=await Workflow.PrepareAsync(organization,Command(blocked.Version),new("manager","token"),default);
        Assert.Equal("ready_for_review",ready.Status);Assert.Equal(1,ready.Snapshot!.Corrections!.Matched);
        Assert.Equal(retained.Items,ready.Snapshot.Corrections.Items);Assert.Equal(retained.Adjustment,ready.Snapshot.Corrections.Adjustment);
        var sealedDay=await Sealing.SealAsync(organization,SealCommand(ready.Version),new("manager","token"),default);
        Assert.Equal("ready_for_review",sealedDay.Status);Assert.NotNull(sealedDay.Snapshot!.Corrections);
        var approved=await Approvals.ApproveAsync(organization,ApprovalCommand(sealedDay.Version),new("manager","token"),default);
        var prepare=new POS::NexaConnect.Services.POS.Domain.DayClose.FinalizationCommand(branch,Date,Guid.NewGuid(),0,approved.Decision!.ApprovalId,approved.Version);
        var prepared=await Finalizations().PrepareAsync(organization,prepare,new("manager","token"),default);Assert.Equal("prepared",prepared.Status);
        var final=(await FinalizeSettlement(new(branch,Date,Guid.NewGuid(),prepared.Version,prepare.OperationId,prepare.ApprovalId,prepare.ReviewedApprovalVersion))).Settlement!;
        Assert.Equal("finalized",final.Status);Assert.Equal(ready.Snapshot.Corrections.Items,final.Receipt!.Snapshot.Corrections!.Items);
        Assert.Equal(ready.Snapshot.Corrections.Adjustment,final.Receipt.Snapshot.Corrections.Adjustment);
        Assert.Equal(1,final.Receipt.Snapshot.Corrections.Matched);Assert.Equal(0,final.Receipt.Snapshot.Corrections.Missing);
        Assert.Equal(original,JsonSerializer.Serialize((await Settlements.ReadAsync(oldDay,default))!.Receipt));
        Assert.Equal(1,await Count(pos,"late_cash_corrections"));Assert.Equal(0,await Count(pos,"cash_movements"));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0022_correction_day_evidence/down.sql"))));
    }

    [ReportingDatabaseFact]
    public async Task Correction_protocol_upgrade_preserves_legacy_payload_and_requires_a_new_capture()
    {
        var migration=Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0022_correction_day_evidence");
        await Sql(pos,File.ReadAllText(Path.Combine(migration,"down.sql")));
        var command=new SourceCutoffCommand(Guid.NewGuid(),Window);
        var legacy=await new NexaConnect.Infrastructure.Persistence.PostgresSnapshotRetention<PosDaySummary>(pos).CaptureAsync(command,"manager",async(c,t,ct)=>
        {
            var summary=await P.PostgresPosDayReader.QueryAsync(Window,c,t,ct);var revision=await NexaConnect.Infrastructure.Persistence.PostgresFinancialRevision.ReadAsync(Window,c,t,ct);
            return new(Guid.Empty,Guid.Empty,0,Window,DateTimeOffset.UtcNow,summary.EvidenceVersion!,summary,[],[],SourceRevision:revision,EvidenceProtocolVersion:2);
        },default);
        await using var query=pos.CreateCommand("SELECT payload::text FROM source_day_cutoffs WHERE id=$1");query.Parameters.AddWithValue(legacy.ManifestId);
        var original=(string)(await query.ExecuteScalarAsync())!;
        await Sql(pos,File.ReadAllText(Path.Combine(migration,"up.sql")));
        var store=new P.PostgresPosCutoffStore(pos);var oldRead=(await store.ReadAsync(Window,legacy.ManifestId,default))!;
        Assert.False(oldRead.Current);Assert.Equal(2,oldRead.Manifest.EvidenceProtocolVersion);Assert.Null(oldRead.Manifest.CashCorrections);
        Assert.Equal(legacy.ManifestId,(await store.CaptureAsync(command,"manager",default)).ManifestId);Assert.Equal(original,(string)(await query.ExecuteScalarAsync())!);
        var fresh=await store.CaptureAsync(new(Guid.NewGuid(),Window),"manager",default);Assert.Equal(3,fresh.EvidenceProtocolVersion);Assert.Empty(fresh.CashCorrections!.Events);
        Assert.True((await store.ReadAsync(Window,fresh.ManifestId,default))!.Current);
        await Assert.ThrowsAsync<NexaConnect.Infrastructure.Persistence.SnapshotOperationConflictException>(()=>store.SealAsync(new(Guid.NewGuid(),Window,legacy.ManifestId,legacy.SourceRevision!),"manager",default));
    }
}
