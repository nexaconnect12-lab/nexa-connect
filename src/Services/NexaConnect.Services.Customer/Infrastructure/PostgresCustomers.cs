using NexaConnect.Services.Customer.Application.Customers;
using Npgsql;

namespace NexaConnect.Services.Customer.Infrastructure;

public sealed class PostgresCustomers(NpgsqlDataSource dataSource) : ICustomers
{
    public CustomerProfile Create(CreateCustomer command)
    {
        if (command.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(command.CustomerNumber) || string.IsNullOrWhiteSpace(command.DisplayName))
            throw new ArgumentException("Organization, customer number, and display name are required.");
        const string sql = """
            INSERT INTO customers
                (id, organization_id, customer_number, identity_subject_id, display_name, status,
                 created_at_utc, created_by, updated_at_utc, updated_by)
            VALUES ($1, $2, $3, $4, $5, 'active', now(), 'api', now(), 'api')
            RETURNING id, organization_id, customer_number, display_name, identity_subject_id, status;
            """;
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using var databaseCommand = new NpgsqlCommand(sql, connection);
        databaseCommand.Parameters.AddWithValue(Guid.NewGuid());
        databaseCommand.Parameters.AddWithValue(command.OrganizationId);
        databaseCommand.Parameters.AddWithValue(command.CustomerNumber.Trim());
        databaseCommand.Parameters.AddWithValue((object?)command.IdentitySubjectId ?? DBNull.Value);
        databaseCommand.Parameters.AddWithValue(command.DisplayName.Trim());
        using NpgsqlDataReader reader = databaseCommand.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Customer insert returned no row.");
        return Read(reader);
    }

    public CustomerProfile? Get(Guid organizationId, Guid id)
    {
        const string sql = """
            SELECT id, organization_id, customer_number, display_name, identity_subject_id, status
            FROM customers
            WHERE organization_id = $1 AND id = $2 AND status <> 'anonymized';
            """;
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using var databaseCommand = new NpgsqlCommand(sql, connection);
        databaseCommand.Parameters.AddWithValue(organizationId);
        databaseCommand.Parameters.AddWithValue(id);
        using NpgsqlDataReader reader = databaseCommand.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static CustomerProfile Read(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5));
}
