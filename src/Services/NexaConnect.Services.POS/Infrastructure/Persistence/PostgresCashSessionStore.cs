using Npgsql;
using NexaConnect.Services.POS.Application.CashSessions;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresCashSessionStore(NpgsqlDataSource dataSource) : ICashSessionStore
{
    public async Task<Guid> OpenAsync(
        Guid shiftId,
        Guid storeId,
        string currency,
        decimal openingAmount,
        CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        const string sql = """
            INSERT INTO cash_sessions
                (id, store_id, shift_id, currency, opening_amount, status,
                 opened_at_utc, created_at_utc, updated_at_utc)
            SELECT $1, $2, shift.id, $3, $4, 'open', now(), now(), now()
            FROM shifts shift
            WHERE shift.id = $5 AND shift.store_id = $2 AND shift.status = 'open';
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(storeId);
        command.Parameters.AddWithValue(currency.ToUpperInvariant());
        command.Parameters.AddWithValue(openingAmount);
        command.Parameters.AddWithValue(shiftId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The shift is not open or does not belong to the store.");
        }

        return id;
    }

    public async Task RecordMovementAsync(
        Guid cashSessionId,
        string movementType,
        decimal amount,
        string recordedBy,
        string? reasonCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO cash_movements
                (id, cash_session_id, movement_type, amount, reason_code, occurred_at_utc, recorded_by)
            SELECT $1, session.id, $2, $3, $4, now(), $5
            FROM cash_sessions session
            WHERE session.id = $6 AND session.status = 'open';
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(movementType);
        command.Parameters.AddWithValue(amount);
        command.Parameters.AddWithValue((object?)reasonCode ?? DBNull.Value);
        command.Parameters.AddWithValue(recordedBy);
        command.Parameters.AddWithValue(cashSessionId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The cash session is not open.");
        }
    }

    public async Task CloseAsync(
        Guid cashSessionId,
        decimal actualClosingAmount,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE cash_sessions
            SET status = 'closed', actual_closing_amount = $2,
                variance_amount = $2 - (opening_amount + COALESCE(
                    (SELECT SUM(CASE WHEN movement_type IN ('sale', 'pay_in', 'float_adjustment')
                                     THEN amount ELSE -amount END)
                     FROM cash_movements WHERE cash_session_id = cash_sessions.id), 0)),
                closed_at_utc = now(), updated_at_utc = now()
            WHERE id = $1 AND status = 'open';
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(cashSessionId);
        command.Parameters.AddWithValue(actualClosingAmount);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The cash session is missing or already closed.");
        }
    }
}
