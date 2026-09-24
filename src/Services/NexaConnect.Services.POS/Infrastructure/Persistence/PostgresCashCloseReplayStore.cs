using NexaConnect.Services.POS.Application.CashReviews;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresCashCloseReplayStore(NpgsqlDataSource dataSource) : ICashCloseReplayStore
{
    public async Task<IReadOnlyList<CashCloseReplayEvent>> SelectAsync(CashCloseReplayRequest request, CancellationToken ct)
    {
        const string sql = """
            SELECT id,payload::text FROM outbox_messages
            WHERE event_type='pos.cash-close.snapshot.v1' AND contract_version=1
              AND payload->>'OrganizationId'=$1 AND payload->>'BranchId'=$2 AND payload->>'StoreId'=$3
              AND occurred_at_utc >= $4 AND occurred_at_utc < $5
            ORDER BY occurred_at_utc,id LIMIT $6
            """;
        await using var q = dataSource.CreateCommand(sql);
        q.Parameters.AddWithValue(request.OrganizationId.ToString("D")); q.Parameters.AddWithValue(request.BranchId.ToString("D"));
        q.Parameters.AddWithValue(request.StoreId.ToString("D")); q.Parameters.AddWithValue(request.FromUtc.ToUniversalTime());
        q.Parameters.AddWithValue(request.ToUtc.ToUniversalTime()); q.Parameters.AddWithValue(request.Limit + 1);
        await using var reader = await q.ExecuteReaderAsync(ct);
        var result = new List<CashCloseReplayEvent>();
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return result;
    }
    public async Task StartAsync(Guid runId, CashCloseReplayRequest request, Guid operatorId, string reason, string manifest, int count, CancellationToken ct)
    {
        await using var q = dataSource.CreateCommand("""
            INSERT INTO cash_close_replay_runs(id,organization_id,branch_id,store_id,from_utc,to_utc,operator_id,reason,manifest,event_count,selection_limit)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
            """);
        q.Parameters.AddWithValue(runId); q.Parameters.AddWithValue(request.OrganizationId); q.Parameters.AddWithValue(request.BranchId);
        q.Parameters.AddWithValue(request.StoreId); q.Parameters.AddWithValue(request.FromUtc.ToUniversalTime()); q.Parameters.AddWithValue(request.ToUtc.ToUniversalTime());
        q.Parameters.AddWithValue(operatorId); q.Parameters.AddWithValue(reason); q.Parameters.AddWithValue(manifest); q.Parameters.AddWithValue(count);
        q.Parameters.AddWithValue(request.Limit);
        await q.ExecuteNonQueryAsync(ct);
    }
    public async Task AppendAsync(Guid runId, Guid eventId, string outcome, CancellationToken ct)
    {
        await using var q = dataSource.CreateCommand("INSERT INTO cash_close_replay_attempts(run_id,event_id,outcome) VALUES($1,$2,$3)");
        q.Parameters.AddWithValue(runId); q.Parameters.AddWithValue(eventId); q.Parameters.AddWithValue(outcome);
        await q.ExecuteNonQueryAsync(ct);
    }
}
