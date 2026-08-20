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
            SELECT id,organization_id,restaurant_id,branch_id,order_id,amount,currency,payment_method,status,created_at_utc,concurrency_version,provider_authorization_id,failure_code
            FROM payment_intents WHERE organization_id=$1 AND id=$2;
            """;
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using var databaseCommand = new NpgsqlCommand(sql, connection);
        databaseCommand.Parameters.AddWithValue(organizationId);
        databaseCommand.Parameters.AddWithValue(id);
        using NpgsqlDataReader reader = databaseCommand.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public PaymentAuthorizationLease BeginAuthorization(Guid organizationId, Guid id, PaymentMutationContext context)
    {
        ValidateContext(context);
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction();
        PaymentIntent intent = ReadForUpdate(connection, transaction, organizationId, id)
            ?? throw new KeyNotFoundException("Payment intent was not found.");
        if (intent.Status is "authorized" or "authorizing")
        {
            transaction.Commit();
            return new PaymentAuthorizationLease(intent, false);
        }
        if (intent.Status != "pending") throw new InvalidOperationException("Only a pending payment intent can be authorized.");
        using (var command = new NpgsqlCommand("UPDATE payment_intents SET status='authorizing',failure_code=NULL,updated_at_utc=$1,concurrency_version=concurrency_version+1 WHERE organization_id=$2 AND id=$3 AND concurrency_version=$4", connection, transaction))
        {
            command.Parameters.AddWithValue(occurredAt); command.Parameters.AddWithValue(organizationId);
            command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(intent.ConcurrencyVersion);
            if (command.ExecuteNonQuery() != 1) throw new PaymentConcurrencyException("The payment intent changed before authorization started.");
        }
        PaymentIntent authorizing = ReadForUpdate(connection, transaction, organizationId, id)!;
        AppendLifecycle(connection, transaction, authorizing, "payment.authorization.started", context, occurredAt,
            new PaymentAuthorizationStartedV1(Guid.NewGuid(), context.CorrelationId, occurredAt, organizationId,
                authorizing.RestaurantId, authorizing.BranchId, authorizing.OrderId, id, authorizing.Amount, authorizing.Currency));
        transaction.Commit();
        return new PaymentAuthorizationLease(authorizing, true);
    }

    public PaymentIntent CompleteAuthorization(Guid organizationId, Guid id, long expectedVersion, bool succeeded,
        string? providerAuthorizationId, string? failureCode, PaymentMutationContext context)
    {
        ValidateContext(context);
        if (succeeded && string.IsNullOrWhiteSpace(providerAuthorizationId))
            throw new ArgumentException("A successful authorization requires a provider reference.");
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction();
        PaymentIntent intent = ReadForUpdate(connection, transaction, organizationId, id)
            ?? throw new KeyNotFoundException("Payment intent was not found.");
        if (intent.Status == "authorized") { transaction.Commit(); return intent; }
        if (intent.Status != "authorizing" || intent.ConcurrencyVersion != expectedVersion)
            throw new PaymentConcurrencyException("The payment intent changed while authorization was in progress.");
        string status = succeeded ? "authorized" : "failed";
        using (var command = new NpgsqlCommand("UPDATE payment_intents SET status=$1,provider_authorization_id=$2,failure_code=$3,authorized_at_utc=CASE WHEN $1='authorized' THEN $4 ELSE NULL END,failed_at_utc=CASE WHEN $1='failed' THEN $4 ELSE NULL END,updated_at_utc=$4,concurrency_version=concurrency_version+1 WHERE organization_id=$5 AND id=$6 AND concurrency_version=$7", connection, transaction))
        {
            command.Parameters.AddWithValue(status);
            command.Parameters.AddWithValue((object?)(succeeded ? providerAuthorizationId!.Trim() : null) ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)(succeeded ? null : failureCode ?? "provider_declined") ?? DBNull.Value);
            command.Parameters.AddWithValue(occurredAt); command.Parameters.AddWithValue(organizationId);
            command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(expectedVersion);
            if (command.ExecuteNonQuery() != 1) throw new PaymentConcurrencyException("The payment intent changed while authorization completed.");
        }
        PaymentIntent completed = ReadForUpdate(connection, transaction, organizationId, id)!;
        IIntegrationEvent integrationEvent = succeeded
            ? new PaymentAuthorizedV1(Guid.NewGuid(), context.CorrelationId, occurredAt, organizationId, completed.RestaurantId,
                completed.BranchId, completed.OrderId, id, completed.Amount, completed.Currency, completed.PaymentMethod)
            : new PaymentAuthorizationFailedV1(Guid.NewGuid(), context.CorrelationId, occurredAt, organizationId,
                completed.RestaurantId, completed.BranchId, completed.OrderId, id, completed.FailureCode ?? "provider_declined");
        AppendLifecycle(connection, transaction, completed, succeeded ? "payment.authorization.succeeded" : "payment.authorization.failed",
            context, occurredAt, integrationEvent);
        transaction.Commit();
        return completed;
    }

    private static PaymentIntent Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
        reader.GetGuid(3), reader.GetGuid(4), reader.GetDecimal(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
        reader.GetFieldValue<DateTimeOffset>(9), reader.FieldCount > 10 ? reader.GetInt64(10) : 1,
        reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetString(11) : null,
        reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetString(12) : null);

    private static PaymentIntent? ReadForUpdate(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid organizationId, Guid id)
    {
        using var command = new NpgsqlCommand("SELECT id,organization_id,restaurant_id,branch_id,order_id,amount,currency,payment_method,status,created_at_utc,concurrency_version,provider_authorization_id,failure_code FROM payment_intents WHERE organization_id=$1 AND id=$2 FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue(organizationId); command.Parameters.AddWithValue(id);
        using NpgsqlDataReader reader = command.ExecuteReader(); return reader.Read() ? Read(reader) : null;
    }

    private static PaymentIntent? ReadExisting(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid organizationId, Guid restaurantId, string idempotencyKey)
    {
        using var command = new NpgsqlCommand("SELECT id,organization_id,restaurant_id,branch_id,order_id,amount,currency,payment_method,status,created_at_utc,concurrency_version,provider_authorization_id,failure_code FROM payment_intents WHERE organization_id=$1 AND restaurant_id=$2 AND idempotency_key=$3", connection, transaction);
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

    private static void AppendLifecycle(NpgsqlConnection connection, NpgsqlTransaction transaction, PaymentIntent intent,
        string action, PaymentMutationContext context, DateTimeOffset occurredAt, IIntegrationEvent integrationEvent)
    {
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), context.CorrelationId, occurredAt, context.ActorSubjectId.Trim(),
            intent.OrganizationId, action, "payment-intent", intent.Id.ToString("D"), "succeeded");
        InsertAudit(connection, transaction, audit, intent);
        string eventType = integrationEvent switch
        {
            PaymentAuthorizationStartedV1 => "payment.authorization-started.v1",
            PaymentAuthorizedV1 => "payment.authorized.v1",
            PaymentAuthorizationFailedV1 => "payment.authorization-failed.v1",
            _ => throw new InvalidOperationException("Unsupported payment lifecycle event.")
        };
        Enqueue(connection, transaction, integrationEvent.EventId, eventType, intent.Id, integrationEvent, context.CorrelationId, occurredAt);
        Enqueue(connection, transaction, audit.EventId, "payment.audit.v1", intent.Id, audit, context.CorrelationId, occurredAt);
    }

    private static void ValidateContext(PaymentMutationContext context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.ActorSubjectId) || context.ActorSubjectId.Length > 200
            || context.ActorSubjectId.Any(char.IsControl) || context.CorrelationId == Guid.Empty)
            throw new ArgumentException("A valid mutation actor and correlation identifier are required.");
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
