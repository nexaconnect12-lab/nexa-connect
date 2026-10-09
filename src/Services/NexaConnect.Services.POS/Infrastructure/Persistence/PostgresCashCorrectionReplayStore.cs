using NexaConnect.Services.POS.Application.DayClose;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresCashCorrectionReplayStore(NpgsqlDataSource dataSource) : ICashCorrectionReplayStore
{
    public async Task<IReadOnlyList<CashCorrectionReplayEvent>> SelectAsync(CashCorrectionReplayRequest request, CancellationToken ct)
    {
        request.Validate();
        var selected = await new NexaConnect.Services.POS.Infrastructure.DayClose.PostgresCashCorrectionInventoryStore(dataSource)
            .ReadRetainedAsync(request.OrganizationId, request.RestaurantId, request.BranchId, request.FromUtc.ToUniversalTime(), request.ToUtc.ToUniversalTime(), ct);
        if (selected.Manifest.Events.Count > request.Limit) throw new InvalidOperationException("Replay inventory exceeds limit; narrow the range.");
        return selected.Manifest.Events.Select(e => new CashCorrectionReplayEvent(e.EventId, selected.Payloads[e.EventId])).ToArray();
    }
    public async Task StartAsync(Guid runId, CashCorrectionReplayRequest request, Guid operatorId, string reason, string manifest, int count, CancellationToken ct)
    {
        await using var q = dataSource.CreateCommand("""
            INSERT INTO cash_correction_replay_runs(id,organization_id,branch_id,restaurant_id,from_utc,to_utc,operator_id,reason,manifest,event_count,selection_limit)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
            """);
        q.Parameters.AddWithValue(runId); q.Parameters.AddWithValue(request.OrganizationId); q.Parameters.AddWithValue(request.BranchId);
        q.Parameters.AddWithValue(request.RestaurantId); q.Parameters.AddWithValue(request.FromUtc.ToUniversalTime()); q.Parameters.AddWithValue(request.ToUtc.ToUniversalTime());
        q.Parameters.AddWithValue(operatorId); q.Parameters.AddWithValue(reason); q.Parameters.AddWithValue(manifest); q.Parameters.AddWithValue(count);
        q.Parameters.AddWithValue(request.Limit);
        await q.ExecuteNonQueryAsync(ct);
    }
    public async Task AppendAsync(Guid runId, Guid eventId, string outcome, CancellationToken ct)
    {
        await using var q = dataSource.CreateCommand("INSERT INTO cash_correction_replay_attempts(run_id,event_id,outcome) VALUES($1,$2,$3)");
        q.Parameters.AddWithValue(runId); q.Parameters.AddWithValue(eventId); q.Parameters.AddWithValue(outcome);
        await q.ExecuteNonQueryAsync(ct);
    }
}
