using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Customer.Application.Customers;
using NexaConnect.Services.Customer.Domain;
using Npgsql;

namespace NexaConnect.Services.Customer.Infrastructure;

public sealed class PostgresCustomers(NpgsqlDataSource dataSource) : ICustomers
{
    public async Task<CustomerProfile> CreateAsync(
        CustomerProfileAggregate aggregate,
        CustomerMutationContext context,
        CancellationToken cancellationToken)
    {
        InMemoryCustomers.ValidateContext(context);
        DateTimeOffset occurredAt = aggregate.CreatedAtUtc;
        CustomerProfile candidate = CustomerProfile.From(aggregate);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            INSERT INTO customers
                (id, organization_id, customer_number, identity_subject_id, display_name, status,
                 created_at_utc, created_by, updated_at_utc, updated_by, concurrency_version)
            VALUES ($1,$2,$3,$4,$5,'active',$6,$7,$6,$7,1)
            ON CONFLICT DO NOTHING
            RETURNING id,organization_id,customer_number,display_name,identity_subject_id,status,
                      concurrency_version,created_at_utc;
            """;
        await using var insert = new NpgsqlCommand(sql, connection, transaction);
        insert.Parameters.AddWithValue(candidate.Id);
        insert.Parameters.AddWithValue(candidate.OrganizationId);
        insert.Parameters.AddWithValue(candidate.CustomerNumber);
        insert.Parameters.AddWithValue((object?)candidate.IdentitySubjectId ?? DBNull.Value);
        insert.Parameters.AddWithValue(candidate.DisplayName);
        insert.Parameters.AddWithValue(occurredAt);
        insert.Parameters.AddWithValue(context.ActorSubjectId.Trim());
        await using NpgsqlDataReader reader = await insert.ExecuteReaderAsync(cancellationToken);
        bool created = await reader.ReadAsync(cancellationToken);
        CustomerProfile? result = created ? Read(reader) : null;
        await reader.CloseAsync();

        if (!created)
        {
            result = await ReadByCustomerNumberAsync(connection, transaction, candidate.OrganizationId,
                candidate.CustomerNumber, cancellationToken);
            if (result is null)
                throw new CustomerIdempotencyConflictException(
                    "The identity subject is already associated with a different customer profile.");
            InMemoryCustomers.EnsureSameRequest(result, candidate);
        }
        else
        {
            await InsertAuditAndEventsAsync(connection, transaction, result!, context, occurredAt, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return result!;
    }

    public async Task<CustomerProfile?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,organization_id,customer_number,display_name,identity_subject_id,status,
                   concurrency_version,created_at_utc
            FROM customers
            WHERE organization_id=$1 AND id=$2 AND status<>'anonymized';
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(id);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static async Task<CustomerProfile?> ReadByCustomerNumberAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId,
        string customerNumber,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,organization_id,customer_number,display_name,identity_subject_id,status,
                   concurrency_version,created_at_utc
            FROM customers WHERE organization_id=$1 AND customer_number=$2;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(customerNumber);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static async Task InsertAuditAndEventsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CustomerProfile profile,
        CustomerMutationContext context,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        string actor = context.ActorSubjectId.Trim();
        var created = new CustomerProfileCreatedV1(Guid.NewGuid(), context.CorrelationId, occurredAt,
            profile.OrganizationId, profile.Id, profile.Status, profile.ConcurrencyVersion,
            context.RequestCorrelationId);
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), context.CorrelationId, occurredAt, actor,
            profile.OrganizationId, "customer.profile.created", "customer-profile", profile.Id.ToString("D"),
            "succeeded");
        await using (var command = new NpgsqlCommand(
            "INSERT INTO customer_audit_records(id,organization_id,customer_id,action,actor_subject_id,occurred_at_utc) VALUES($1,$2,$3,$4,$5,$6)",
            connection, transaction))
        {
            command.Parameters.AddWithValue(audit.EventId);
            command.Parameters.AddWithValue(profile.OrganizationId);
            command.Parameters.AddWithValue(profile.Id);
            command.Parameters.AddWithValue(audit.Action);
            command.Parameters.AddWithValue(actor);
            command.Parameters.AddWithValue(occurredAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnqueueAsync(connection, transaction, created.EventId, "customer.profile-created.v1", profile.Id,
            created, context.RequestCorrelationId, occurredAt, cancellationToken);
        await EnqueueAsync(connection, transaction, audit.EventId, "customer.audit.v1", profile.Id,
            audit, context.RequestCorrelationId, occurredAt, cancellationToken);
    }

    private static async Task EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        string eventType,
        Guid aggregateId,
        object payload,
        string correlationId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,$2,1,'customer-profile',$3,$4::jsonb,$5,$6)",
            connection, transaction);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(eventType);
        command.Parameters.AddWithValue(aggregateId);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(payload));
        command.Parameters.AddWithValue(correlationId);
        command.Parameters.AddWithValue(occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CustomerProfile Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1),
        reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
        reader.GetInt64(6), reader.GetFieldValue<DateTimeOffset>(7));
}
