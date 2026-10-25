using NexaConnect.Services.POS.Application.Terminals;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresManualTenderAdmissionStore(NpgsqlDataSource source) : IManualTenderAdmissionStore
{
    public async Task<bool> IsEligibleAsync(ManualTenderAdmissionRequest request, DateTimeOffset at, CancellationToken ct)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM terminals terminal JOIN stores store ON store.id=terminal.store_id
                JOIN shifts shift ON shift.store_id=store.id AND shift.terminal_id=terminal.id
                WHERE terminal.id=$1 AND terminal.restaurant_id=$2 AND store.restaurant_id=$2 AND store.branch_id=$3
                  AND terminal.registration_status='active' AND store.operational_status='active'
                  AND shift.employee_identity_subject_id=$4 AND shift.status='open' AND shift.opened_at_utc<=$5
                  AND shift.closed_at_utc IS NULL
                  AND ($8::uuid IS NULL OR shift.id=$8) AND ($9::uuid IS NULL OR store.id=$9)
                  AND ($6='promptpay_manual' OR EXISTS (
                      SELECT 1 FROM cash_sessions session WHERE session.shift_id=shift.id AND session.store_id=store.id
                        AND session.status='open' AND session.closed_at_utc IS NULL AND session.opened_at_utc<=$5
                        AND btrim(session.currency)=$7)))
            """;
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(request.TerminalId);
        command.Parameters.AddWithValue(request.RestaurantId);
        command.Parameters.AddWithValue(request.BranchId);
        command.Parameters.AddWithValue(request.OperatorSubjectId);
        command.Parameters.AddWithValue(at);
        command.Parameters.AddWithValue(request.Method);
        command.Parameters.AddWithValue(request.Currency);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlTypes.NpgsqlDbType.Uuid, Value=(object?)request.ExpectedShiftId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlTypes.NpgsqlDbType.Uuid, Value=(object?)request.ExpectedStoreId ?? DBNull.Value });
        return await command.ExecuteScalarAsync(ct) is true;
    }
}

