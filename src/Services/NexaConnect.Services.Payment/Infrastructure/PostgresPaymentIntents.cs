using NexaConnect.Services.Payment.Application.Intents;
using Npgsql;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class PostgresPaymentIntents(NpgsqlDataSource dataSource) : IPaymentIntents
{
    public PaymentIntent Create(CreatePaymentIntent command)
    {
        if (command.RestaurantId == Guid.Empty || command.BranchId == Guid.Empty || command.OrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.Amount <= 0 || string.IsNullOrWhiteSpace(command.Currency))
            throw new ArgumentException("Restaurant, branch, order, idempotency key, amount, and currency are required.");
        const string sql = """
            INSERT INTO payment_intents
                (id, restaurant_id, branch_id, order_id, idempotency_key, amount, currency, payment_method, status, created_at_utc, updated_at_utc)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'pending', now(), now())
            ON CONFLICT (restaurant_id, idempotency_key)
            DO UPDATE SET updated_at_utc = payment_intents.updated_at_utc
            RETURNING id, restaurant_id, branch_id, order_id, amount, currency, payment_method, status, created_at_utc;
            """;
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using var databaseCommand = new NpgsqlCommand(sql, connection);
        databaseCommand.Parameters.AddWithValue(Guid.NewGuid());
        databaseCommand.Parameters.AddWithValue(command.RestaurantId);
        databaseCommand.Parameters.AddWithValue(command.BranchId);
        databaseCommand.Parameters.AddWithValue(command.OrderId);
        databaseCommand.Parameters.AddWithValue(command.IdempotencyKey.Trim());
        databaseCommand.Parameters.AddWithValue(command.Amount);
        databaseCommand.Parameters.AddWithValue(command.Currency.Trim().ToUpperInvariant());
        databaseCommand.Parameters.AddWithValue(command.PaymentMethod.Trim().ToLowerInvariant());
        using NpgsqlDataReader reader = databaseCommand.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Payment intent insert returned no row.");
        return Read(reader);
    }

    public PaymentIntent? Get(Guid id)
    {
        const string sql = """
            SELECT id, restaurant_id, branch_id, order_id, amount, currency, payment_method, status, created_at_utc
            FROM payment_intents WHERE id = $1;
            """;
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using var databaseCommand = new NpgsqlCommand(sql, connection);
        databaseCommand.Parameters.AddWithValue(id);
        using NpgsqlDataReader reader = databaseCommand.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static PaymentIntent Read(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetDecimal(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8));
}
