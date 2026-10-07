using NexaConnect.Contracts.Reporting;
using Npgsql;

namespace NexaConnect.Infrastructure.Persistence;

/// <summary>Reads an owning database's revision in the caller's snapshot. No financial policy lives here.</summary>
public static class PostgresFinancialRevision
{
    public static async Task<SourceFinancialRevision> ReadAsync(EndOfDayWindow window,
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("""
            SELECT e.epoch, COALESCE(r.revision, 0)
            FROM source_financial_epoch e
            LEFT JOIN source_financial_revisions r ON r.restaurant_id=$1 AND r.branch_id=$2
            WHERE e.singleton=true
            """, connection, transaction);
        query.Parameters.AddWithValue(window.RestaurantId);
        query.Parameters.AddWithValue(window.BranchId);
        await using var rows = await query.ExecuteReaderAsync(ct);
        if (!await rows.ReadAsync(ct)) throw new InvalidOperationException("Financial revision epoch unavailable.");
        return new(rows.GetGuid(0), rows.GetInt64(1));
    }

    public static bool IsCurrent<T>(SourceCutoff<T> saved, SourceCutoff<T> current) =>
        saved.EvidenceProtocolVersion == 2 && current.EvidenceProtocolVersion == 2
        && saved.SourceRevision is { Epoch: var epoch, Revision: >= 0 } && epoch != Guid.Empty
        && saved.SourceRevision == current.SourceRevision
        && saved.EvidenceVersion.Length == 64 && saved.EvidenceVersion == current.EvidenceVersion;
}
