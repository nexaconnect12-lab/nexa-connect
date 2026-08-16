using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Domain;
using NexaConnect.Contracts.IntegrationEvents;
using Npgsql;
using System.Text.Json;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class PostgresPaymentIntents(NpgsqlDataSource dataSource) : IPaymentIntents
{
    public PaymentIntent Create(Guid organizationId, CreatePaymentIntent command, PaymentMutationContext context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.ActorSubjectId) || context.ActorSubjectId.Length > 200
            || context.ActorSubjectId.Any(char.IsControl) || context.CorrelationId == Guid.Empty)
            throw new ArgumentException("A valid mutation actor and correlation identifier are required.");
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        PaymentIntentAggregate candidate = PaymentIntentAggregate.Create(organizationId, command.RestaurantId, command.BranchId,
            command.OrderId, command.IdempotencyKey, command.Amount, command.Currency, command.PaymentMethod, occurredAt);
        const string sql = """
            INSERT INTO payment_intents
                (id, organization_id, restaurant_id, branch_id, order_id, idempotency_key, amount, currency, payment_method, status, created_at_utc, updated_at_utc)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,'pending',$10,$10)
            ON CONFLICT (organization_id, restaurant_id, idempotency_key) DO NOTHING
            RETURNING id,organization_id,restaurant_id,branch_id,order_id,amount,currency,payment_method,status,created_at_utc;
            """;
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction();
        using var databaseCommand = new NpgsqlCommand(sql, connection, transaction);
        databaseCommand.Parameters.AddWithValue(candidate.Id);
        databaseCommand.Parameters.AddWithValue(candidate.OrganizationId);
        databaseCommand.Parameters.AddWithValue(candidate.RestaurantId);
        databaseCommand.Parameters.AddWithValue(candidate.BranchId);
        databaseCommand.Parameters.AddWithValue(candidate.OrderId);
        databaseCommand.Parameters.AddWithValue(candidate.IdempotencyKey);
        databaseCommand.Parameters.AddWithValue(candidate.Amount);
        databaseCommand.Parameters.AddWithValue(candidate.Currency);
        databaseCommand.Parameters.AddWithValue(candidate.PaymentMethod);
        databaseCommand.Parameters.AddWithValue(occurredAt);
        using NpgsqlDataReader reader = databaseCommand.ExecuteReader();
        bool created = reader.Read();
        PaymentIntent? result = created ? Read(reader) : null;
        reader.Close();
        if (!created)
        {
            result = ReadExisting(connection, transaction, candidate.OrganizationId, candidate.RestaurantId, candidate.IdempotencyKey)
                ?? throw new InvalidOperationException("Payment intent idempotency lookup returned no row.");
            EnsureSameRequest(result, candidate);
        }
        else
        {
            var changed = new PaymentIntentCreatedV1(Guid.NewGuid(), context.CorrelationId, occurredAt, result!.OrganizationId,
                result.RestaurantId, result.BranchId, result.OrderId, result.Id, result.Amount, result.Currency,
                result.PaymentMethod, result.Status);
            var audit = new PlatformAuditEventV1(Guid.NewGuid(), context.CorrelationId, occurredAt, context.ActorSubjectId.Trim(),
                result.OrganizationId, "payment.intent.created", "payment-intent", result.Id.ToString("D"), "succeeded");
            InsertAudit(connection, transaction, audit, result);
            Enqueue(connection, transaction, changed.EventId, "payment.intent-created.v1", result.Id, changed, context.CorrelationId, occurredAt);
            Enqueue(connection, transaction, audit.EventId, "payment.audit.v1", result.Id, audit, context.CorrelationId, occurredAt);
        }
        transaction.Commit();
        return result!;
    }

    public PaymentIntent? Get(Guid organizationId, Guid id)
    {
        const string sql = """
            SELECT id,organization_id,restaurant_id,branch_id,order_id,amount,currency,payment_method,status,created_at_utc
            FROM payment_intents WHERE organization_id=$1 AND id=$2;
            """;
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using var databaseCommand = new NpgsqlCommand(sql, connection);
        databaseCommand.Parameters.AddWithValue(organizationId);
        databaseCommand.Parameters.AddWithValue(id);
        using NpgsqlDataReader reader = databaseCommand.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static PaymentIntent Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
        reader.GetGuid(3), reader.GetGuid(4), reader.GetDecimal(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
        reader.GetFieldValue<DateTimeOffset>(9));

    private static PaymentIntent? ReadExisting(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid organizationId, Guid restaurantId, string idempotencyKey)
    {
        using var command = new NpgsqlCommand("SELECT id,organization_id,restaurant_id,branch_id,order_id,amount,currency,payment_method,status,created_at_utc FROM payment_intents WHERE organization_id=$1 AND restaurant_id=$2 AND idempotency_key=$3", connection, transaction);
        command.Parameters.AddWithValue(organizationId); command.Parameters.AddWithValue(restaurantId); command.Parameters.AddWithValue(idempotencyKey);
        using NpgsqlDataReader reader = command.ExecuteReader(); return reader.Read() ? Read(reader) : null;
    }

    private static void EnsureSameRequest(PaymentIntent existing, PaymentIntentAggregate candidate)
    {
        if (existing.BranchId != candidate.BranchId || existing.OrderId != candidate.OrderId || existing.Amount != candidate.Amount
            || !string.Equals(existing.Currency, candidate.Currency, StringComparison.Ordinal)
            || !string.Equals(existing.PaymentMethod, candidate.PaymentMethod, StringComparison.Ordinal))
            throw new PaymentIdempotencyConflictException("The idempotency key is already associated with a different payment request.");
    }

    private static void InsertAudit(NpgsqlConnection connection, NpgsqlTransaction transaction, PlatformAuditEventV1 audit, PaymentIntent intent)
    {
        using var command = new NpgsqlCommand("INSERT INTO payment_audit_records(id,organization_id,restaurant_id,branch_id,order_id,payment_intent_id,action,actor_subject_id,occurred_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9)", connection, transaction);
        command.Parameters.AddWithValue(audit.EventId); command.Parameters.AddWithValue(intent.OrganizationId); command.Parameters.AddWithValue(intent.RestaurantId);
        command.Parameters.AddWithValue(intent.BranchId); command.Parameters.AddWithValue(intent.OrderId); command.Parameters.AddWithValue(intent.Id);
        command.Parameters.AddWithValue(audit.Action); command.Parameters.AddWithValue(audit.SubjectId); command.Parameters.AddWithValue(audit.OccurredAtUtc); command.ExecuteNonQuery();
    }

    private static void Enqueue(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, string type, Guid aggregateId,
        object payload, Guid correlationId, DateTimeOffset occurredAt)
    {
        using var command = new NpgsqlCommand("INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,$2,1,'payment-intent',$3,$4::jsonb,$5,$6)", connection, transaction);
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(type); command.Parameters.AddWithValue(aggregateId);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(payload)); command.Parameters.AddWithValue(correlationId.ToString("D"));
        command.Parameters.AddWithValue(occurredAt); command.ExecuteNonQuery();
    }
}
