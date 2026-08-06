using Npgsql;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.Shifts;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresShiftStore(NpgsqlDataSource dataSource) : IShiftStore
{
    public async Task<bool> TerminalMatchesAsync(
        Guid branchId,
        Guid storeId,
        Guid terminalId,
        Guid restaurantId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS
            (
                SELECT 1 FROM stores store
                JOIN terminals terminal ON terminal.store_id = store.id
                WHERE store.id = $1 AND store.restaurant_id = $2 AND store.branch_id = $3
                  AND store.operational_status = 'active' AND terminal.id = $4
                  AND terminal.registration_status = 'active'
            );
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(storeId);
        command.Parameters.AddWithValue(restaurantId);
        command.Parameters.AddWithValue(branchId);
        command.Parameters.AddWithValue(terminalId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task CreateAsync(Shift shift, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO shifts
                (id, store_id, terminal_id, employee_identity_subject_id, shift_number, status,
                 opened_at_utc, opened_by, created_at_utc, updated_at_utc, authorization_decision_id)
            VALUES ($1, $2, $3, $4, $5, 'open', $6, $4, $6, $6, $7);
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(shift.Id);
        command.Parameters.AddWithValue(shift.StoreId);
        command.Parameters.AddWithValue(shift.TerminalId);
        command.Parameters.AddWithValue(shift.EmployeeSubject);
        command.Parameters.AddWithValue(shift.ShiftNumber);
        command.Parameters.AddWithValue(shift.OpenedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(shift.AuthorizationDecisionId);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName is "uq_shifts_terminal_open" or "uq_shifts_store_shift_number")
        {
            throw new ShiftConflictException();
        }
    }

    public async Task<ShiftSnapshot?> FindOpenAsync(Guid shiftId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT shift.id, shift.store_id, shift.terminal_id, store.restaurant_id, store.branch_id,
                   shift.employee_identity_subject_id, shift.shift_number, shift.status,
                   shift.opened_at_utc, shift.closed_at_utc, shift.opened_by, shift.closed_by,
                   shift.authorization_decision_id, shift.close_authorization_decision_id,
                   shift.concurrency_version
            FROM shifts shift
            JOIN stores store ON store.id = shift.store_id
            WHERE shift.id = $1 AND shift.status = 'open';
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(shiftId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ShiftSnapshot(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            Enum.Parse<ShiftStatus>(reader.GetString(7), ignoreCase: true),
            ToUtc(reader.GetFieldValue<DateTime>(8)),
            reader.IsDBNull(9) ? null : ToUtc(reader.GetFieldValue<DateTime>(9)),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetGuid(12),
            reader.IsDBNull(13) ? null : reader.GetGuid(13),
            reader.GetInt64(14));
    }

    public async Task<bool> TryCloseAsync(Shift shift, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE shifts
            SET status = 'closed', closed_at_utc = $2, closed_by = $3,
                close_authorization_decision_id = $4, updated_at_utc = $2,
                concurrency_version = concurrency_version + 1
            WHERE id = $1 AND status = 'open' AND concurrency_version = $5;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(shift.Id);
        command.Parameters.AddWithValue(shift.ClosedAtUtc?.UtcDateTime ?? throw new ShiftValidationException("Closed time is required."));
        command.Parameters.AddWithValue(shift.ClosedBy ?? throw new ShiftValidationException("Closed subject is required."));
        command.Parameters.AddWithValue(shift.CloseAuthorizationDecisionId ?? throw new ShiftValidationException("Close authorization is required."));
        command.Parameters.AddWithValue(shift.ConcurrencyVersion - 1);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
